using Avalonia.Controls;

namespace GhNotify.UI;

/// <summary>The settings window. The settings task replaces the body; the constructor takes the live host (AppController, or SampleSettingsHost in tests).</summary>
public partial class SettingsWindow : Window
{
    readonly ISettingsHost _host;

    public SettingsWindow() : this(new SampleSettingsHost()) { }

    public SettingsWindow(ISettingsHost host)
    {
        _host = host;
        InitializeComponent();
    }

    public ISettingsHost Host => _host;
}
