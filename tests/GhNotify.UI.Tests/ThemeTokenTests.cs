using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

public class ThemeTokenTests
{
    static readonly string[] Brushes =
    [
        "Brush.Surface", "Brush.Raised", "Brush.Text", "Brush.Muted", "Brush.Section", "Brush.Hairline", "Brush.Red", "Brush.Amber",
        "Brush.Green", "Brush.ReadyText", "Brush.Blue", "Brush.RedTint", "Brush.EdgeStrip", "Brush.Header", "Brush.Count", "Brush.Eyebrow",
        "Brush.UrgentCard",
    ];

    static readonly string[] Themes =
    [
        "Style.SectionLabel", "Style.EBars", "Style.Card", "Style.PillPrimary", "Style.PillGhost", "Style.IconButton", "Style.LinkButton",
        "Style.MenuCard", "Style.NavItem", "Style.SegmentGroup", "Style.Segment", "Style.Chip", "Style.ChipBorder", "Style.PillBox",
        "Style.FieldBox", "Style.Check", "Style.ComboItem", "Style.PillCombo", "Style.Keyword", "Style.FieldLabel",
    ];

    static object? Resource(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var value) ? value : null;

    static Color Color(string key, ThemeVariant variant) => ((ISolidColorBrush)Resource(key, variant)!).Color;

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Every_brush_token_exists_in_each_variant(string variant)
    {
        var v = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        foreach (var key in Brushes) Assert.True(Resource(key, v) is IBrush, $"{key} ({variant})");
    }

    [AvaloniaFact]
    public void Dark_and_light_carry_the_WPF_colours()
    {
        Assert.Equal(Avalonia.Media.Color.Parse("#000C1A"), Color("Brush.Surface", ThemeVariant.Dark));
        Assert.Equal(Avalonia.Media.Color.Parse("#8296AB"), Color("Brush.Muted", ThemeVariant.Dark));
        Assert.Equal(Avalonia.Media.Color.Parse("#5FD474"), Color("Brush.ReadyText", ThemeVariant.Dark));
        Assert.Equal(Avalonia.Media.Color.Parse("#FFFFFF"), Color("Brush.Surface", ThemeVariant.Light));
        Assert.Equal(Avalonia.Media.Color.Parse("#5D7186"), Color("Brush.Muted", ThemeVariant.Light));
        Assert.Equal(Avalonia.Media.Color.Parse("#238A36"), Color("Brush.ReadyText", ThemeVariant.Light));
        Assert.Equal(Avalonia.Media.Color.Parse("#ED352F"), Color("Brush.Red", ThemeVariant.Dark));
        Assert.Equal(Avalonia.Media.Color.Parse("#ED352F"), Color("Brush.Red", ThemeVariant.Light));
        Assert.Equal(Avalonia.Media.Colors.Transparent, Color("Brush.EdgeStrip", ThemeVariant.Dark));
        Assert.Equal(Avalonia.Media.Color.Parse("#ED352F"), Color("Brush.EdgeStrip", ThemeVariant.Light));
        Assert.IsType<LinearGradientBrush>(Resource("Brush.UrgentCard", ThemeVariant.Dark));
    }

    [AvaloniaFact]
    public void Fonts_geometry_and_every_keyed_control_theme_exist()
    {
        Assert.True(Resource("Font.Body", ThemeVariant.Default) is FontFamily);
        Assert.True(Resource("Font.Heavy", ThemeVariant.Default) is FontFamily);
        Assert.NotNull(Resource("Geometry.EBars", ThemeVariant.Default));
        foreach (var key in Themes) Assert.True(Resource(key, ThemeVariant.Default) is ControlTheme, key);
    }

    [AvaloniaFact]
    public void The_bundled_Roboto_is_the_body_font()
    {
        using var h = new PopupHarness();
        var count = h.All<TextBlock>().First(t => t.Name == "CountText");
        Assert.Equal("Roboto", ((FontFamily)Resource("Font.Body", ThemeVariant.Default)!).FamilyNames.First());
        Assert.Equal("Roboto", count.FontFamily.FamilyNames.First());
        Assert.Equal(FontWeight.Black, count.FontWeight);
    }

    [AvaloniaFact]
    public void The_popup_follows_the_requested_theme_variant()
    {
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        try
        {
            using var h = new PopupHarness();
            app.RequestedThemeVariant = ThemeVariant.Dark;
            h.Pump();
            Assert.Equal(Avalonia.Media.Color.Parse("#0B1A29"), ((ISolidColorBrush)h.Card(SampleHost.ReadyKey).Background!).Color);

            app.RequestedThemeVariant = ThemeVariant.Light;
            h.Pump();
            Assert.Equal(Avalonia.Media.Color.Parse("#EBF0F5"), ((ISolidColorBrush)h.Card(SampleHost.ReadyKey).Background!).Color);
            var urgent = Assert.IsType<LinearGradientBrush>(h.Card(SampleHost.ApprovalKey).Background);
            Assert.Equal(Avalonia.Media.Color.Parse("#EBD8DB"), urgent.GradientStops[0].Color);
            Assert.Equal(Avalonia.Media.Color.Parse("#ED352F"), ((ISolidColorBrush)h.Card(SampleHost.ApprovalKey).BorderBrush!).Color);
            Assert.Equal(new Thickness(3, 0, 0, 0), h.Card(SampleHost.ApprovalKey).BorderThickness);
        }
        finally
        {
            app.RequestedThemeVariant = before;
        }
    }

    [AvaloniaFact]
    public void The_theme_controller_sets_the_application_variant()
    {
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        try
        {
            var system = new FakeSystemTheme { IsDark = false };
            using var controller = new ThemeController(() => ThemeMode.System, system, new InlineDispatcher());
            controller.Apply();
            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
            system.IsDark = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
            using var forced = new ThemeController(() => ThemeMode.Light, system, new InlineDispatcher());
            forced.Apply();
            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
        }
        finally
        {
            app.RequestedThemeVariant = before;
        }
    }

    [AvaloniaFact]
    public void The_pill_buttons_use_the_red_and_ghost_looks()
    {
        using var h = new PopupHarness();
        var approve = h.Button("Approve…", h.Card(SampleHost.ApprovalKey));
        var reject = h.Button("Reject…", h.Card(SampleHost.ApprovalKey));
        Assert.Equal(Avalonia.Media.Color.Parse("#ED352F"), ((ISolidColorBrush)approve.Background!).Color);
        Assert.Equal(FontWeight.Bold, approve.FontWeight);
        Assert.Equal(Avalonia.Media.Colors.Transparent, ((ISolidColorBrush)reject.Background!).Color);
        Assert.Equal(FontWeight.Medium, reject.FontWeight);
    }
}
