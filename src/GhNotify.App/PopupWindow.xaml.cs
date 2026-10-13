using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GhNotify.App;

public partial class PopupWindow : Window
{
    readonly AppController _c;

    public PopupWindow(AppController controller)
    {
        InitializeComponent();
        _c = controller;
    }

    public void Render()
    {
        var sections = _c.BuildSections();
        Sections.ItemsSource = sections;
        EmptyText.Visibility = sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LastRefreshed.Text = _c.LastPolledAt is { } t ? $"Updated {t.ToLocalTime():HH:mm:ss}" : "Not refreshed yet";
        Problems.ItemsSource = _c.Problems();
    }

    public void ShowNearTray()
    {
        Render();
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 8;
        Top = area.Bottom - Height - 8;
        Show();
        Activate();
    }

    void OnDeactivated(object? sender, EventArgs e) => Hide();

    void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string url) _c.OpenUrl(url);
    }

    void OnRunAgent(object sender, RoutedEventArgs e) => _c.RunAgent((string)((FrameworkElement)sender).Tag);

    void OnOpenInClaude(object sender, RoutedEventArgs e) => _c.OpenInClaude((string)((FrameworkElement)sender).Tag);

    void OnRefresh(object sender, RoutedEventArgs e) => _ = _c.RefreshAsync();

    void OnMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        var openConfig = new MenuItem { Header = "Open config" };
        openConfig.Click += (_, _) => _c.OpenUrl(_c.ConfigPath);
        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = StartupRegistration.IsEnabled() };
        startup.Click += (_, _) => StartupRegistration.Set(startup.IsChecked);
        var logs = new MenuItem { Header = "Open logs folder" };
        logs.Click += (_, _) => _c.OpenUrl(AppPaths.Logs);
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(openConfig);
        menu.Items.Add(startup);
        menu.Items.Add(logs);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        menu.IsOpen = true;
    }
}
