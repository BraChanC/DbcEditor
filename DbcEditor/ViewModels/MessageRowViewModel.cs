using CommunityToolkit.Mvvm.ComponentModel;
using DbcEditor.Models;

namespace DbcEditor.ViewModels;

public partial class MessageRowViewModel : ObservableObject
{
    public DbcMessage Model { get; }

    public MessageRowViewModel(DbcMessage model) => Model = model;

    public string Message => Model.Name;
    public string Node => Model.Transmitter;
    public string Id => Model.IdHex;
    public string IdFormat => Model.IdFormat;
    public int Dlc => Model.Dlc;
    public string TxMethod => string.IsNullOrEmpty(Model.TxMethod) ? "n/a" : Model.TxMethod;
    public string CycleTime => string.IsNullOrEmpty(Model.CycleTime) ? "-" : Model.CycleTime;
    public string Transmitter => Model.Transmitter;
    public string Comment => Model.Comment;
    public string NmMessage => AttrYesNo("NmMessage");
    public string DiagState => AttrYesNo("DiagState");

    private string AttrYesNo(string name)
    {
        if (Model.Attributes.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            return v switch
            {
                "0" => "No",
                "1" => "Yes",
                _ => v
            };
        }
        return "No";
    }
}
