using System.Runtime.CompilerServices;
using System.Windows;

namespace GhNotify.App.Tests;

static class TestApp
{
    // pack://application:,,,/GhNotify;component/... only resolves when an Application exists.
    [ModuleInitializer]
    internal static void Init()
    {
        var t = new Thread(() => { if (Application.Current is null) new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
    }
}
