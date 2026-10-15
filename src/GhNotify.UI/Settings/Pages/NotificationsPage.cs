using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using GhNotify.Core;

namespace GhNotify.UI;

public sealed class NotificationsPage : SettingsPageBase
{
    readonly StackPanel _rules = new();
    readonly StackPanel _summaries = new() { Margin = new Thickness(0, 0, 0, 10) };

    public NotificationsPage(SettingsDraft draft, ISettingsHost host, Action changed) : base(draft, host, changed)
    {
        Body.Children.Add(Ui.Section("SOUND RULES · FIRST MATCH WINS"));
        Body.Children.Add(_rules);
        var add = Ui.Btn("+ Add sound rule", () =>
        {
            Sounds.Add(new SoundRule());
            ShowRules();
            Changed();
        }, "link");
        add.Margin = new Thickness(0, 2, 0, 16);
        Body.Children.Add(add);
        Body.Children.Add(_summaries);
        Body.Children.Add(Ui.Text("First matching rule wins. Outside work hours only rules with 'alert off-hours' notify.", "muted", "small"));
        ShowRules();
    }

    List<SoundRule> Sounds => Config.Notifications.Sounds;

    /// <summary>The rule rows, in order (internal for tests).</summary>
    internal IReadOnlyList<SoundRow> Rows { get; private set; } = [];

    internal IReadOnlyList<ComboBox> SoundCombos => _rules.Children.OfType<Border>()
        .SelectMany(b => b.GetLogicalDescendants().OfType<ComboBox>()).Where(c => c.Tag is "sound").ToList();

    void ShowRules()
    {
        var rows = Rows = Sounds.Select(r => new SoundRow(r, Changed)).ToList();
        _rules.Children.Clear();
        _summaries.Children.Clear();
        foreach (var row in rows)
        {
            _rules.Children.Add(Card(row));
            var summary = Ui.Text(row.Summary, "muted", "small");
            summary.Margin = new Thickness(0, 0, 0, 3);
            row.PropertyChanged += (_, _) => summary.Text = row.Summary;
            _summaries.Children.Add(summary);
        }
    }

    Control Card(SoundRow row)
    {
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        foreach (var (glyph, tip, delta) in new[] { ("↑", "Move up", -1), ("↓", "Move down", 1) })
        {
            var b = Ui.Btn(glyph, () => Move(row, delta), "icon");
            ToolTip.SetTip(b, tip);
            tools.Children.Add(b);
        }
        tools.Children.Add(Ui.Remove(() =>
        {
            Sounds.Remove(row.Rule);
            ShowRules();
            Changed();
        }));
        DockPanel.SetDock(tools, Dock.Right);

        var wrap = new WrapPanel();
        var when = Ui.Text("WHEN", "keyword");
        when.Margin = new Thickness(0, 4, 8, 4);
        wrap.Children.Add(when);
        var kind = Ui.Combo(row.KindLabels, row.KindIndex, i => row.KindIndex = i, 150);
        kind.Margin = new Thickness(0, 2, 10, 2);
        wrap.Children.Add(kind);
        wrap.Children.Add(Labeled("IN", Ui.Box(row.Repo, "any repo", 120, v => row.Repo = v)));
        wrap.Children.Add(Labeled("BY", Ui.Box(row.Author, "anyone", 90, v => row.Author = v)));

        var sound = Ui.Combo(row.SoundOptions, row.SoundIndex, i => row.SoundIndex = i, 130);
        sound.Tag = "sound";
        sound.Margin = new Thickness(0, 2, 4, 2);
        ToolTip.SetTip(sound, row.SoundToolTip);
        sound.SelectionChanged += (_, _) => OnSoundSelected(sound, row);
        sound.DropDownClosed += (_, _) => OnSoundDropDownClosed(sound, row);
        var play = Ui.Btn("▶", () =>
        {
            if (row.Rule.Sound != "none") Host.PlaySound(row.Rule.Sound);
        }, "icon");
        play.FontSize = 10;
        play.IsEnabled = row.CanPlay;
        play.Margin = new Thickness(0, 2, 10, 2);
        ToolTip.SetTip(play, "Play");
        row.PropertyChanged += (_, _) => play.IsEnabled = row.CanPlay;
        var arrow = Ui.Text("→", "keyword");
        arrow.FontSize = 13;
        var soundGroup = new StackPanel { Orientation = Orientation.Horizontal };
        soundGroup.Children.Add(arrow);
        soundGroup.Children.Add(sound);
        soundGroup.Children.Add(play);
        wrap.Children.Add(soundGroup);

        var alert = Ui.Check("alert off-hours", row.AlertOffHours, v => row.AlertOffHours = v);
        alert.Margin = new Thickness(0, 2);
        wrap.Children.Add(alert);

        var panel = new DockPanel();
        panel.Children.Add(tools);
        panel.Children.Add(wrap);
        return Ui.Card(panel);
    }

    static Control Labeled(string keyword, Control box)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(Ui.Text(keyword, "keyword"));
        box.Margin = new Thickness(0, 2, 10, 2);
        p.Children.Add(box);
        return p;
    }

    /// <summary>"Custom file…" picked with the keyboard on a closed box: put the selection back (arrowing must never pop a picker).</summary>
    void OnSoundSelected(ComboBox combo, SoundRow row)
    {
        if (combo.SelectedIndex == row.CustomIndex && !combo.IsDropDownOpen) Revert(combo, row);
    }

    /// <summary>"Custom file…" committed from the open list: pick a .wav; cancelling puts the old sound back.</summary>
    async void OnSoundDropDownClosed(ComboBox combo, SoundRow row)
    {
        if (combo.SelectedIndex != row.CustomIndex) return;
        var picked = await Ui.PickFile(this, "Choose a sound", "Wave sounds", "*.wav");
        if (picked is null)
        {
            Revert(combo, row);
            return;
        }
        row.SetCustomSound(picked);
        Ui.Post(ShowRules); // the list now names the picked file
    }

    static void Revert(ComboBox combo, SoundRow row) => Ui.Post(() => combo.SelectedIndex = row.SoundIndex);

    void Move(SoundRow row, int delta)
    {
        if (!SettingsDraft.Move(Sounds, Sounds.IndexOf(row.Rule), delta)) return;
        ShowRules();
        Changed();
    }
}
