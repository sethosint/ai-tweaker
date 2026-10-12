using System.Text.Json;
using System.Text.Json.Serialization;

namespace AITweaker.Core.Model;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(RegistryAction), "registry")]
[JsonDerivedType(typeof(ServiceAction), "service")]
[JsonDerivedType(typeof(ScheduledTaskAction), "task")]
[JsonDerivedType(typeof(PowerSettingAction), "powercfg")]
[JsonDerivedType(typeof(PowerSchemeAction), "powerScheme")]
[JsonDerivedType(typeof(BcdAction), "bcd")]
[JsonDerivedType(typeof(CommandAction), "command")]
[JsonDerivedType(typeof(PowerShellAction), "powershell")]
[JsonDerivedType(typeof(DeviceAction), "device")]
[JsonDerivedType(typeof(NicPropertyAction), "nic")]
[JsonDerivedType(typeof(MsiModeAction), "msi")]
[JsonDerivedType(typeof(InterruptAffinityAction), "affinity")]
[JsonDerivedType(typeof(DirectXSettingAction), "directx")]
[JsonDerivedType(typeof(AppxRemoveAction), "appx")]
[JsonDerivedType(typeof(DisplayRefreshAction), "display")]
public abstract class TweakAction
{
    /// <summary>Optional per-action condition (e.g. different values on laptops).</summary>
    public string? When { get; init; }

    public abstract string Describe();
}

[JsonConverter(typeof(JsonStringEnumConverter<RegistryValueKind2>))]
public enum RegistryValueKind2
{
    Dword,
    Qword,
    String,
    Expand,
    Multi,
    Binary,
}

/// <summary>
/// Sets or deletes a registry value. <see cref="Path"/> may contain <c>*</c> segments which expand to every
/// existing subkey (e.g. all network interfaces); wildcard paths never create keys.
/// </summary>
public sealed class RegistryAction : TweakAction
{
    public required string Path { get; init; }
    /// <summary>Value name. Empty string = the key's default value.</summary>
    public string Name { get; init; } = "";
    public RegistryValueKind2 Kind { get; init; } = RegistryValueKind2.Dword;
    public JsonElement? Value { get; init; }
    /// <summary>Delete the value instead of setting it.</summary>
    public bool Delete { get; init; }
    /// <summary>Only touch keys that already exist (never create the key).</summary>
    public bool OnlyIfKeyExists { get; init; }

    public override string Describe() => Delete
        ? $"reg delete {Path} /v {Name}"
        : $"reg set {Path} \\ {(Name.Length == 0 ? "(default)" : Name)} = {Value?.ToString()} ({Kind})";
}

[JsonConverter(typeof(JsonStringEnumConverter<ServiceStartup>))]
public enum ServiceStartup
{
    Disabled,
    Manual,
    Automatic,
    Delayed,
}

public sealed class ServiceAction : TweakAction
{
    public required string Name { get; init; }
    public ServiceStartup Startup { get; init; } = ServiceStartup.Disabled;
    /// <summary>Stop the service right away (only meaningful for Disabled/Manual).</summary>
    public bool Stop { get; init; } = true;

    public override string Describe() => $"service {Name} -> {Startup}";
}

public sealed class ScheduledTaskAction : TweakAction
{
    /// <summary>Full task path, e.g. <c>\Microsoft\Windows\Application Experience\ProgramDataUpdater</c>.</summary>
    public required string Path { get; init; }
    public bool Enabled { get; init; }

    public override string Describe() => $"task {Path} -> {(Enabled ? "enabled" : "disabled")}";
}

/// <summary>Writes a power setting index on the active power scheme (AC and/or DC).</summary>
public sealed class PowerSettingAction : TweakAction
{
    public required string Subgroup { get; init; }
    public required string Setting { get; init; }
    public uint? Ac { get; init; }
    public uint? Dc { get; init; }
    public string? Alias { get; init; }

    public override string Describe() => $"powercfg {Alias ?? Setting} AC={Ac?.ToString() ?? "-"} DC={Dc?.ToString() ?? "-"}";
}

/// <summary>Activates (creating if needed) a power scheme and optionally sets the power mode overlay.</summary>
public sealed class PowerSchemeAction : TweakAction
{
    /// <summary>ultimate | high | balanced</summary>
    public string Scheme { get; init; } = "high";
    /// <summary>Optional power mode overlay: best-performance | balanced | efficiency.</summary>
    public string? Overlay { get; init; }

    public override string Describe() => $"power scheme -> {Scheme}{(Overlay is null ? "" : $" (overlay {Overlay})")}";
}

public sealed class BcdAction : TweakAction
{
    public required string Element { get; init; }
    /// <summary>Null = delete the element.</summary>
    public string? Value { get; init; }

    public override string Describe() => Value is null ? $"bcdedit /deletevalue {Element}" : $"bcdedit /set {Element} {Value}";
}

public sealed class CommandAction : TweakAction
{
    public required string File { get; init; }
    public string Args { get; init; } = "";
    public string? UndoFile { get; init; }
    public string? UndoArgs { get; init; }

    public override string Describe() => $"{File} {Args}";
}

public sealed class PowerShellAction : TweakAction
{
    public required string Script { get; init; }
    public string? Undo { get; init; }
    /// <summary>Optional script that prints <c>1</c> when the tweak is already in effect.</summary>
    public string? Check { get; init; }

    public override string Describe() => $"powershell: {Script}";
}

/// <summary>Disables (or enables) PnP devices whose instance id starts with one of <see cref="Match"/>.</summary>
public sealed class DeviceAction : TweakAction
{
    public required List<string> Match { get; init; }
    public bool Enabled { get; init; }

    public override string Describe() => $"device {string.Join(", ", Match)} -> {(Enabled ? "enabled" : "disabled")}";
}

/// <summary>Sets NIC advanced properties (driver registry keywords) when the adapter exposes them.</summary>
public sealed class NicPropertyAction : TweakAction
{
    /// <summary>ethernet | wifi | all</summary>
    public string Target { get; init; } = "all";
    public required Dictionary<string, string> Keywords { get; init; }

    public override string Describe() => $"nic[{Target}] {string.Join(", ", Keywords.Select(k => $"{k.Key}={k.Value}"))}";
}

/// <summary>Enables Message Signaled Interrupts for a device class.</summary>
public sealed class MsiModeAction : TweakAction
{
    /// <summary>gpu | nic</summary>
    public required string Target { get; init; }

    public override string Describe() => $"MSI mode -> {Target}";
}

/// <summary>Pins device interrupts to dedicated performance cores (see <see cref="Affinity.InterruptPlanner"/>).</summary>
public sealed class InterruptAffinityAction : TweakAction
{
    public List<string> Targets { get; init; } = ["gpu", "nic", "usb"];

    public override string Describe() => $"interrupt affinity -> {string.Join(", ", Targets)}";
}

/// <summary>Merges <c>Key=Value;</c> into HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings.</summary>
public sealed class DirectXSettingAction : TweakAction
{
    public required string Key { get; init; }
    public required string Value { get; init; }

    public override string Describe() => $"DirectX {Key}={Value}";
}

public sealed class AppxRemoveAction : TweakAction
{
    public required List<string> Packages { get; init; }

    public override string Describe() => $"remove appx {string.Join(", ", Packages)}";
}

/// <summary>Switches every active display to its highest supported refresh rate at the current resolution.</summary>
public sealed class DisplayRefreshAction : TweakAction
{
    public override string Describe() => "display -> max refresh rate";
}
