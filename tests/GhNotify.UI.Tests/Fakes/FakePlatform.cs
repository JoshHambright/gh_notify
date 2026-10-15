using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.UI.Tests;

public sealed class FakeNotifier : INotifier
{
    public List<ToastRequest> Requests { get; } = [];
    public int Cleared { get; private set; }
    public event Action<string, string>? Activated;

    public void Apply(ToastRequest request) => Requests.Add(request);
    public void ClearAll() => Cleared++;
    public void Activate(string tag, string action) => Activated?.Invoke(tag, action);
}

public sealed class FakeHotkeyService : IHotkeyService
{
    public bool Succeeds = true;
    public Exception? Throws;
    public readonly List<string> Calls = [];
    public Action? Pressed;

    public bool TryRegister(Hotkey hotkey, Action onPressed, out string? error)
    {
        Calls.Add("register " + hotkey);
        if (Throws is not null) throw Throws;
        Pressed = onPressed;
        error = Succeeds ? null : "taken";
        return Succeeds;
    }

    public void Unregister() => Calls.Add("unregister");
}

public sealed class FakeStartupService : IStartupService
{
    public bool IsEnabled { get; private set; }
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}

public sealed class FakeSoundService : ISoundService
{
    public List<string> Played { get; } = [];
    public void Play(string soundIdOrPath) => Played.Add(soundIdOrPath);
}

public sealed class FakeSystemTheme : ISystemTheme
{
    bool _dark = true;

    public bool IsDark
    {
        get => _dark;
        set
        {
            _dark = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}

public sealed class FakeShell : IShell
{
    public List<string> Urls { get; } = [];
    public List<string> Folders { get; } = [];
    public List<(string Dir, string Command)> Terminals { get; } = [];

    public void OpenUrl(string httpsUrl) => Urls.Add(httpsUrl);
    public void OpenFolder(string path) => Folders.Add(path);
    public void OpenTerminal(string workingDir, string command) => Terminals.Add((workingDir, command));
}

/// <summary>Runs posted work immediately, on the calling thread.</summary>
public sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>A full set of fakes, keeping typed handles to each.</summary>
public sealed class FakePlatform
{
    public FakeNotifier Notifier { get; } = new();
    public FakeHotkeyService Hotkey { get; } = new();
    public FakeStartupService Startup { get; } = new();
    public FakeSoundService Sound { get; } = new();
    public FakeSystemTheme Theme { get; } = new();
    public FakeShell Shell { get; } = new();

    public PlatformServices Services => new(Notifier, Hotkey, Startup, Sound, Theme, Shell);
}
