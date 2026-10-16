using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac.Tests;

public class MacServicesTests
{
    [Fact]
    public void Notifier_shows_removes_and_translates_clicks_to_the_Windows_action_strings()
    {
        var shim = new FakeShim();
        var notifier = new MacNotifier(shim);
        notifier.Apply(new ToastRequest(ToastAction.Show, "gh:review:acme/api#1", "Review requested", "acme/api #1", "https://github.com/acme/api/pull/1"));
        notifier.Apply(new ToastRequest(ToastAction.Show, "t2", null, null) { Silent = true });
        notifier.Apply(new ToastRequest(ToastAction.Remove, "t2"));
        notifier.ClearAll();
        Assert.Equal([
            "show gh:review:acme/api#1 Review requested | acme/api #1 | https://github.com/acme/api/pull/1 | sound",
            "show t2 gh_notify |  | - | silent",
            "remove t2",
            "clear",
        ], shim.Calls);

        var got = new List<(string, string)>();
        notifier.Activated += (tag, action) => got.Add((tag, action));
        shim.OnClick!("gh:review:acme/api#1", "https://github.com/acme/api/pull/1");
        shim.OnClick!("t2", null);
        Assert.Equal([("gh:review:acme/api#1", ToastActions.ForUrl("https://github.com/acme/api/pull/1")), ("t2", ToastActions.Open)], got);
    }

    [Fact]
    public void A_click_before_anyone_listens_is_replayed_to_the_first_handler()
    {
        var shim = new FakeShim();
        var notifier = new MacNotifier(shim);
        shim.OnClick!("launch", null); // the click that started the app
        var got = new List<string>();
        notifier.Activated += (tag, _) => got.Add(tag);
        notifier.Activated += (tag, _) => got.Add("second " + tag);
        Assert.Equal(["launch"], got);
    }

    [Fact]
    public void Without_notification_support_nothing_reaches_the_shim()
    {
        var shim = new FakeShim { InitStatus = -1 };
        var notifier = new MacNotifier(shim);
        notifier.Apply(new ToastRequest(ToastAction.Show, "t", "x", "y"));
        notifier.ClearAll();
        Assert.Empty(shim.Calls);
    }

    [Fact]
    public void Hotkey_maps_modifiers_and_reports_a_taken_combination()
    {
        var shim = new FakeShim();
        var service = new MacHotkeyService(shim);
        Assert.True(HotkeyParser.TryParse("Ctrl+Option+G", out var hk));
        Assert.True(service.TryRegister(hk, () => { }, out var error));
        Assert.Null(error);
        Assert.True(HotkeyParser.TryParse("Cmd+Shift+F5", out var hk2));
        shim.HotkeyStatus = MacHotkeyService.EventHotKeyExistsErr;
        Assert.False(service.TryRegister(hk2, () => { }, out error));
        Assert.Contains("already registered", error);
        Assert.Equal(["hotkey 0x05 0x1800", "unhotkey", "hotkey 0x60 0x0300"], shim.Calls);
        service.Unregister(); // nothing registered now: no second unregister
        Assert.Equal(3, shim.Calls.Count);
    }

    [Fact]
    public void Every_key_the_parser_accepts_has_a_Carbon_key_code()
    {
        var keys = Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
            .Concat(Enumerable.Range(0, 10).Select(d => d.ToString()))
            .Concat(Enumerable.Range(1, 12).Select(f => "F" + f));
        foreach (var key in keys)
        {
            Assert.True(HotkeyParser.TryParse("Ctrl+" + key, out var hk), key);
            MacKeyCodes.Translate(hk); // throws KeyNotFoundException for a missing code
        }
        Assert.Equal(48, MacKeyCodes.KnownKeys.Count);
        Assert.Equal(48, MacKeyCodes.KnownKeys.Select(k => MacKeyCodes.Translate(new Hotkey(HotkeyModifiers.Ctrl, k)).KeyCode).Distinct().Count());
    }

    [Theory]
    [InlineData(0, false, null)]
    [InlineData(1, true, null)]
    [InlineData(2, false, MacStartupService.ApprovalHint)]
    [InlineData(3, false, null)]
    public void Login_item_status_maps_to_enabled_and_a_problem(int status, bool enabled, string? problem)
    {
        var service = new MacStartupService(new FakeShim { LoginStatus = status });
        Assert.Equal(enabled, service.IsEnabled);
        Assert.Equal(problem, service.Problem);
    }

    [Fact]
    public void Login_item_read_error_is_reported_and_set_goes_to_the_shim()
    {
        var shim = new FakeShim { LoginStatus = -1, LoginSetStatus = -1 };
        var service = new MacStartupService(shim);
        Assert.Contains("could not be read", service.Problem);
        service.SetEnabled(true); // logged, not thrown
        Assert.Equal(["login True"], shim.Calls);
    }

    [Fact]
    public void Sounds_extract_built_ins_once_and_fall_back_to_Ping()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ghn-macsound-" + Guid.NewGuid().ToString("N"));
        try
        {
            var runner = new FakeRunner();
            var sounds = new MacSoundService(runner, dir);
            var chime = sounds.Resolve("builtin:Chime");
            Assert.Equal(Path.Combine(dir, "chime.wav"), chime);
            Assert.True(new FileInfo(chime!).Length > 1000);
            Assert.Equal(chime, sounds.Resolve("builtin:chime"));
            Assert.Null(sounds.Resolve("builtin:kazoo"));
            Assert.Null(sounds.Resolve("default"));
            Assert.Null(sounds.Resolve(Path.Combine(dir, "missing.wav")));
            sounds.Play("default");
            sounds.Play("builtin:chime");
            Assert.Equal([MacSoundService.Fallback, chime!], runner.Started.Select(s => s.Args[0]));
            Assert.All(runner.Started, s => Assert.Equal("afplay", s.FileName));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Shell_opens_https_urls_and_folders_with_open_and_refuses_other_urls()
    {
        var runner = new FakeRunner();
        var shell = new MacShell(runner, () => "terminal");
        shell.OpenUrl("https://github.com/acme/api/pull/1");
        shell.OpenUrl("file:///etc/passwd");
        shell.OpenFolder("/Users/alice/dev");
        shell.OpenTerminal("/Users/alice/dev", "claude --resume abc");
        Assert.Equal(["open https://github.com/acme/api/pull/1", "open /Users/alice/dev", "osascript"],
            runner.Started.Select(s => s.FileName == "open" ? "open " + s.Args[0] : s.FileName));
    }

    [Fact]
    public void Shell_falls_back_to_Terminal_when_the_setting_cannot_be_read()
    {
        var runner = new FakeRunner();
        new MacShell(runner, () => throw new IOException("locked")).OpenTerminal("/tmp", "ls");
        Assert.Contains(runner.Started.Single().Args, a => a.Contains("tell application \"Terminal\""));
    }

    [Fact]
    public void NoOp_services_explain_why()
    {
        Assert.False(new NoOpHotkeyService("no shim").TryRegister(new Hotkey(HotkeyModifiers.Ctrl, "G"), () => { }, out var error));
        Assert.Equal("no shim", error);
        var startup = new NoOpStartupService("no shim");
        Assert.False(startup.IsEnabled);
        Assert.Contains("no shim", startup.Problem);
    }
}
