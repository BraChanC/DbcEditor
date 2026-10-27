using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DbcEditor.Models;
using DbcEditor.Services;

namespace DbcEditor.Views;

/// <summary>
/// Vector: Message Signal 'Msg::Sig' —
/// Definition | Message | Signal | Receivers | Value Descriptions | Attributes | Comment
/// Overlapping → tab click shows Invalid Offset.
/// Cancel → stay / keep warning on later tabs; OK → acknowledge, no more prompts this session.
/// </summary>
public partial class MessageSignalEditorWindow : Window
{
    private readonly DbcDatabase _db;
    private readonly DbcMessage _message;
    private readonly DbcSignal _model;
    private int _lastTabIndex;
    private bool _suppressTabWarn;
    private bool _tabsReady;
    private bool _overlapWarningAccepted;

    public MessageSignalEditorVm Vm { get; }

    public MessageSignalEditorWindow(DbcDatabase db, DbcMessage message, DbcSignal model)
    {
        InitializeComponent();
        _db = db;
        _message = message;
        _model = model;
        Vm = new MessageSignalEditorVm(db, message, model);
        DataContext = Vm;
        Title = $"Message Signal '{message.Name}::{model.Name}'";
        Loaded += (_, _) =>
        {
            _lastTabIndex = Tabs.SelectedIndex;
            _tabsReady = true;
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_model);
        Vm.ApplyMessage();
        DialogResult = true;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Vm.ApplyTo(_model);
        Vm.ApplyMessage();
        Title = $"Message Signal '{_message.Name}::{_model.Name}'";
    }

    private void OnInitValueClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        const string body =
            "The initial value is associated with attribute <GenSigStartValue>:\n" +
            "While the initial value displayed here is the physical initial value, the attribute <GenSigStartValue> contains the raw initial value.\n" +
            "Following signal conversion formula is used to transform the raw value to a physical value or in the reverse direction:\n" +
            "[Physical value] = ( [Raw value] * [Factor] ) + [Offset]";

        var text = new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18, 16, 18, 12)
        };
        var ok = new Button
        {
            Content = "确定",
            Width = 80,
            Height = 23,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 12, 12)
        };
        var panel = new DockPanel();
        DockPanel.SetDock(ok, Dock.Bottom);
        panel.Children.Add(ok);
        panel.Children.Add(text);

        var note = new Window
        {
            Title = "Note",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 468,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = panel,
            FontFamily = new FontFamily("Tahoma"),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
        };
        ok.Click += (_, _) => note.Close();
        note.ShowDialog();
    }

    private void OnCalculateMinMax(object sender, RoutedEventArgs e) => Vm.CalculateMinMax();

    private void OnAttributeCommitted(object sender, Controls.AttrCommittedEventArgs e) =>
        Vm.CommitAttribute(e.Name, e.Value);

    private void OnAddReceiver(object sender, RoutedEventArgs e)
    {
        var exclude = Vm.ReceiverNodes.Select(r => r.Name).ToList();
        var dlg = new ChooseObjectsWindow(_db, ChooseObjectsMode.Nodes, excludeNodeNames: exclude)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true || dlg.SelectedNodes.Count == 0) return;
        Vm.AddReceivers(dlg.SelectedNodes);
    }

    private void OnRemoveReceiver(object sender, RoutedEventArgs e) => Vm.RemoveSelectedReceiver();

    private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_tabsReady || _suppressTabWarn) return;
        if (e.Source is not TabControl) return;
        if (sender is not TabControl tc) return;

        // 确定过一次后，本对话框会话内不再提示
        if (_overlapWarningAccepted)
        {
            _lastTabIndex = tc.SelectedIndex;
            return;
        }

        var peer = Vm.FindCurrentOverlapPeer();
        if (peer == null)
        {
            _lastTabIndex = tc.SelectedIndex;
            return;
        }

        var body = "Invalid Offset\n\n" + DbcBitLayout.FormatOverlapWarning(_message, peer);
        var result = MessageBox.Show(
            this,
            body,
            "CANdb++",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            // 取消：回到原 tab，之后再点其他 tab 仍会弹
            _suppressTabWarn = true;
            tc.SelectedIndex = _lastTabIndex;
            _suppressTabWarn = false;
            return;
        }

        // 确定：允许切走，且本会话不再弹
        _overlapWarningAccepted = true;
        _lastTabIndex = tc.SelectedIndex;
    }
}

