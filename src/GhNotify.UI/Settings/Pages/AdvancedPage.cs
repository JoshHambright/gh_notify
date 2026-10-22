using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GhNotify.Core;

namespace GhNotify.UI;

public sealed class AdvancedPage : SettingsPageBase
{
    const string ResetLabel = "Reset to defaults…";
    const string ConfirmLabel = "Confirm reset — Save applies it";
    readonly DispatcherTimer _confirmWindow;

    public AdvancedPage(SettingsDraft draft, ISettingsHost host, Action changed) : base(draft, host, changed)
    {
        Body.Children.Add(Ui.Section("FILES"));
        var files = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (label, path) in new[] { ("Open config.json", host.ConfigPath), ("Open logs folder", host.LogsPath), ("Open data folder", host.DataPath) })
        {
            var b = Ui.Btn(label, () => host.OpenPath(path), "pill");
            b.Margin = new Thickness(0, 0, 8, 8);
            files.Children.Add(b);
        }
        Body.Children.Add(files);
        ConfigPathBox = new TextBox { Text = host.ConfigPath, IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(0, 4) };
        ConfigPathBox.Classes.Add("pill");
        Body.Children.Add(Ui.Field("Config file", ConfigPathBox, 22));

        if (UiPlatform.IsMac)
        {
            Body.Children.Add(Ui.Section("TERMINAL"));
            TerminalBox = Ui.Box(Config.Display.Terminal, DisplayConfig.DefaultTerminal, 260, v => { Config.Display.Terminal = v.Trim(); Changed(); });
            Body.Children.Add(Ui.Field("Open terminals in", TerminalBox, 4));
            var hint = Ui.Text("terminal (Terminal.app), iterm (iTerm2), or a command that gets the folder and the command line as its two arguments.", "muted", "small");
            hint.Margin = new Thickness(0, 0, 0, 12);
            Body.Children.Add(hint);
        }

        Body.Children.Add(Ui.Section("RESET"));
        var note = Ui.Text("Puts every setting back to its default. Nothing changes on disk until you Save; Cancel undoes it.", "muted", "small");
        note.Margin = new Thickness(0, 0, 0, 10);
        Body.Children.Add(note);
        ResetButton = Ui.Btn(ResetLabel, OnReset, "pill");
        ResetButton.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(ResetButton);

        _confirmWindow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _confirmWindow.Tick += (_, _) => Disarm();
        DetachedFromVisualTree += (_, _) => Disarm();
    }

    internal TextBox ConfigPathBox { get; }
    internal TextBox? TerminalBox { get; }
    internal Button ResetButton { get; }
    internal bool Armed => _confirmWindow.IsEnabled;

    /// <summary>The second click of "Reset to defaults…"; the window resets the draft and rebuilds the pages.</summary>
    public event Action? ResetConfirmed;

    /// <summary>Two-step: the first click arms the button for 5 s, the second resets.</summary>
    internal void OnReset()
    {
        if (!_confirmWindow.IsEnabled)
        {
            ResetButton.Content = ConfirmLabel;
            ResetButton.Classes.Add("primary");
            _confirmWindow.Start();
            return;
        }
        Disarm();
        ResetConfirmed?.Invoke();
    }

    void Disarm()
    {
        _confirmWindow.Stop();
        ResetButton.Content = ResetLabel;
        ResetButton.Classes.Remove("primary");
    }
}
