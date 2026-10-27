using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DbcEditor.Models;
using DbcEditor.Services;

namespace DbcEditor.Controls;

/// <summary>
/// Vector CANdb++ / BUSMASTER DbcViewPro bit layout:
/// bytes as rows, bits as columns; Inverted / Tiled|Paged; msb↔lsb arrows; red selection bar.
/// </summary>
public partial class BitLayoutControl : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(DbcMessage), typeof(BitLayoutControl),
            new FrameworkPropertyMetadata(null, OnMessageChanged));

    public static readonly DependencyProperty SelectedSignalProperty =
        DependencyProperty.Register(nameof(SelectedSignal), typeof(DbcSignal), typeof(BitLayoutControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedChanged));

    public DbcMessage? Message
    {
        get => (DbcMessage?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public DbcSignal? SelectedSignal
    {
        get => (DbcSignal?)GetValue(SelectedSignalProperty);
        set => SetValue(SelectedSignalProperty, value);
    }

    public event Action<DbcSignal>? SignalActivated;
    public event Action? EditSignalRequested;
    public event Action? AddSignalRequested;
    public event Action? RemoveSignalRequested;
    /// <summary>Raised after Layout To Front / To Back / drag changes StartBit positions.</summary>
    public event Action? LayoutSignalsChanged;

    /// <summary>
    /// Absolute bit last clicked on the grid (empty cell or signal). Used by Layout Add...
    /// </summary>
    public int PendingInsertAbsBit { get; private set; } = -1;

    /// <summary>Paint / overlap order (later = on top / owns shared bits). Survives redraws.</summary>
    private readonly List<DbcSignal> _zOrder = new();

    private const int BytesPerPage = 8;
    private const int MinCellW = 28;
    private const int MinCellH = 33; // ~22 * 1.5
    /// <summary>Readable row height; when all rows exceed viewport, keep this and scroll (Vector).</summary>
    private const int PrefCellH = 42; // ~28 * 1.5
    private const int NameBand = 14;
    private const int ArrowBand = 14;
    private const int LeftGutter = 28; /* byte index column inside canvas */

    private static readonly Color[] SigColors =
    {
        Color.FromRgb(120, 200, 90), Color.FromRgb(80, 190, 180), Color.FromRgb(90, 210, 100),
        Color.FromRgb(255, 170, 70), Color.FromRgb(90, 140, 230), Color.FromRgb(200, 100, 200),
        Color.FromRgb(255, 200, 60), Color.FromRgb(100, 220, 120), Color.FromRgb(180, 120, 255),
        Color.FromRgb(255, 120, 140), Color.FromRgb(70, 180, 200), Color.FromRgb(220, 160, 80),
        Color.FromRgb(140, 180, 255), Color.FromRgb(255, 150, 200), Color.FromRgb(100, 210, 150),
        Color.FromRgb(240, 140, 60)
    };

    private int _dlc;
    private int _page;
    private int _selSig = -1;
    private bool _inverted; /* Vector default: Inverted unchecked */
    private bool _paged;
    private int[] _owner = Array.Empty<int>();
    private List<int>[] _sigBits = Array.Empty<List<int>>();
    private string[] _labels = Array.Empty<string>();
    private bool[] _nameTruncated = Array.Empty<bool>();
    private int _tipSig = -1;
    private double _cellW = 52, _cellH = 36;
    private double _topHeader = 52;
    private DateTime _lastClick;
    private int _lastClickSig = -2;
    private bool _redrawing;
    private readonly List<TextBlock> _nameLabels = new();

    private Point _mouseDownPt;
    private int _mouseDownSig = -1;
    private int _mouseDownAbsBit = -1;
    private bool _dragging;
    private int _dragOrigStartBit = -1;
    private HashSet<DbcSignal> _overlap = new();

    public BitLayoutControl()
    {
        InitializeComponent();
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) =>
        {
            if (Scroller != null)
                Scroller.SizeChanged += (_, _) => Redraw();
            UpdateLayoutButtons();
            Redraw();
        };
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (BitLayoutControl)d;
        c._page = 0;
        c._selSig = -1;
        c.PendingInsertAbsBit = -1;
        c.SyncZOrder();
        c.RebuildMap();
        c.UpdateLayoutButtons();
        c.Redraw();
    }

    private static void OnSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (BitLayoutControl)d;
        var msg = c.Message;
        if (msg == null || e.NewValue is not DbcSignal sg)
        {
            c._selSig = -1;
        }
        else
        {
            c._selSig = msg.Signals.IndexOf(sg);
        }
        c.UpdateStatus();
        c.UpdateLayoutButtons();
        c.Redraw();
    }

    private void SyncZOrder()
    {
        var msg = Message;
        if (msg == null)
        {
            _zOrder.Clear();
            return;
        }
        _zOrder.RemoveAll(s => !msg.Signals.Contains(s));
        foreach (var s in msg.Signals)
        {
            if (!_zOrder.Contains(s))
                _zOrder.Add(s);
        }
    }

    private void UpdateLayoutButtons()
    {
        bool hasSel = SelectedSignal != null;
        var ordered = hasSel ? LayoutOrder() : null;
        int idx = ordered != null ? ordered.IndexOf(SelectedSignal!) : -1;
        bool front = ordered != null && idx > 0;
        bool back = ordered != null && idx >= 0 && idx < ordered.Count - 1;
        if (BtnToFront != null) BtnToFront.IsEnabled = front;
        if (BtnToBack != null) BtnToBack.IsEnabled = back;
        if (BtnRemove != null) BtnRemove.IsEnabled = hasSel;
        if (BtnArrange != null) BtnArrange.IsEnabled = Message?.Signals.Count > 0;
    }

    /// <summary>Signals ordered by first occupied bit (layout front → back).</summary>
    private List<DbcSignal> LayoutOrder()
    {
        var msg = Message;
        if (msg == null) return new List<DbcSignal>();
        return msg.Signals
            .OrderBy(DbcBitLayout.MinOccupiedBit)
            .ThenBy(s => s.StartBit)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Vector Layout Add: place signal at the last clicked byte row.</summary>
    public void ApplyInsertPosition(DbcSignal sg)
    {
        if (PendingInsertAbsBit < 0) return;
        if (sg.Length <= 0) sg.Length = 8;
        sg.StartBit = DbcBitLayout.StartBitForByteInsert(sg, PendingInsertAbsBit);
    }

    private void OnArrangeClick(object sender, RoutedEventArgs e)
    {
        var msg = Message;
        if (msg == null || msg.Signals.Count == 0) return;

        /* Vector: refuse auto-arrange when Σ lengths > 64 (classic CAN). */
        int occupiedBits = 0;
        foreach (var s in msg.Signals)
            occupiedBits += Math.Max(0, s.Length);
        if (occupiedBits > 64)
        {
            MessageBox.Show(
                $"The signals in the message '{msg.Name}' occupy more than 64 bits and therefore cannot be arranged automatically.",
                "CANdb++",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DbcBitLayout.ArrangeSignals(msg.Signals);
        SyncZOrder();
        _zOrder.Clear();
        _zOrder.AddRange(msg.Signals);
        RebuildMap();
        Redraw();
        UpdateLayoutButtons();
        UpdateStatus();
        LayoutSignalsChanged?.Invoke();
        if (SelectedSignal != null)
        {
            SelectedSignal = SelectedSignal;
            _selSig = msg.Signals.IndexOf(SelectedSignal);
        }
    }

    private void OnToFrontClick(object sender, RoutedEventArgs e) => ToFrontOrBack(-1);

    private void OnToBackClick(object sender, RoutedEventArgs e) => ToFrontOrBack(+1);

    /// <summary>
    /// Vector CANdb++ 3.1, Radar_Header numTxObjs, recorded in order:
    /// To Front across a hole → start 42, covered MisAlign follows at 50.
    /// To Back onto the signal after the LSB → that follower moves in front, numTxObjs follows (MisAlign 42–40, numTxObjs 55–48).
    /// To Back across a hole → Timestamp slides back by 8 (96–72), numTxObjs takes the tail 111–104.
    /// To Front while the previous bit is occupied → whole swap back (79–72, Timestamp 104–80).
    /// </summary>
    private void ToFrontOrBack(int delta)
    {
        var sg = SelectedSignal;
        var msg = Message;
        if (sg == null || msg == null) return;

        var ordered = LayoutOrder();
        int bitIdx = ordered.IndexOf(sg);
        if (bitIdx < 0) return;

        if (delta < 0)
            MoveTowardFront(sg, msg, ordered, bitIdx);
        else
            MoveTowardBack(sg, msg, ordered);

        SyncZOrder();
        _zOrder.Clear();
        _zOrder.AddRange(msg.Signals);
        _zOrder.Remove(sg);
        _zOrder.Add(sg);

        SelectedSignal = sg;
        _selSig = msg.Signals.IndexOf(sg);
        RebuildMap();
        Redraw();
        UpdateLayoutButtons();
        UpdateStatus();
        LayoutSignalsChanged?.Invoke();
        SelectedSignal = sg;
        _selSig = msg.Signals.IndexOf(sg);
        UpdateLayoutButtons();
        UpdateStatus();
        Redraw();
    }

    /// <summary>
    /// Previous bit occupied: repack this signal in front of that neighbor.
    /// Previous bit empty: step onto previous.StartBit - 1; signals under the new footprint follow.
    /// </summary>
    private static void MoveTowardFront(DbcSignal sg, DbcMessage msg, List<DbcSignal> ordered, int bitIdx)
    {
        int before = DbcBitLayout.StepOutside(sg, -1);
        var touch = DbcBitLayout.SignalAtBit(msg.Signals, before, sg);
        if (touch != null)
        {
            DbcBitLayout.RepackAdjacentPair(sg, touch);
            SwapList(msg, sg, touch);
            return;
        }

        if (bitIdx <= 0) return;
        var neighbor = ordered[bitIdx - 1];
        int newStart = Math.Max(0, neighbor.StartBit - 1);
        var probe = new DbcSignal
        {
            StartBit = newStart,
            Length = sg.Length,
            IsLittleEndian = sg.IsLittleEndian
        };
        var displaced = msg.Signals
            .Where(o => !ReferenceEquals(o, sg) && o.Length > 0 && DbcBitLayout.Overlaps(probe, o))
            .OrderBy(DbcBitLayout.MinOccupiedBit)
            .ToList();

        sg.StartBit = newStart;
        var ahead = sg;
        foreach (var other in displaced)
        {
            other.StartBit = DbcBitLayout.StartBitRightAfter(ahead, other);
            ahead = other;
        }
    }

    /// <summary>
    /// Next bit occupied: whole interchange, the follower goes in front
    /// (MisAlign 50 → 42/41/40, numTxObjs → 55–48). Do not cover the signal ahead.
    /// Next bit empty: slide the next signal back by this length and take its old tail
    /// (Timestamp 87→79, numTxObjs → 111).
    /// </summary>
    private static void MoveTowardBack(DbcSignal sg, DbcMessage msg, List<DbcSignal> ordered)
    {
        int after = DbcBitLayout.StepOutside(sg, +1);
        var follower = DbcBitLayout.SignalAtBit(msg.Signals, after, sg);
        if (follower != null)
        {
            DbcBitLayout.RepackAdjacentPair(follower, sg);
            SwapList(msg, sg, follower);
            return;
        }

        int sgMax = DbcBitLayout.MaxOccupiedBit(sg);
        var next = ordered.FirstOrDefault(s =>
            !ReferenceEquals(s, sg) && s.Length > 0 && DbcBitLayout.MinOccupiedBit(s) > sgMax);
        if (next == null) return;

        int oldNext = next.StartBit;
        int oldLen = next.Length;
        next.StartBit = Math.Max(0, oldNext - sg.Length);
        sg.StartBit = Math.Max(0, oldNext + oldLen - sg.Length);
        SwapList(msg, sg, next);
    }

    private static void SwapList(DbcMessage msg, DbcSignal a, DbcSignal b)
    {
        int i1 = msg.Signals.IndexOf(a);
        int i2 = msg.Signals.IndexOf(b);
        if (i1 < 0 || i2 < 0 || i1 == i2) return;
        (msg.Signals[i1], msg.Signals[i2]) = (b, a);
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddSignalRequested?.Invoke();

    private void OnRemoveClick(object sender, RoutedEventArgs e) => RemoveSignalRequested?.Invoke();

    private void OnChromeChanged(object sender, RoutedEventArgs e)
    {
        // RadioButton IsChecked=True fires Checked during InitializeComponent
        // before later named fields (RadioPaged / PageNav) exist.
        if (ChkInvert == null || RadioPaged == null || PageNav == null)
            return;

        _inverted = ChkInvert.IsChecked == true;
        _paged = RadioPaged.IsChecked == true;
        PageNav.Visibility = _paged ? Visibility.Visible : Visibility.Collapsed;
        if (_paged && _page >= PageCount()) _page = Math.Max(0, PageCount() - 1);
        UpdatePageLabel();
        Redraw();
    }

    private void OnPrevPage(object sender, RoutedEventArgs e)
    {
        if (_page > 0) { _page--; UpdatePageLabel(); Redraw(); }
    }

    private void OnNextPage(object sender, RoutedEventArgs e)
    {
        if (_page + 1 < PageCount()) { _page++; UpdatePageLabel(); Redraw(); }
    }

    private void OnEditSignalClick(object sender, RoutedEventArgs e) =>
        EditSignalRequested?.Invoke();

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Paged: wheel flips pages. Tiled: let ScrollViewer scroll vertically.
        if (!_paged) return;
        if (e.Delta > 0) OnPrevPage(sender, e);
        else OnNextPage(sender, e);
        e.Handled = true;
    }

    private int PageCount() => _dlc <= 0 ? 1 : (_dlc + BytesPerPage - 1) / BytesPerPage;

    private int FirstVisibleByte() => _paged ? _page * BytesPerPage : 0;

    private void UpdatePageLabel()
    {
        if (LblPage == null || BtnPrev == null || BtnNext == null) return;
        int pages = PageCount();
        LblPage.Text = $"{(_page + 1)}/{Math.Max(1, pages)}";
        BtnPrev.IsEnabled = _paged && _page > 0;
        BtnNext.IsEnabled = _paged && _page + 1 < pages;
    }

    private void RebuildMap()
    {
        var msg = Message;
        _dlc = 0;
        _owner = Array.Empty<int>();
        _sigBits = Array.Empty<List<int>>();
        _labels = Array.Empty<string>();
        _nameTruncated = Array.Empty<bool>();
        if (msg == null) return;

        SyncZOrder();

        _dlc = msg.Dlc;
        if (_dlc <= 0) _dlc = 8;
        if (_dlc > 64) _dlc = 64;

        int total = _dlc * 8;
        _owner = Enumerable.Repeat(-1, total).ToArray();
        _sigBits = new List<int>[msg.Signals.Count];
        _labels = new string[msg.Signals.Count];
        _nameTruncated = new bool[msg.Signals.Count];
        for (int s = 0; s < msg.Signals.Count; s++)
        {
            _labels[s] = msg.Signals[s].Name;
            _sigBits[s] = DbcBitLayout.CollectBits(msg.Signals[s]).ToList();
        }
        // Later in z-order wins overlapping bits for paint
        foreach (var sg in _zOrder)
        {
            int s = msg.Signals.IndexOf(sg);
            if (s < 0 || s >= _sigBits.Length) continue;
            foreach (var b in _sigBits[s])
            {
                if (b >= 0 && b < total) _owner[b] = s;
            }
        }
        _overlap = DbcBitLayout.FindOverlappingSignals(msg.Signals);
    }

    /// <summary>Vector / BUSMASTER bit walk — see <see cref="DbcBitLayout.CollectBits"/>.</summary>
    public static IEnumerable<int> CollectBits(DbcSignal sg) => DbcBitLayout.CollectBits(sg);

    /// <summary>
    /// Vector: signal blocks always sit on sawtooth columns (bit7 left … bit0 right).
    /// Inverted only remaps the printed index / column header (absBit ⊕ 7 within byte).
    /// </summary>
    private static int BitInByteToCol(int bitInByte) => 7 - bitInByte;
    private static int ColToBitInByte(int col) => 7 - col;
    private static int DisplayBitIndex(int absBit) => absBit ^ 7;

    private void Redraw()
    {
        if (!IsLoaded || _redrawing) return;
        _redrawing = true;
        try
        {
            RebuildMap();
            UpdatePageLabel();
            UpdateStatus();
            HideNameTip();

            double hostW = Scroller?.ViewportWidth ?? 0;
            double hostH = Scroller?.ViewportHeight ?? 0;
            if (hostW < 80)
                hostW = Math.Max(ActualWidth - 118, 320);
            if (hostH < 80)
                hostH = Math.Max(ActualHeight - 40, 240);

            _topHeader = 44;
            int first = FirstVisibleByte();
            int rowCount;
            if (_paged)
            {
                rowCount = Math.Max(1, Math.Min(BytesPerPage, Math.Max(0, _dlc - first)));
            }
            else
            {
                first = 0;
                rowCount = Math.Max(1, _dlc);
            }
            int last = first + rowCount;

            // Horizontal: always stretch 8 columns to window width (no H-scrollbar)
            double canvasW = hostW;
            _cellW = Math.Max(1.0, (canvasW - LeftGutter) / 8.0);

            // Vertical: readable row height; taller than viewport → V-scrollbar
            double neededH = _topHeader + rowCount * (double)PrefCellH;
            if (_paged || neededH <= hostH)
            {
                _cellH = Math.Max(MinCellH, (hostH - _topHeader) / rowCount);
                neededH = hostH; // fill page / short message
            }
            else
            {
                _cellH = PrefCellH; // 23 rows etc.: keep height, scroll
            }

            double canvasH = _topHeader + rowCount * _cellH;
            if (_paged)
                canvasH = Math.Max(hostH, canvasH);

            DrawHost.Width = canvasW;
            DrawHost.Height = canvasH;
            DrawHost.Children.Clear();
            _nameLabels.Clear();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                PaintGrid(dc, first, last);

            var host = new VisualHost(dv)
            {
                Width = canvasW,
                Height = canvasH,
                IsHitTestVisible = false
            };
            DrawHost.Children.Add(host);

            PlaceSignalNameLabels(first, last);
        }
        finally
        {
            _redrawing = false;
        }
    }

    private void PaintGrid(DrawingContext dc, int first, int last)
    {
        var typeface = new Typeface(new FontFamily("Tahoma"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        /* Multiplexor caption */
        DrawText(dc, "Multiplexor Signal:  -- No Multiplexor --",
            typeface, 11, Color.FromRgb(40, 40, 40), LeftGutter, 8, dpi);

        /* Column headers: Normal 7..0; Inverted 0..7 — blocks stay put */
        for (int col = 0; col < 8; col++)
        {
            int label = _inverted ? col : 7 - col;
            var ft = Fmt(label.ToString(CultureInfo.InvariantCulture), typeface, 11, Color.FromRgb(50, 50, 50), dpi);
            double x = LeftGutter + col * _cellW + (_cellW - ft.Width) / 2;
            dc.DrawText(ft, new Point(x, _topHeader - 18));
        }

        var cellPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        var lightPen = new Pen(new SolidColorBrush(Color.FromRgb(170, 170, 170)), 1);
        cellPen.Freeze(); lightPen.Freeze();

        for (int byteIdx = first; byteIdx < last; byteIdx++)
        {
            double y = _topHeader + (byteIdx - first) * _cellH;

            /* Byte index */
            var by = Fmt(byteIdx.ToString(CultureInfo.InvariantCulture), typeface, 11, Color.FromRgb(40, 40, 40), dpi);
            dc.DrawText(by, new Point(LeftGutter - 4 - by.Width, y + (_cellH - by.Height) / 2));

            for (int col = 0; col < 8; col++)
            {
                int absBit = byteIdx * 8 + ColToBitInByte(col);
                int si = absBit >= 0 && absBit < _owner.Length ? _owner[absBit] : -1;
                var cell = new Rect(LeftGutter + col * _cellW, y, _cellW, _cellH);

                Color fill = si < 0 ? Color.FromRgb(252, 252, 252) : SigColors[si % SigColors.Length];
                if (si >= 0 && si == _selSig)
                    fill = Color.FromRgb(
                        (byte)Math.Max(0, fill.R - 20),
                        (byte)Math.Max(0, fill.G - 20),
                        (byte)Math.Max(0, fill.B - 20));

                var brush = new SolidColorBrush(fill);
                brush.Freeze();
                dc.DrawRectangle(brush, lightPen, cell);
                dc.DrawLine(cellPen, new Point(cell.Left, cell.Bottom), new Point(cell.Right, cell.Bottom));
                dc.DrawLine(cellPen, new Point(cell.Right, cell.Top), new Point(cell.Right, cell.Bottom));

                bool endCell = false;
                if (si >= 0 && si < _sigBits.Length && _sigBits[si].Count > 1)
                {
                    GetMsbLsb(si, out int msb, out int lsb);
                    if (absBit == msb || absBit == lsb) endCell = true;
                }
                int shown = _inverted ? DisplayBitIndex(absBit) : absBit;
                var bn = Fmt(shown.ToString(CultureInfo.InvariantCulture), typeface, 10,
                    si >= 0 ? Color.FromRgb(70, 70, 70) : Color.FromRgb(180, 180, 180), dpi);
                double bx = endCell
                    ? cell.Left + cell.Width / 2 + (cell.Width / 2 - bn.Width) - 3
                    : cell.Right - bn.Width - 3;
                dc.DrawText(bn, new Point(bx, cell.Bottom - bn.Height - 5));
            }
        }

        for (int s = 0; s < _sigBits.Length; s++)
            DrawSignalMsbLsbArrow(dc, typeface, dpi, s, first, last);
        if (_selSig >= 0)
            DrawSelectionRed(dc, _selSig, first, last);
        else if (PendingInsertAbsBit >= 0)
            DrawInsertRowHighlight(dc, PendingInsertAbsBit, first, last);

        /* Vector: yellow ! on overlapping signals (allowed, but warned). */
        var msg = Message;
        if (msg != null)
        {
            for (int s = 0; s < msg.Signals.Count; s++)
            {
                if (_overlap.Contains(msg.Signals[s]))
                    DrawOverlapMark(dc, s, first, last);
            }
        }
    }

    private void DrawInsertRowHighlight(DrawingContext dc, int absBit, int first, int last)
    {
        int byteIdx = absBit / 8;
        if (byteIdx < first || byteIdx >= last) return;
        double y = _topHeader + (byteIdx - first) * _cellH;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(220, 0, 0)), 2);
        pen.Freeze();
        dc.DrawRectangle(null, pen, new Rect(LeftGutter, y, 8 * _cellW, _cellH));
    }

    private void DrawOverlapMark(DrawingContext dc, int sigIndex, int first, int last)
    {
        if (sigIndex < 0 || sigIndex >= _sigBits.Length || _sigBits[sigIndex].Count == 0) return;
        if (!TryGetNameBand(sigIndex, first, last, out int nameRow, out int drawMin, out _))
            return;
        double x = LeftGutter + drawMin * _cellW + 2;
        double y = _topHeader + nameRow * _cellH + 2;
        var fill = new SolidColorBrush(Color.FromRgb(255, 193, 7));
        fill.Freeze();
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(183, 28, 28)), 0.8);
        stroke.Freeze();
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(x + 6, y), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(x + 12, y + 11), true));
        fig.Segments.Add(new LineSegment(new Point(x, y + 11), true));
        geo.Figures.Add(fig);
        dc.DrawGeometry(fill, stroke, geo);
        var bang = Fmt("!", new Typeface("Tahoma"), 8, Color.FromRgb(183, 28, 28),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(bang, new Point(x + 6 - bang.Width / 2, y + 2));
    }

    /// <summary>
    /// Place one TextBlock per signal on the first contiguous row of its cells.
    /// Uses TextTrimming — never FormattedText.MaxTextWidth (that wraps and looks overlapped).
    /// </summary>
    private void PlaceSignalNameLabels(int first, int last)
    {
        for (int i = 0; i < _nameTruncated.Length; i++)
            _nameTruncated[i] = false;

        for (int s = 0; s < _labels.Length; s++)
        {
            if (s >= _sigBits.Length || _sigBits[s].Count == 0) continue;
            if (!TryGetNameBand(s, first, last, out int nameRow, out int drawMin, out int drawMax))
                continue;

            double nameX = LeftGutter + drawMin * _cellW + 2;
            double nameW = Math.Max(4, (drawMax - drawMin + 1) * _cellW - 4);
            double nameY = _topHeader + nameRow * _cellH + 1;
            double bandH = Math.Min(NameBand + 2, Math.Max(14, _cellH - ArrowBand - 2));

            var tb = new TextBlock
            {
                Text = _labels[s],
                FontFamily = new FontFamily("Tahoma"),
                FontSize = 10,
                Foreground = Brushes.Black,
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false
            };
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
            tb.Measure(new Size(double.PositiveInfinity, bandH));
            _nameTruncated[s] = tb.DesiredSize.Width > nameW + 0.5;
            tb.Width = nameW;
            tb.Height = bandH;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(tb, nameX);
            Canvas.SetTop(tb, nameY);
            Panel.SetZIndex(tb, 10);
            DrawHost.Children.Add(tb);
            _nameLabels.Add(tb);
        }
    }

    private bool TryGetNameBand(int s, int first, int last, out int nameRow, out int drawMin, out int drawMax)
    {
        nameRow = 0;
        drawMin = 0;
        drawMax = -1;
        int minRow = 9999;
        bool any = false;
        foreach (var absBit in _sigBits[s])
        {
            int byteIdx = absBit / 8;
            if (byteIdx < first || byteIdx >= last) continue;
            minRow = Math.Min(minRow, byteIdx - first);
            any = true;
        }
        if (!any) return false;

        nameRow = minRow;
        int byteIdxRow = first + minRow;
        int runStart = -1, runEnd = -1;
        for (int col = 0; col < 8; col++)
        {
            int absBit = byteIdxRow * 8 + ColToBitInByte(col);
            int si = absBit < _owner.Length ? _owner[absBit] : -1;
            if (si == s)
            {
                if (runStart < 0) runStart = col;
                runEnd = col;
            }
            else if (runStart >= 0) break;
        }
        if (runStart < 0) return false;
        drawMin = runStart;
        drawMax = runEnd;
        return drawMax >= drawMin;
    }

    private void GetMsbLsb(int sigIndex, out int msbBit, out int lsbBit)
    {
        msbBit = lsbBit = -1;
        if (sigIndex < 0 || sigIndex >= _sigBits.Length || _sigBits[sigIndex].Count == 0) return;
        var bits = _sigBits[sigIndex];
        msbBit = bits[0];
        lsbBit = bits[^1];
        var msg = Message;
        if (msg != null && sigIndex < msg.Signals.Count && msg.Signals[sigIndex].IsLittleEndian)
        {
            msbBit = bits[^1];
            lsbBit = bits[0];
        }
    }

    private bool BitToCell(int absBit, int first, int last, out Rect cell)
    {
        cell = default;
        int byteIdx = absBit / 8;
        if (byteIdx < first || byteIdx >= last) return false;
        int row = byteIdx - first;
        int col = BitInByteToCol(absBit % 8);
        cell = new Rect(LeftGutter + col * _cellW, _topHeader + row * _cellH, _cellW, _cellH);
        return true;
    }

    private void DrawSignalMsbLsbArrow(DrawingContext dc, Typeface typeface, double dpi,
        int sigIndex, int first, int last)
    {
        if (sigIndex < 0 || sigIndex >= _sigBits.Length || _sigBits[sigIndex].Count == 0) return;
        GetMsbLsb(sigIndex, out int msbBit, out int lsbBit);
        if (msbBit < 0 || lsbBit < 0) return;

        bool multiBit = msbBit != lsbBit && _sigBits[sigIndex].Count > 1;
        var bits = _sigBits[sigIndex];

        int[] colMin = Enumerable.Repeat(8, 64).ToArray();
        int[] colMax = Enumerable.Repeat(-1, 64).ToArray();
        int minByte = 9999, maxByte = -1;
        foreach (var absBit in bits)
        {
            int byteIdx = absBit / 8;
            if (byteIdx < 0 || byteIdx >= 64) continue;
            if (!BitToCell(absBit, first, last, out _)) continue;
            int col = BitInByteToCol(absBit % 8);
            minByte = Math.Min(minByte, byteIdx);
            maxByte = Math.Max(maxByte, byteIdx);
            colMin[byteIdx] = Math.Min(colMin[byteIdx], col);
            colMax[byteIdx] = Math.Max(colMax[byteIdx], col);
        }
        if (maxByte < minByte) return;

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(180, 40, 40)), 1);
        pen.Freeze();
        for (int byteIdx = minByte; byteIdx <= maxByte; byteIdx++)
        {
            if (colMax[byteIdx] < colMin[byteIdx]) continue;
            double xL = LeftGutter + colMin[byteIdx] * _cellW + 3;
            double xR = LeftGutter + (colMax[byteIdx] + 1) * _cellW - 3;
            if (xR < xL + 8)
            {
                double mid = (xL + xR) / 2;
                xL = mid - 10; xR = mid + 10;
            }
            int row = byteIdx - first;
            double yArrow = _topHeader + (row + 1) * _cellH - 4;
            dc.DrawLine(pen, new Point(xL, yArrow), new Point(xR, yArrow));
            dc.DrawLine(pen, new Point(xL, yArrow), new Point(xL + 4, yArrow - 3));
            dc.DrawLine(pen, new Point(xL, yArrow), new Point(xL + 4, yArrow + 3));
            dc.DrawLine(pen, new Point(xR, yArrow), new Point(xR - 4, yArrow - 3));
            dc.DrawLine(pen, new Point(xR, yArrow), new Point(xR - 4, yArrow + 3));
        }

        if (!multiBit) return;
        var red = Color.FromRgb(160, 0, 0);
        if (BitToCell(msbBit, first, last, out var msbCell))
        {
            var ft = Fmt("msb", typeface, 9, red, dpi);
            dc.DrawText(ft, new Point(msbCell.Left + 2, msbCell.Bottom - 4 - ft.Height - 1));
        }
        if (BitToCell(lsbBit, first, last, out var lsbCell))
        {
            var ft = Fmt("lsb", typeface, 9, red, dpi);
            dc.DrawText(ft, new Point(lsbCell.Left + 2, lsbCell.Bottom - 4 - ft.Height - 1));
        }
    }

    private void DrawSelectionRed(DrawingContext dc, int sigIndex, int first, int last)
    {
        if (sigIndex < 0 || sigIndex >= _sigBits.Length) return;
        int[] colMin = Enumerable.Repeat(8, 64).ToArray();
        int[] colMax = Enumerable.Repeat(-1, 64).ToArray();
        double[] rowTop = Enumerable.Repeat(-1.0, 64).ToArray();
        int minByte = 9999, maxByte = -1;
        foreach (var absBit in _sigBits[sigIndex])
        {
            int byteIdx = absBit / 8;
            if (byteIdx < 0 || byteIdx >= 64) continue;
            if (!BitToCell(absBit, first, last, out var cell)) continue;
            int col = BitInByteToCol(absBit % 8);
            minByte = Math.Min(minByte, byteIdx);
            maxByte = Math.Max(maxByte, byteIdx);
            colMin[byteIdx] = Math.Min(colMin[byteIdx], col);
            colMax[byteIdx] = Math.Max(colMax[byteIdx], col);
            rowTop[byteIdx] = cell.Top;
        }
        if (maxByte < minByte) return;

        var red = new SolidColorBrush(Color.FromRgb(220, 0, 0));
        red.Freeze();
        for (int byteIdx = minByte; byteIdx <= maxByte; byteIdx++)
        {
            if (colMax[byteIdx] < colMin[byteIdx] || rowTop[byteIdx] < 0) continue;
            double x1 = LeftGutter + colMin[byteIdx] * _cellW;
            double x2 = LeftGutter + (colMax[byteIdx] + 1) * _cellW;
            dc.DrawRectangle(red, null, new Rect(x1, rowTop[byteIdx], x2 - x1, 4));
        }
    }

    private void UpdateStatus()
    {
        if (StatusTip == null) return;
        var msg = Message;
        if (_selSig >= 0 && msg != null && _selSig < msg.Signals.Count)
        {
            var sg = msg.Signals[_selSig];
            GetMsbLsb(_selSig, out int msb, out int lsb);
            StatusTip.Foreground = new SolidColorBrush(Color.FromRgb(160, 0, 0));
            StatusTip.Text =
                $"Selected: {sg.Name} | start={DbcBitLayout.VectorStartBit(sg)} len={sg.Length} " +
                $"{(sg.IsLittleEndian ? "Intel" : "Motorola")} {(sg.IsSigned ? "Signed" : "Unsigned")} | " +
                $"msb={msb} lsb={lsb} | unit={(string.IsNullOrEmpty(sg.Unit) ? "-" : sg.Unit)} | " +
                $"{(_inverted ? "Inverted" : "Normal")} / {(_paged ? "Paged" : "Tiled")}";
        }
        else
        {
            StatusTip.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            if (PendingInsertAbsBit >= 0)
            {
                StatusTip.Foreground = new SolidColorBrush(Color.FromRgb(0, 90, 160));
                StatusTip.Text =
                    $"Insert at byte {PendingInsertAbsBit / 8} (bit {PendingInsertAbsBit}). Add... places a signal here. Drag signals freely; overlaps show !.";
            }
            else
            {
                StatusTip.Text =
                    "Click a byte row then Add.... Drag signals to move (overlaps allowed, warned with !).";
            }
        }
    }

    private void OnCanvasClick(object sender, MouseButtonEventArgs e)
    {
        /* MouseLeftButtonDown — start potential drag or click. */
        Focus();
        var p = e.GetPosition(DrawHost);
        _mouseDownPt = p;
        _mouseDownAbsBit = HitAbsBit(p);
        _mouseDownSig = HitTest(p);
        _dragging = false;
        _dragOrigStartBit = -1;
        if (_mouseDownSig >= 0 && Message != null && _mouseDownSig < Message.Signals.Count)
            _dragOrigStartBit = Message.Signals[_mouseDownSig].StartBit;
        DrawHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(DrawHost);
        var msg = Message;
        if (_dragging && _mouseDownSig >= 0 && msg != null && _mouseDownSig < msg.Signals.Count)
        {
            /* Vector: drag freely — overlaps allowed; warnings shown after. */
            int drop = HitAbsBit(p);
            if (drop >= 0)
            {
                var sg = msg.Signals[_mouseDownSig];
                sg.StartBit = DbcBitLayout.StartBitForDrop(sg, drop);
            }
            SelectedSignal = msg.Signals[_mouseDownSig];
            _selSig = _mouseDownSig;
            PendingInsertAbsBit = drop >= 0 ? drop : PendingInsertAbsBit;
            RebuildMap();
            Redraw();
            UpdateStatus();
            UpdateLayoutButtons();
            LayoutSignalsChanged?.Invoke();
        }
        else
        {
            FinishClickSelect(p);
        }

        _dragging = false;
        _mouseDownSig = -1;
        _dragOrigStartBit = -1;
        if (DrawHost.IsMouseCaptured)
            DrawHost.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void FinishClickSelect(Point p)
    {
        int hit = HitTest(p);
        int abs = HitAbsBit(p);
        var now = DateTime.UtcNow;
        bool dbl = hit >= 0 && hit == _lastClickSig && (now - _lastClick).TotalMilliseconds < 400;
        _lastClick = now;
        _lastClickSig = hit;

        if (abs >= 0)
            PendingInsertAbsBit = abs;

        if (hit == _selSig && !dbl)
            _selSig = -1;
        else
            _selSig = hit;

        var msg = Message;
        SelectedSignal = (_selSig >= 0 && msg != null && _selSig < msg.Signals.Count)
            ? msg.Signals[_selSig] : null;

        if (dbl && SelectedSignal != null)
            SignalActivated?.Invoke(SelectedSignal);

        UpdateStatus();
        UpdateLayoutButtons();
        Redraw();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(DrawHost);
        if (e.LeftButton == MouseButtonState.Pressed && _mouseDownSig >= 0 && Message != null
            && _mouseDownSig < Message.Signals.Count)
        {
            double dx = p.X - _mouseDownPt.X;
            double dy = p.Y - _mouseDownPt.Y;
            if (!_dragging && (dx * dx + dy * dy) >= 16)
                _dragging = true;

            if (_dragging)
            {
                HideNameTip();
                int drop = HitAbsBit(p);
                if (drop >= 0)
                {
                    var sg = Message.Signals[_mouseDownSig];
                    int next = DbcBitLayout.StartBitForDrop(sg, drop);
                    if (sg.StartBit != next)
                    {
                        sg.StartBit = next;
                        _selSig = _mouseDownSig;
                        SelectedSignal = sg;
                        RebuildMap();
                        Redraw();
                    }
                }
                return;
            }
        }

        if (NameTip == null || NameTipText == null) return;
        int hit = HitTest(p);
        if (hit >= 0 && hit < _nameTruncated.Length && _nameTruncated[hit] &&
            hit < _labels.Length && !string.IsNullOrEmpty(_labels[hit]))
        {
            if (_tipSig != hit)
            {
                _tipSig = hit;
                NameTipText.Text = _labels[hit];
            }
            if (!NameTip.IsOpen)
                NameTip.IsOpen = true;
            NameTip.HorizontalOffset = 12;
            NameTip.VerticalOffset = 14;
        }
        else
        {
            HideNameTip();
        }
    }

    private void OnCanvasMouseLeave(object sender, MouseEventArgs e) => HideNameTip();

    private void HideNameTip()
    {
        _tipSig = -1;
        if (NameTip != null && NameTip.IsOpen)
            NameTip.IsOpen = false;
    }

    private int HitAbsBit(Point pt)
    {
        int first = FirstVisibleByte();
        int last = _paged ? Math.Min(_dlc, first + BytesPerPage) : _dlc;
        if (pt.Y < _topHeader || pt.X < LeftGutter || _cellH <= 0 || _cellW <= 0) return -1;
        int row = (int)((pt.Y - _topHeader) / _cellH);
        int col = (int)((pt.X - LeftGutter) / _cellW);
        if (col < 0 || col > 7) return -1;
        int byteIdx = first + row;
        if (byteIdx < first || byteIdx >= last) return -1;
        return byteIdx * 8 + ColToBitInByte(col);
    }

    private int HitTest(Point pt)
    {
        int absBit = HitAbsBit(pt);
        if (absBit < 0 || absBit >= _owner.Length) return -1;
        return _owner[absBit];
    }

    private static FormattedText Fmt(string text, Typeface tf, double size, Color c, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size,
            new SolidColorBrush(c), dpi);

    private static void DrawText(DrawingContext dc, string text, Typeface tf, double size, Color c,
        double x, double y, double dpi)
    {
        dc.DrawText(Fmt(text, tf, size, c, dpi), new Point(x, y));
    }

    private sealed class VisualHost : FrameworkElement
    {
        private readonly DrawingVisual _visual;
        public VisualHost(DrawingVisual visual)
        {
            _visual = visual;
            AddVisualChild(_visual);
        }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;
        protected override Size MeasureOverride(Size availableSize) =>
            new(Width > 0 ? Width : availableSize.Width, Height > 0 ? Height : availableSize.Height);
        protected override Size ArrangeOverride(Size finalSize) => finalSize;
    }
}
