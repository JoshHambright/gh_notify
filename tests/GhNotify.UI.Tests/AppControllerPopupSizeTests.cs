using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Popup width and text size on the real AppController: kept in state.json, and a wider popup gets longer repo names.</summary>
public sealed class AppControllerPopupSizeTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("ghn-size").FullName;
    const string LongRepo = "fabrikam/customer-platform/checkout-service-api";

    string StatePath => Path.Combine(_root, "state.json");

    AppController Create()
    {
        var configPath = Path.Combine(_root, "config.json");
        if (!File.Exists(configPath)) File.WriteAllText(configPath, """{"github":{"enabled":false},"ado":{"enabled":false}}""");
        var config = new ConfigStore(configPath);
        config.Load();
        var dirs = new AppDirs(_root, Path.Combine(_root, "logs"), Path.Combine(_root, "wt"), Path.Combine(_root, "scratch"));
        return new AppController(config, new StateStore(StatePath), new FakePlatform().Services, new AppRuntime(new InlineDispatcher(), dirs, () => { }, _ => { }));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void Width_and_text_size_persist_and_out_of_range_values_are_corrected()
    {
        new StateStore(StatePath).Save(new AppState { PopupWidth = 9999, TextScale = 1.2 });
        using (var c = Create())
        {
            Assert.Equal(PopupSize.MaxWidth, c.PopupWidth);
            Assert.Equal(1.15, c.TextScale);
            c.SetPopupWidth(500);
            c.SetTextScale(1.3);
        }
        var state = new StateStore(StatePath).Load();
        Assert.Equal(500, state.PopupWidth);
        Assert.Equal(1.3, state.TextScale);

        using (var c = Create())
        {
            Assert.Equal(500, c.PopupWidth);
            c.SetPopupWidth(PopupSize.DefaultWidth);
        }
        Assert.Null(new StateStore(StatePath).Load().PopupWidth); // the default is not stored
    }

    [Fact]
    public void A_wider_popup_gets_longer_repo_names()
    {
        var item = new InboxItem("gh:review:" + LongRepo + "#1", Provider.GitHub, ItemKind.ReviewRequested, LongRepo, "T", "https://u", "alice",
            DateTimeOffset.Now.AddHours(-1), "abc1234", 10, null);
        new StateStore(StatePath).Save(new AppState { Snapshot = new() { ["GitHub"] = [item] } });
        using var c = Create();
        string Repo() => c.BuildSections(null).SelectMany(s => s.Rows).Single().RepoDisplay;
        Assert.Contains("…", Repo());
        var changed = 0;
        c.Changed += () => changed++;
        c.SetPopupWidth(600);
        Assert.Equal(LongRepo, Repo());
        Assert.Equal(1, changed);
    }
}
