using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using DbcEditor.Services;

namespace DbcEditor.Models;

public sealed class EditableAttrGroup
{
    public string Name { get; set; } = "";
    public FontWeight NameWeight => FontWeights.SemiBold;
    public string Value => "";
    public bool IsGroup => true;
    public ObservableCollection<EditableAttrItem> Children { get; } = new();
}

public sealed class EditableAttrItem : INotifyPropertyChanged, IAttrListItem
{
    private string _value = "";
    private bool _isEditing;
    private bool _isDefault;

    public string Name { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string ValueType { get; init; } = "";
    public bool IsEnum { get; init; }
    public List<string> EnumOptions { get; init; } = new();
    public bool IsGroup => false;
    public FontWeight NameWeight => FontWeights.Normal;
    public ObservableCollection<EditableAttrItem> Children { get; } = new();

    /// <summary>UI value without trailing * (HEX shown as 0x…).</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (_isDefault == value) return;
            _isDefault = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    /// <summary>Vector list: default values get a trailing '*'.</summary>
    public string DisplayValue => IsDefault ? Value + "*" : Value;

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowText));
            OnPropertyChanged(nameof(ShowCombo));
            OnPropertyChanged(nameof(ShowTextBox));
        }
    }

    public bool ShowText => !IsEditing;
    public bool ShowCombo => IsEditing && IsEnum;
    public bool ShowTextBox => IsEditing && !IsEnum;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public static EditableAttrItem FromDef(
        DbcDatabase db, string objectType, string name, string rawValue, bool isDefault)
    {
        var def = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.ObjectType, objectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        def ??= db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        var isEnum = def != null &&
                     def.ValueType.Equals("ENUM", StringComparison.OrdinalIgnoreCase) &&
                     def.EnumValues.Count > 0;
        var options = isEnum ? def!.EnumValues.Distinct().ToList() : new List<string>();

        var display = rawValue ?? "";
        // Map numeric enum index → name if needed
        if (isEnum && int.TryParse(display, out var idx) && idx >= 0 && idx < options.Count)
            display = options[idx];
        else
            display = AttributeValueDisplay.Format(def, display);

        var defaultOk = isDefault || AttributeValueDisplay.ValuesEqual(def, display, def?.DefaultValue);

        return new EditableAttrItem
        {
            Name = name,
            ObjectType = objectType,
            ValueType = def?.ValueType ?? "",
            IsEnum = isEnum,
            EnumOptions = options,
            Value = display,
            IsDefault = defaultOk
        };
    }
}