public sealed class MessageSignalEditorVm : INotifyPropertyChanged
{
    private readonly DbcDatabase _db;
    private readonly DbcMessage _message;
    private readonly DbcSignal _src;
    private MsgNodeRow? _selectedReceiver;

    public DbcDatabase Database => _db;

    private string _messageName = "";
    public string MessageName
    {
        get => _messageName;
        set { _messageName = value; OnPropertyChanged(); }
    }

    public string IdText { get; set; } = "";
    public string IdFormat { get; set; } = "CAN Standard";
    public int Dlc { get; set; }
    public string Transmitter { get; set; } = "";
    public string TxMethod { get; set; } = "";
    public string CycleTime { get; set; } = "";

    private string _signalName = "";
    public string SignalName
    {
        get => _signalName;
        set { _signalName = value; OnPropertyChanged(); }
    }

    public string StartBit { get; set; } = "";

    private string _multiplexorType = "Signal";
    public string MultiplexorType
    {
        get => _multiplexorType;
        set
        {
            _multiplexorType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowMultiplexValue));
        }
    }

    public bool ShowMultiplexValue =>
        (_multiplexorType ?? "").StartsWith("Multiplexed", StringComparison.OrdinalIgnoreCase);

    public string MultiplexValue { get; set; } = "";
    public string Length { get; set; } = "";
    public string ByteOrder { get; set; } = "Motorola";
    public string ValueType { get; set; } = "Unsigned";
    public string Factor { get; set; } = "1";
    public string Offset { get; set; } = "0";
    public string Min { get; set; } = "0";
    public string Max { get; set; } = "0";
    public string Unit { get; set; } = "";
    public string InitValue { get; set; } = "0";
    public string Comment { get; set; } = "";
    public string SelectedValueTable { get; set; } = "<none>";

    public List<string> MultiplexorTypes { get; } = new()
    {
        "Signal", "Multiplexor signal", "Multiplexed Signal"
    };

    public List<string> IdFormats { get; } = new()
    {
        "CAN Standard", "CAN Extended", "CAN FD Standard", "CAN FD Extended"
    };

    public List<int> DlcOptions { get; } = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 32, 48, 64 };
    public List<string> TransmitterOptions { get; } = new();
    public List<string> TxMethods { get; } = new();
    public List<string> ValueTableOptions { get; } = new();

    public List<string> ByteOrders { get; } = new() { "Intel", "Motorola" };
    public List<string> ValueTypes { get; } = new() { "Unsigned", "Signed" };

    public ObservableCollection<MsgNodeRow> ReceiverNodes { get; } = new();
    public ObservableCollection<EditableAttrGroup> AttributeGroups { get; } = new();
    public ObservableCollection<DbcValueDesc> ValueDescriptions { get; } = new();

    public MsgNodeRow? SelectedReceiver
    {
        get => _selectedReceiver;
        set
        {
            _selectedReceiver = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedReceiver));
        }
    }

    public bool HasSelectedReceiver => SelectedReceiver != null;

    public MessageSignalEditorVm(DbcDatabase db, DbcMessage message, DbcSignal s)
    {
        _db = db;
        _message = message;
        _src = s;
        MessageName = message.Name;
        IdText = message.IdHex;
        IdFormat = string.IsNullOrWhiteSpace(message.IdFormat) ? "CAN Standard" : message.IdFormat;
        Dlc = message.Dlc;
        Transmitter = message.Transmitter ?? "";
        TxMethod = string.IsNullOrWhiteSpace(message.TxMethod) ? "n/a" : message.TxMethod;
        CycleTime = string.IsNullOrWhiteSpace(message.CycleTime) ? "0" : message.CycleTime;
        foreach (var n in db.Nodes)
        {
            if (!TransmitterOptions.Contains(n.Name, StringComparer.OrdinalIgnoreCase))
                TransmitterOptions.Add(n.Name);
        }
        if (!string.IsNullOrWhiteSpace(Transmitter) &&
            !TransmitterOptions.Contains(Transmitter, StringComparer.OrdinalIgnoreCase))
            TransmitterOptions.Insert(0, Transmitter);

        var sendDef = db.AttrDefs.FirstOrDefault(a =>
            a.Name.Equals("GenMsgSendType", StringComparison.OrdinalIgnoreCase) &&
            a.ObjectType.Contains("BO_", StringComparison.OrdinalIgnoreCase));
        if (sendDef != null)
        {
            foreach (var ev in sendDef.EnumValues)
                if (!TxMethods.Contains(ev, StringComparer.OrdinalIgnoreCase))
                    TxMethods.Add(ev);
        }
        if (TxMethods.Count == 0)
            TxMethods.AddRange(new[] { "Cycle", "NoMsgSendType", "IfActive", "Event", "CA", "CE" });
        if (!TxMethods.Contains(TxMethod, StringComparer.OrdinalIgnoreCase))
            TxMethods.Insert(0, TxMethod);

        SignalName = s.Name;
        StartBit = DbcBitLayout.VectorStartBit(s).ToString();
        MultiplexorType = ToMultiplexorType(s.Multiplexing);
        MultiplexValue = ToMuxValueText(s.Multiplexing);
        Length = s.Length.ToString();
        ByteOrder = s.IsLittleEndian ? "Intel" : "Motorola";
        ValueType = s.IsSigned ? "Signed" : "Unsigned";
        Factor = s.Factor.ToString(CultureInfo.InvariantCulture);
        Offset = s.Offset.ToString(CultureInfo.InvariantCulture);
        Min = s.Min.ToString(CultureInfo.InvariantCulture);
        Max = s.Max.ToString(CultureInfo.InvariantCulture);
        Unit = s.Unit;
        InitValue = string.IsNullOrWhiteSpace(s.InitialValue) ? "0" : s.InitialValue;
        if (s.Attributes.TryGetValue("GenSigStartValue", out var start) && !string.IsNullOrWhiteSpace(start))
            InitValue = start;
        Comment = s.Comment ?? "";

        ReloadReceivers();
        foreach (var vd in s.ValueDescriptions.OrderBy(x => x.Value))
            ValueDescriptions.Add(vd);

        ValueTableOptions.Add("<none>");
        foreach (var table in db.ValueTables)
        {
            if (!string.IsNullOrWhiteSpace(table.Name))
                ValueTableOptions.Add(table.Name);
        }
        var ownTable = $"VtSig_{s.Name} ({_message.Name}::{s.Name})";
        if (s.ValueDescriptions.Count > 0 &&
            !ValueTableOptions.Contains(ownTable, StringComparer.OrdinalIgnoreCase))
            ValueTableOptions.Add(ownTable);
        SelectedValueTable = s.ValueDescriptions.Count > 0 ? ownTable : "<none>";
        // Vector Message Signal: Attributes empty (not catalog GenSig*)
        AttributeGroups.Add(new EditableAttrGroup { Name = "(no signal attributes)" });
    }

    public DbcSignal? FindCurrentOverlapPeer()
    {
        if (!int.TryParse(Length, out var len) || len <= 0) len = _src.Length;
        var little = ByteOrder == "Intel";
        if (!int.TryParse(StartBit, out var shown)) shown = DbcBitLayout.VectorStartBit(_src);
        var probe = new DbcSignal
        {
            Name = SignalName,
            StartBit = DbcBitLayout.DbcStartFromVector(little, shown, len),
            Length = len,
            IsLittleEndian = little,
            IsSigned = ValueType == "Signed",
            Multiplexing = BuildMultiplexing(MultiplexorType, MultiplexValue, _src.Multiplexing)
        };
        return DbcBitLayout.FindOverlapPeer(probe, _message.Signals, skip: _src);
    }

    public void AddReceivers(IEnumerable<DbcNode> nodes)
    {
        _src.Receivers.RemoveAll(r => r == "Vector__XXX");
        foreach (var n in nodes)
        {
            if (_src.Receivers.Any(r => string.Equals(r, n.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            _src.Receivers.Add(n.Name);
        }
        ReloadReceivers();
    }

    public void RemoveSelectedReceiver()
    {
        if (SelectedReceiver == null) return;
        _src.Receivers.RemoveAll(r =>
            string.Equals(r, SelectedReceiver.Name, StringComparison.OrdinalIgnoreCase));
        if (_src.Receivers.Count == 0)
            _src.Receivers.Add("Vector__XXX");
        SelectedReceiver = null;
        ReloadReceivers();
    }

    public void CommitAttribute(string name, string value) => _src.Attributes[name] = value;

    public void ApplyTo(DbcSignal s)
    {
        s.Name = SignalName?.Trim() ?? s.Name;
        s.Multiplexing = BuildMultiplexing(MultiplexorType, MultiplexValue, s.Multiplexing);
        if (int.TryParse(Length, out var len)) s.Length = len;
        s.IsLittleEndian = ByteOrder == "Intel";
        if (int.TryParse(StartBit, out var shown))
            s.StartBit = DbcBitLayout.DbcStartFromVector(s.IsLittleEndian, shown, s.Length);
        s.IsSigned = ValueType == "Signed";
        if (double.TryParse(Factor, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) s.Factor = f;
        if (double.TryParse(Offset, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) s.Offset = o;
        if (double.TryParse(Min, NumberStyles.Float, CultureInfo.InvariantCulture, out var mi)) s.Min = mi;
        if (double.TryParse(Max, NumberStyles.Float, CultureInfo.InvariantCulture, out var ma)) s.Max = ma;
        s.Unit = Unit;
        s.InitialValue = string.IsNullOrWhiteSpace(InitValue) ? "0" : InitValue.Trim();
        s.Attributes["GenSigStartValue"] = s.InitialValue;
        s.Comment = Comment ?? "";
        s.MessageName = _message.Name;
        s.MessageId = _message.Id;
    }

    public void ApplyMessage()
    {
        var name = MessageName?.Trim() ?? "";
        if (name.Length > 0 && !string.Equals(_message.Name, name, StringComparison.Ordinal))
        {
            _message.Name = name;
            foreach (var sig in _message.Signals)
                sig.MessageName = name;
        }

        _message.IdFormat = string.IsNullOrWhiteSpace(IdFormat) ? _message.IdFormat : IdFormat;
        if (TryParseId(IdText, out var canId))
            _message.Id = DbcMessage.EncodeId(canId, _message.IdFormat);
        _message.Dlc = Dlc;
        _message.Transmitter = Transmitter?.Trim() ?? "";
        _message.TxMethod = TxMethod ?? "";
        _message.CycleTime = string.IsNullOrWhiteSpace(CycleTime) ? "0" : CycleTime.Trim();
        if (!string.IsNullOrEmpty(_message.TxMethod))
            _message.Attributes["GenMsgSendType"] = _message.TxMethod;
        _message.Attributes["GenMsgCycleTime"] = _message.CycleTime;
        var frame = FrameFormatToken(_message.IdFormat);
        if (frame != null)
            _message.Attributes["VFrameFormat"] = frame;
    }

    public void CalculateMinMax()
    {
        if (!int.TryParse(Length, out var len) || len <= 0 || len > 64) return;
        double factor = 1, offset = 0;
        double.TryParse(Factor, NumberStyles.Float, CultureInfo.InvariantCulture, out factor);
        double.TryParse(Offset, NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
        double rawMin = ValueType == "Signed" ? -Math.Pow(2, len - 1) : 0;
        double rawMax = ValueType == "Signed" ? Math.Pow(2, len - 1) - 1 : Math.Pow(2, len) - 1;
        var physMin = rawMin * factor + offset;
        var physMax = rawMax * factor + offset;
        if (physMin > physMax)
            (physMin, physMax) = (physMax, physMin);
        Min = FormatNum(physMin);
        Max = FormatNum(physMax);
        OnPropertyChanged(nameof(Min));
        OnPropertyChanged(nameof(Max));
    }

    private void ReloadReceivers()
    {
        ReceiverNodes.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _src.Receivers)
        {
            if (string.IsNullOrWhiteSpace(r) || r == "Vector__XXX" || !seen.Add(r)) continue;
            var node = _db.FindNode(r);
            ReceiverNodes.Add(new MsgNodeRow { Name = r, Address = node?.Address ?? "0x0" });
        }
    }

    private static string ToMultiplexorType(string multiplexing)
    {
        if (string.IsNullOrWhiteSpace(multiplexing) || multiplexing == "-")
            return "Signal";
        if (multiplexing.Contains("Multiplexed", StringComparison.OrdinalIgnoreCase) ||
            multiplexing.StartsWith("m", StringComparison.OrdinalIgnoreCase))
            return "Multiplexed Signal";
        if (multiplexing.Contains("Multiplexor", StringComparison.OrdinalIgnoreCase) ||
            multiplexing.Equals("M", StringComparison.OrdinalIgnoreCase))
            return "Multiplexor signal";
        return "Signal";
    }

    private static string ToMuxValueText(string multiplexing)
    {
        var m = System.Text.RegularExpressions.Regex.Match(multiplexing ?? "", @"(\d+)");
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var n)) return "0x0";
        return $"0x{n:X}";
    }

    private static string BuildMultiplexing(string type, string valueText, string previous)
    {
        if (type.Equals("Multiplexor signal", StringComparison.OrdinalIgnoreCase))
            return "Multiplexor";
        if (type.StartsWith("Multiplexed", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseMuxValue(valueText, out var n))
            {
                var keep = System.Text.RegularExpressions.Regex.Match(previous ?? "", @"(\d+)");
                if (!keep.Success || !int.TryParse(keep.Groups[1].Value, out n))
                    return "Multiplexed signal";
            }
            var text = $"Multiplexed signal {n}";
            if ((previous ?? "").Contains("Multiplexor", StringComparison.OrdinalIgnoreCase) &&
                !(previous ?? "").Equals("Multiplexor", StringComparison.OrdinalIgnoreCase))
                text += " / Multiplexor";
            return text;
        }
        return "";
    }

    private static bool TryParseMuxValue(string text, out int n)
    {
        text = (text ?? "").Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
    }

    private static bool TryParseId(string text, out uint canId)
    {
        text = (text ?? "").Trim();
        if (text.EndsWith("x", StringComparison.OrdinalIgnoreCase))
            text = text[..^1];
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out canId);
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out canId);
    }

    private static string? FrameFormatToken(string display) => display switch
    {
        "CAN Standard" => "StandardCAN",
        "CAN Extended" => "ExtendedCAN",
        "CAN FD Standard" => "StandardCAN_FD",
        "CAN FD Extended" => "ExtendedCAN_FD",
        _ => null
    };

    private static string FormatNum(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
        var rounded = Math.Round(v);
        if (Math.Abs(v - rounded) < 1e-6 && Math.Abs(v) < 1e15)
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        return v.ToString("0.########", CultureInfo.InvariantCulture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
