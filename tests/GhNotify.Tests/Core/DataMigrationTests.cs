using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class DataMigrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ghn-mig-" + Guid.NewGuid().ToString("N"));
    string Old => Path.Combine(_root, "GhNotify");
    string New => Path.Combine(_root, "GhNotify-data");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void Moves_state_and_logs_once()
    {
        Directory.CreateDirectory(Path.Combine(Old, "logs"));
        File.WriteAllText(Path.Combine(Old, "state.json"), "{}");
        File.WriteAllText(Path.Combine(Old, "logs", "a.log"), "x");
        Directory.CreateDirectory(Path.Combine(Old, "worktrees", "w1"));

        DataMigration.Run(Old, New);

        Assert.True(File.Exists(Path.Combine(New, "state.json")));
        Assert.True(File.Exists(Path.Combine(New, "logs", "a.log")));
        Assert.False(File.Exists(Path.Combine(Old, "state.json")));
        Assert.True(Directory.Exists(Path.Combine(Old, "worktrees", "w1"))); // left in place; sweep uses stored absolute paths
    }

    [Fact]
    public void Does_nothing_when_new_state_exists()
    {
        Directory.CreateDirectory(Old);
        Directory.CreateDirectory(New);
        File.WriteAllText(Path.Combine(Old, "state.json"), "old");
        File.WriteAllText(Path.Combine(New, "state.json"), "new");
        DataMigration.Run(Old, New);
        Assert.Equal("new", File.ReadAllText(Path.Combine(New, "state.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Old, "state.json")));
    }

    [Fact]
    public void Missing_legacy_dir_is_fine() => DataMigration.Run(Old, New);

    [Fact]
    public void Merges_legacy_logs_into_existing_target_logs_dir()
    {
        Directory.CreateDirectory(Path.Combine(Old, "logs"));
        Directory.CreateDirectory(Path.Combine(New, "logs"));
        File.WriteAllText(Path.Combine(Old, "state.json"), "{}");
        File.WriteAllText(Path.Combine(Old, "logs", "old.log"), "old");
        File.WriteAllText(Path.Combine(Old, "logs", "same.log"), "legacy");
        File.WriteAllText(Path.Combine(New, "logs", "same.log"), "current");

        DataMigration.Run(Old, New);

        Assert.Equal("old", File.ReadAllText(Path.Combine(New, "logs", "old.log")));
        Assert.Equal("current", File.ReadAllText(Path.Combine(New, "logs", "same.log"))); // never overwritten
        Assert.True(File.Exists(Path.Combine(New, "state.json")));
    }
}
