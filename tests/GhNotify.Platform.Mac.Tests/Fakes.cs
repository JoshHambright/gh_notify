using GhNotify.Core;

namespace GhNotify.Platform.Mac.Tests;

/// <summary>A test that needs macOS (the real shim); skipped elsewhere.</summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute() { if (!OperatingSystem.IsMacOS()) Skip = "macOS only"; }
}

sealed class FakeShim : IMacShim
{
    public int InitStatus { get; set; }
    public int ShowStatus { get; set; }
    public int HotkeyStatus { get; set; }
    public int LoginStatus { get; set; }
    public int LoginSetStatus { get; set; }
    public Action<string, string?>? OnClick { get; private set; }
    public Action? OnHotkey { get; private set; }
    public List<string> Calls { get; } = [];

    public int Version() => NativeShim.ApiVersion;

    public int NotifyInit(Action<string, string?> onClick)
    {
        OnClick = onClick;
        return InitStatus;
    }

    public int NotifyShow(string tag, string title, string body, string? url, bool silent)
    {
        Calls.Add($"show {tag} {title} | {body} | {url ?? "-"} | {(silent ? "silent" : "sound")}");
        return ShowStatus;
    }

    public void NotifyRemove(string tag) => Calls.Add($"remove {tag}");
    public void NotifyClearAll() => Calls.Add("clear");

    public int HotkeyRegister(uint keyCode, uint carbonModifiers, Action onPressed)
    {
        Calls.Add($"hotkey 0x{keyCode:X2} 0x{carbonModifiers:X4}");
        OnHotkey = onPressed;
        return HotkeyStatus;
    }

    public void HotkeyUnregister() => Calls.Add("unhotkey");
    public int LoginItemStatus() => LoginStatus;

    public int LoginItemSet(bool enabled)
    {
        Calls.Add($"login {enabled}");
        return LoginSetStatus;
    }
}

sealed class FakeRunner : IProcessRunner
{
    public List<ProcessSpec> Started { get; } = [];
    public Func<ProcessSpec, ProcessResult> Result { get; set; } = _ => new ProcessResult(0, "", "", false);

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default) => Task.FromResult(Result(spec));
    public void StartDetached(ProcessSpec spec) => Started.Add(spec);
}
