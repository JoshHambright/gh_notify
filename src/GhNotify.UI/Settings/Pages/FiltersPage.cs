using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using GhNotify.Core;

namespace GhNotify.UI;

public sealed class FiltersPage : SettingsPageBase
{
    readonly IReadOnlyList<InboxItem> _items;
    readonly WrapPanel _muteChips = new();
    readonly WrapPanel _hideChips = new();
    readonly StackPanel _timedMutes = new();
    readonly TextBlock _noTimedMutes;

    public FiltersPage(SettingsDraft draft, ISettingsHost host, Action changed) : base(draft, host, changed)
    {
        _items = host.CurrentItems();

        Body.Children.Add(Ui.Section("MUTED", 8));
        Body.Children.Add(Note("Muted items stay in the list, dimmed, and never toast, sound or count.", 10));
        Body.Children.Add(_muteChips);
        MuteHint = Ui.Text("", "muted", "small");
        MuteBox = PatternBox("gh:acme/legacy-* — Enter to add", () => Filters.Mute, MuteHint);
        Body.Children.Add(AddBox(MuteBox, MuteHint, 22));

        Body.Children.Add(Ui.Section("HIDDEN", 8));
        Body.Children.Add(Note("Hidden items are dropped from the popup entirely.", 10));
        Body.Children.Add(_hideChips);
        HideHint = Ui.Text("", "muted", "small");
        HideBox = PatternBox("ado:contoso/sandbox — Enter to add", () => Filters.Hide, HideHint);
        Body.Children.Add(AddBox(HideBox, HideHint, 12));
        Body.Children.Add(Note("Patterns: gh: or ado: limits to one provider · * matches within one path segment · contoso alone means the whole org.", 22));

        Body.Children.Add(Ui.Section("TIMED MUTES"));
        Body.Children.Add(_timedMutes);
        _noTimedMutes = Ui.Text("No timed mutes. Mute a row for a while from its ⋯ menu in the popup.", "muted", "small");
        Body.Children.Add(_noTimedMutes);

        ShowChips();
        ShowTimedMutes();
    }

    FiltersConfig Filters => Config.Filters;
    internal TextBox MuteBox { get; }
    internal TextBox HideBox { get; }
    internal TextBlock MuteHint { get; }
    internal TextBlock HideHint { get; }
    internal IReadOnlyList<string> TimedMuteLines => _timedMutes.Children.OfType<DockPanel>().Select(d => d.Tag as string ?? "").ToList();

    static TextBlock Note(string text, double bottom)
    {
        var t = Ui.Text(text, "muted", "small");
        t.Margin = new Thickness(0, 0, 0, bottom);
        return t;
    }

    static Control AddBox(TextBox box, TextBlock hint, double bottom)
    {
        hint.Margin = new Thickness(2, 4, 0, 0);
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, bottom) };
        p.Children.Add(box);
        p.Children.Add(hint);
        return p;
    }

    /// <summary>The add box: Enter adds the pattern; typing shows how many current items it matches.</summary>
    TextBox PatternBox(string watermark, Func<List<string>> list, TextBlock hint)
    {
        var box = Ui.Box("", watermark, 260, text => ShowHint(text, hint));
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || SettingsOptions.Blank(box.Text) is not { } pattern) return;
            e.Handled = true;
            Add(list(), pattern);
            box.Text = "";
        };
        return box;
    }

    internal void Add(List<string> list, string pattern)
    {
        if (!list.Contains(pattern, StringComparer.OrdinalIgnoreCase)) list.Add(pattern);
        ShowChips();
        Changed();
    }

    /// <summary>Live hint under the add box: how many current items the pattern matches, or that it is invalid.</summary>
    void ShowHint(string text, TextBlock hint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            hint.Text = "";
            return;
        }
        var n = SettingsDraft.CountMatches(text, _items);
        hint.Text = n < 0 ? "invalid pattern" : $"matches {n} current item{(n == 1 ? "" : "s")}";
        Ui.TokenForeground(hint, n < 0 ? "Brush.Red" : "Brush.Muted");
    }

    void ShowChips()
    {
        Fill(_muteChips, Filters.Mute);
        Fill(_hideChips, Filters.Hide);
    }

    void Fill(WrapPanel panel, List<string> list)
    {
        panel.Children.Clear();
        foreach (var pattern in list.ToList())
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            chip.Children.Add(new TextBlock { Text = pattern, VerticalAlignment = VerticalAlignment.Center });
            chip.Children.Add(Ui.Remove(() =>
            {
                list.Remove(pattern);
                ShowChips();
                Changed();
            }, tiny: false));
            var border = new Border { Child = chip };
            border.Classes.Add("chipborder");
            panel.Children.Add(border);
        }
    }

    // ----- Timed mutes (app state, not config: Unmute applies immediately) -----

    void ShowTimedMutes()
    {
        var timed = Host.ActiveMutes().Where(m => !m.Permanent).ToList();
        _timedMutes.Children.Clear();
        foreach (var mute in timed)
        {
            var unmute = Ui.Btn("Unmute", () =>
            {
                Host.Unmute(mute.Pattern);
                ShowTimedMutes();
            }, "pill");
            DockPanel.SetDock(unmute, Dock.Right);
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.Inlines!.Add(new Avalonia.Controls.Documents.Run(mute.Pattern));
            var rest = new Avalonia.Controls.Documents.Run(" · " + mute.Remaining);
            text.Inlines.Add(rest);
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Tag = $"{mute.Pattern} · {mute.Remaining}" };
            line.Children.Add(unmute);
            line.Children.Add(text);
            _timedMutes.Children.Add(line);
        }
        _noTimedMutes.IsVisible = timed.Count == 0;
    }
}
