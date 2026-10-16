using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class HotkeyAndActionsConfigTests
{
    static string WriteConfig(string json)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ghn-cfg").FullName, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Defaults_are_win_shift_g_and_repo_default_merge_method()
    {
        var c = new AppConfig();
        Assert.Equal(AppConfig.DefaultHotkey(OperatingSystem.IsMacOS()), c.Hotkey);
        Assert.Null(c.Actions.MergeMethod);
        Assert.Empty(ConfigValidator.Errors(c));
    }

    [Fact]
    public void V12_config_without_the_new_keys_loads_with_defaults()
    {
        var store = new ConfigStore(WriteConfig("""{ "pollSeconds": 90, "github": { "watchRepos": ["acme/api"] }, "updates": { "checkHours": 12 } }"""));
        store.Load();
        Assert.Null(store.Error);
        Assert.Equal(90, store.Current.PollSeconds);
        Assert.Equal(AppConfig.DefaultHotkey(OperatingSystem.IsMacOS()), store.Current.Hotkey);
        Assert.Null(store.Current.Actions.MergeMethod);
    }

    [Theory]
    [InlineData("squash", MergeMethod.Squash)]
    [InlineData("merge", MergeMethod.Merge)]
    [InlineData("rebase", MergeMethod.Rebase)]
    public void New_keys_round_trip(string method, MergeMethod expected)
    {
        var store = new ConfigStore(WriteConfig($$"""{ "hotkey": "Ctrl+Alt+G", "actions": { "mergeMethod": "{{method}}" } }"""));
        store.Load();
        Assert.Null(store.Error);
        Assert.Equal("Ctrl+Alt+G", store.Current.Hotkey);
        Assert.Equal(expected, store.Current.Actions.MergeMethod);
        var clone = ConfigStore.Clone(store.Current);
        Assert.Equal("Ctrl+Alt+G", clone.Hotkey);
        Assert.Equal(expected, clone.Actions.MergeMethod);
        var draft = new SettingsDraft(store.Current, null);
        Assert.Equal(expected, draft.Config.Actions.MergeMethod);
        Assert.False(draft.IsDirty);
        draft.Config.Hotkey = "Alt+F2";
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public void Unknown_merge_method_is_a_load_error()
    {
        var store = new ConfigStore(WriteConfig("""{ "actions": { "mergeMethod": "fast-forward" } }"""));
        store.Load();
        Assert.NotNull(store.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Win+Shift+G")]
    [InlineData("F9")]
    [InlineData("ctrl+alt+g")]
    public void Valid_hotkeys_pass(string hotkey) =>
        Assert.Empty(ConfigValidator.Errors(new AppConfig { Hotkey = hotkey }));

    [Theory]
    [InlineData("G")]
    [InlineData("Shift+G")]
    [InlineData("Meta+G")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+")]
    public void Invalid_hotkeys_are_collected_with_the_other_errors(string hotkey)
    {
        var errors = ConfigValidator.Errors(new AppConfig { Hotkey = hotkey, PollSeconds = 5 });
        Assert.Contains(errors, e => e.Field == "hotkey");
        Assert.Contains(errors, e => e.Field == "pollSeconds");
    }

    [Fact]
    public void Hotkey_and_actions_errors_land_on_the_general_page()
    {
        Assert.Equal(SettingsPage.General, SettingsDraft.PageFor("hotkey"));
        Assert.Equal(SettingsPage.General, SettingsDraft.PageFor("actions.mergeMethod"));
    }

    [Fact]
    public void Null_hotkey_and_actions_are_errors()
    {
        var errors = ConfigValidator.Errors(new AppConfig { Hotkey = null!, Actions = null! });
        Assert.Contains(errors, e => e.Field == "hotkey");
        Assert.Contains(errors, e => e.Field == "actions");
    }
}
