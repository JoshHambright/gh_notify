using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class MuteConfigEditorTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ghn-mute-" + Guid.NewGuid().ToString("N"));
    string PathJ => Path.Combine(_dir, "config.json");

    public MuteConfigEditorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    ConfigStore Load(string json)
    {
        File.WriteAllText(PathJ, json);
        var store = new ConfigStore(PathJ);
        store.Load();
        return store;
    }

    static void Add(List<string> l) => l.Add("acme/*");

    [Fact]
    public void The_hide_list_is_edited_without_touching_mutes()
    {
        var store = Load("{ \"filters\": { \"mute\": [\"gh:fabrikam\"] } }");
        Assert.Equal(MuteSaveResult.Saved, MuteConfigEditor.TrySave(store, Add, commentsWarningShown: false, FilterList.Hide));
        Assert.Equal(["acme/*"], store.Current.Filters.Hide);
        Assert.Equal(["gh:fabrikam"], store.Current.Filters.Mute);
        var reread = new ConfigStore(PathJ);
        reread.Load();
        Assert.Equal(["acme/*"], reread.Current.Filters.Hide);
    }

    [Fact]
    public void Normal_edit_is_saved_with_backup()
    {
        var store = Load("{ \"pollSeconds\": 60 }");
        Assert.Equal(MuteSaveResult.Saved, MuteConfigEditor.TrySave(store, Add, commentsWarningShown: false));
        Assert.Contains("acme/*", store.Current.Filters.Mute);
        Assert.Contains("acme/*", File.ReadAllText(PathJ));
        Assert.Equal("{ \"pollSeconds\": 60 }", File.ReadAllText(PathJ + ".bak"));
    }

    [Fact]
    public void Config_error_refuses_and_leaves_file_alone()
    {
        var store = Load("{ not json");
        Assert.NotNull(store.Error);
        Assert.Equal(MuteSaveResult.ConfigError, MuteConfigEditor.TrySave(store, Add, false));
        Assert.Equal("{ not json", File.ReadAllText(PathJ));
    }

    [Fact]
    public void File_changed_on_disk_refuses()
    {
        var store = Load("{ \"pollSeconds\": 60 }");
        File.WriteAllText(PathJ, "{ \"pollSeconds\": 61 }");
        Assert.Equal(MuteSaveResult.Failed, MuteConfigEditor.TrySave(store, Add, false));
        Assert.Contains("61", File.ReadAllText(PathJ));
        Assert.DoesNotContain("acme", File.ReadAllText(PathJ));
    }

    [Fact]
    public void Comments_not_yet_warned_refuses()
    {
        var store = Load("{ // keep me\n \"pollSeconds\": 60 }");
        Assert.Equal(MuteSaveResult.CommentsNotAcknowledged, MuteConfigEditor.TrySave(store, Add, commentsWarningShown: false));
        Assert.Contains("// keep me", File.ReadAllText(PathJ));
    }

    [Fact]
    public void Comments_already_warned_saves()
    {
        var store = Load("{ // keep me\n \"pollSeconds\": 60 }");
        Assert.Equal(MuteSaveResult.Saved, MuteConfigEditor.TrySave(store, Add, commentsWarningShown: true));
        Assert.Contains("acme/*", File.ReadAllText(PathJ));
    }
}
