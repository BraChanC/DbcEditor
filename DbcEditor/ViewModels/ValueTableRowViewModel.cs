using DbcEditor.Models;

namespace DbcEditor.ViewModels;

/// <summary>Right-pane row when a VtSig_* / VtEv_* tree node is selected (Vector Overall View).</summary>
public sealed class ValueTableRowViewModel
{
    public DbcSignal? Signal { get; }
    public DbcEnvVar? EnvVar { get; }

    public ValueTableRowViewModel(DbcSignal signal)
    {
        Signal = signal;
        Name = $"VtSig_{signal.Name}";
        // Vector Overall View: Comment empty; Usage = Message::Signal
        Comment = signal.ValueTableComment ?? "";
        Usage = string.IsNullOrWhiteSpace(signal.MessageName)
            ? signal.Name
            : $"{signal.MessageName}::{signal.Name}";
    }

    public ValueTableRowViewModel(DbcEnvVar env)
    {
        EnvVar = env;
        Name = $"VtEv_{env.Name}";
        Comment = env.ValueTableComment ?? "";
        Usage = env.Name;
    }

    public string Name { get; }
    public string Comment { get; }
    /// <summary>Vector column: Message::Signal (header may show as U..).</summary>
    public string Usage { get; }
}
