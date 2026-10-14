using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>A popup over a <see cref="SampleHost"/> in the headless platform, with helpers to find controls, click them and press keys.</summary>
sealed class PopupHarness : IDisposable
{
    public SampleHost Host { get; }
    public PopupWindow Window { get; }

    public PopupHarness(Action<SampleHost>? setup = null, bool show = true)
    {
        Host = new SampleHost();
        setup?.Invoke(Host);
        Window = new PopupWindow(Host);
        if (show) Show();
    }

    public void Show()
    {
        Window.ShowNearTray();
        Pump();
    }

    /// <summary>Runs everything that is queued: posted renders, loaded handlers, layout.</summary>
    public void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose() => Window.Close();

    // ----- finding -----

    public IEnumerable<T> All<T>(Visual? root = null) where T : Visual => (root ?? Window).GetVisualDescendants().OfType<T>();

    public Border Card(string key) => All<Border>().First(b => b.Name == "RowCard" && b.Tag as string == key);

    public bool HasCard(string key) => All<Border>().Any(b => b.Name == "RowCard" && b.Tag as string == key);

    public Border Ring(string key) => All<Border>().First(b => b.Name == "SelectionRing" && b.Tag as string == key);

    public IReadOnlyList<string> CardKeys() => All<Border>().Where(b => b.Name == "RowCard").Select(b => (string)b.Tag!).ToList();

    /// <summary>The visible button (inside <paramref name="scope"/>, default the whole window) whose content is exactly <paramref name="text"/>.</summary>
    public Button Button(string text, Visual? scope = null) =>
        TryButton(text, scope) ?? throw new InvalidOperationException($"no visible button '{text}'");

    public Button? TryButton(string text, Visual? scope = null) =>
        All<Button>(scope).FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == text);

    public Button Chevron(string key) =>
        All<Button>(Card(key)).First(b => b.Tag as string == key && ToolTip.GetTip(b) as string == "Details");

    public Button RowMenuButton(string key) =>
        All<Button>(Card(key)).First(b => b.Tag as string == key && ToolTip.GetTip(b) as string == "Snooze or mute");

    public Button ConfirmButton(string key) => All<Button>(Card(key)).First(b => b.Name == "ConfirmButton");

    public Button BatchConfirm => All<Button>().First(b => b.Name == "BatchConfirm");

    public TextBox CommentBox(string key) => All<TextBox>(Card(key)).First(b => b.Name == "CommentBox");

    public TextBox? BatchComment => All<TextBox>().FirstOrDefault(b => b.Parent is StackPanel p && p.Parent is Border { Name: "BatchPanel" });

    public TextBox SearchBox => All<TextBox>().First(b => b.Name == "SearchBox");

    /// <summary>All text shown in a row card (visible text blocks only).</summary>
    public IReadOnlyList<string> Texts(Visual scope) =>
        All<TextBlock>(scope).Where(t => t.IsEffectivelyVisible).Select(t => t.Inlines is { Count: > 0 } i ? string.Concat(i.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text ?? "").ToList();

    public bool Shows(Visual scope, string text) => Texts(scope).Any(t => t.Contains(text, StringComparison.Ordinal));

    // ----- pointer -----

    public Point Center(Control c)
    {
        c.BringIntoView(); // the popup scrolls; a control below the fold would be clicked through
        Window.UpdateLayout();
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), Window) ?? throw new InvalidOperationException("not in the tree");
        return p;
    }

    public void Click(Control c)
    {
        var p = Center(c);
        Window.MouseDown(p, MouseButton.Left);
        Window.MouseUp(p, MouseButton.Left);
        Pump();
    }

    /// <summary>The pointer enters the button and moves on it, as a real mouse does before a deliberate click.</summary>
    public void Hover(Control c)
    {
        var p = Center(c);
        Window.MouseMove(new Point(p.X - 6, p.Y));
        Window.MouseMove(new Point(p.X - 3, p.Y));
        Window.MouseMove(p);
        Window.MouseMove(new Point(p.X + 3, p.Y));
    }

    // ----- keyboard -----

    public void Press(Key key, KeyModifiers modifiers = KeyModifiers.None, string? symbol = null)
    {
        Window.KeyPress(key, (RawInputModifiers)modifiers, PhysicalKey.None, symbol);
        Pump();
    }

    public void Type(string text)
    {
        Window.KeyTextInput(text);
        Pump();
    }

    /// <summary>Real time passing (the 600 ms confirm guard reads the clock).</summary>
    public async Task WaitGuardAsync()
    {
        await Task.Delay(ActionText.ConfirmDelay + TimeSpan.FromMilliseconds(80));
        Window.Render();
        Pump();
    }

    public static string? FocusedName(PopupHarness h) => (TopLevel.GetTopLevel(h.Window)?.FocusManager?.GetFocusedElement() as Control)?.Name;
}
