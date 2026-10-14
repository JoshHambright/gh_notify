using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GhNotify.Core;

namespace GhNotify.App;

public partial class AdvancedPage : UserControl, ISettingsPage
{
    const string ResetLabel = "Reset to defaults…";
    const string ConfirmLabel = "Confirm reset — Save applies it";
    readonly ISettingsHost _host;
    readonly DispatcherTimer _confirmWindow;

    public AdvancedPage(SettingsDraft draft, ISettingsHost host, Action changed)
    {
        InitializeComponent();
        _host = host;
        ConfigPathBox.Text = host.ConfigPath;
        _confirmWindow = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        _confirmWindow.Tick += (_, _) => Disarm();
        Unloaded += (_, _) => Disarm();
    }

    /// <summary>The second click of "Reset to defaults…"; the window resets the draft and rebuilds the pages.</summary>
    public event Action? ResetConfirmed;

    void OnOpenConfig(object sender, RoutedEventArgs e) => _host.OpenPath(_host.ConfigPath);
    void OnOpenLogs(object sender, RoutedEventArgs e) => _host.OpenPath(_host.LogsPath);
    void OnOpenData(object sender, RoutedEventArgs e) => _host.OpenPath(_host.DataPath);

    /// <summary>Two-step: the first click arms the button for 5 s, the second resets.</summary>
    void OnReset(object sender, RoutedEventArgs e)
    {
        if (!_confirmWindow.IsEnabled)
        {
            ResetButton.Content = ConfirmLabel;
            ResetButton.Style = (Style)FindResource("Style.PillPrimary");
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
        ResetButton.Style = (Style)FindResource("Style.PillGhost");
    }

    public void ShowErrors(IReadOnlyList<SettingsError> errors) => ErrorList.ItemsSource = errors.Select(e => e.Message).ToList();
}
