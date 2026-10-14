using System.Runtime.InteropServices;
using Velopack;

namespace GhNotify.App;

public static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => InstallHooks.AfterInstall())
            .OnAfterUpdateFastCallback(_ => InstallHooks.AfterUpdate())
            .OnBeforeUninstallFastCallback(_ => InstallHooks.BeforeUninstall())
            .Run();
        if (args is ["--version"])
        {
            AttachConsole(-1); // WinExe: print to the console that launched us, if any
            Console.WriteLine(AppVersion.Current);
            return 0;
        }
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
