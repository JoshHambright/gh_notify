using GhNotify.UI;
using Microsoft.Win32;

namespace GhNotify.Platform.Windows;

/// <summary>Start with Windows: the HKCU Run key, pointing at the stable Velopack exe path.</summary>
public sealed class RunKeyStartupService : IStartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "GhNotify";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, $"\"{InstallInfo.StableExePath ?? Environment.ProcessPath}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
