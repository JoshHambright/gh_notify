using GhNotify.App;

namespace GhNotify.App.Tests;

public class InstallInfoTests
{
    [Fact]
    public void Stable_path_is_the_current_folder_exe_when_update_exe_is_beside_it()
    {
        var root = Directory.CreateTempSubdirectory("ghn-inst").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "current"));
            File.WriteAllText(Path.Combine(root, "Update.exe"), "");
            var exe = Path.Combine(root, "current", "GhNotify.exe");
            Assert.Equal(exe, InstallInfo.StablePathFor(exe));
            Assert.Null(InstallInfo.StablePathFor(Path.Combine(root, "bin", "GhNotify.exe")));
        }
        finally { Directory.Delete(root, true); }
    }
}
