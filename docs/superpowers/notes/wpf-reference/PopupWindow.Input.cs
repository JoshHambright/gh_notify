using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfKey = System.Windows.Input.Key;
using GhNotify.Core;

namespace GhNotify.App;

// Keyboard navigation, selection, search and the merge options (v1.3 spec §2 and §6). Key logic lives in Core KeyMap;
// this file only translates WPF input to KeyInput / PopupContext and carries out the resulting command.
public partial class PopupWindow
{
    // ----- search -----

    void OnSearchButton(object sender, RoutedEventArgs e)
    {
        if (SearchBar.Visibility == Visibility.Visible && string.IsNullOrEmpty(SearchBox.Text)) HideSearch();
        else ShowSearch();
    }

    void ShowSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    void HideSearch()
    {
        SearchBox.Clear();
        SearchBar.Visibility = Visibility.Collapsed;
        _query = "";
        Keyboard.ClearFocus();
        Focus();
        Render();
    }

    void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text;
        if (IsVisible) Render();
    }

    // ----- notice (a short line above the footer; the popup never uses a modal message box) -----

    void ShowNotice(string text)
    {
        NoticeText.Text = text;
        NoticeText.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    // ----- selection -----

    InboxRow? SelectedRow() => _selectedKey is null ? null : _rows.FirstOrDefault(r => r.Key == _selectedKey);

    /// <summary>Keeps the selection on the same item across refreshes; if it left, the row now at the same index (or the last row).</summary>
    void EnsureSelection(IReadOnlyList<InboxRow> rows)
    {
        if (rows.Count == 0)
        {
            _selectedKey = null;
            _selectedIndex = 0;
            return;
        }
        var at = _selectedKey is null ? -1 : rows.ToList().FindIndex(r => r.Key == _selectedKey);
        if (at >= 0)
        {
            _selectedIndex = at;
            return;
        }
        _selectedIndex = _selectedKey is null ? 0 : Math.Min(_selectedIndex, rows.Count - 1);
        _selectedKey = rows[_selectedIndex].Key;
    }

    void SelectKey(string key, bool scrollIntoView)
    {
        _selectedKey = key;
        var at = _rows.ToList().FindIndex(r => r.Key == key);
        if (at >= 0) _selectedIndex = at;
        ApplySelection(scrollIntoView);
    }

    void MoveSelection(int delta)
    {
        if (_rows.Count == 0) return;
        var at = _selectedKey is null ? -1 : _rows.ToList().FindIndex(r => r.Key == _selectedKey);
        var to = at < 0 ? (delta > 0 ? 0 : _rows.Count - 1) : Math.Clamp(at + delta, 0, _rows.Count - 1); // no wrap
        SelectKey(_rows[to].Key, scrollIntoView: true);
    }

    void ApplySelection(bool scrollIntoView)
    {
        foreach (var ring in Descendants(Sections).OfType<Border>().Where(b => b.Name == "SelectionRing"))
        {
            var selected = _selectedKey is not null && ring.Tag as string == _selectedKey;
            ring.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (selected && scrollIntoView) ring.BringIntoView();
        }
    }

    FrameworkElement? RingFor(string key) =>
        Descendants(Sections).OfType<Border>().FirstOrDefault(b => b.Name == "SelectionRing" && b.Tag as string == key);

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>A click on a row selects it; hovering never does. The click itself carries on to whatever was hit.</summary>
    void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var cur = e.OriginalSource as DependencyObject; cur is not null; cur = ParentOf(cur))
        {
            if (cur is Border { Name: "RowCard", Tag: string key })
            {
                SelectKey(key, scrollIntoView: false);
                return;
            }
        }
    }

    static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    // ----- keys -----

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ToKeyInput(e) is not { } input) return;
        var command = KeyMap.Resolve(input, Context());
        if (command.Kind != KeyCommandKind.None && Execute(command)) e.Handled = true;
    }

    static KeyInput? ToKeyInput(KeyEventArgs e)
    {
        var key = e.Key switch { WpfKey.System => e.SystemKey, WpfKey.ImeProcessed => e.ImeProcessedKey, var k => k };
        var name = key switch
        {
            WpfKey.Down => "Down",
            WpfKey.Up => "Up",
            WpfKey.Home => "Home",
            WpfKey.End => "End",
            WpfKey.Space => "Space",
            WpfKey.Enter => "Enter",
            WpfKey.Escape => "Esc",
            WpfKey.OemQuestion or WpfKey.Divide => "/",
            >= WpfKey.A and <= WpfKey.Z => key.ToString(),
            _ => null,
        };
        if (name is null) return null;
        var m = Keyboard.Modifiers;
        return new KeyInput(name, m.HasFlag(ModifierKeys.Control), m.HasFlag(ModifierKeys.Shift), m.HasFlag(ModifierKeys.Alt));
    }

    PopupContext Context()
    {
        var focused = Keyboard.FocusedElement;
        var row = SelectedRow();
        var batch = _host.Batch.Active;
        return new PopupContext(
            TextBoxFocused: focused is TextBox,
            SearchFocused: ReferenceEquals(focused, SearchBox),
            SearchEmpty: SearchBox.Text.Length == 0,
            SelectedRowMode: row?.Mode,
            SelectedRowActions: row?.Actions.Select(a => a.Action).ToList(),
            ConfirmOpen: batch,
            BatchOpen: batch,
            ButtonFocused: focused is System.Windows.Controls.Primitives.ButtonBase);
    }

    /// <summary>Carries out a resolved command; false when it did nothing (so the key keeps its normal meaning).</summary>
    bool Execute(KeyCommand command)
    {
        var row = SelectedRow();
        switch (command.Kind)
        {
            case KeyCommandKind.MoveDown:
                if (ReferenceEquals(Keyboard.FocusedElement, SearchBox))
                {
                    Keyboard.ClearFocus(); // Down from the search box lands on the first row
                    Focus();
                    if (_rows.Count > 0) SelectKey(_rows[0].Key, scrollIntoView: true);
                }
                else MoveSelection(+1);
                return true;
            case KeyCommandKind.MoveUp:
                MoveSelection(-1);
                return true;
            case KeyCommandKind.First:
                if (_rows.Count > 0) SelectKey(_rows[0].Key, scrollIntoView: true);
                return true;
            case KeyCommandKind.Last:
                if (_rows.Count > 0) SelectKey(_rows[^1].Key, scrollIntoView: true);
                return true;
            case KeyCommandKind.ToggleExpand:
                if (row is not null) _host.ToggleExpand(row.Key);
                return true;
            case KeyCommandKind.Open:
                if (row is not null) _host.OpenUrl(row.Url);
                return true;
            case KeyCommandKind.BeginAction:
                BeginFromKeyboard(row, command.Action);
                return true;
            case KeyCommandKind.OpenSnoozeMenu:
                if (row is not null && RingFor(row.Key) is { } ring && RowMenuFor(ring, row.Key) is { } menu) menu.IsOpen = true;
                return true;
            case KeyCommandKind.Confirm:
                ConfirmFromKeyboard(row);
                return true;
            case KeyCommandKind.CancelConfirm:
                if (row is { Mode: RowMode.Confirming }) _host.CancelAction(row.Key);
                else _host.CancelBatch();
                return true;
            case KeyCommandKind.FocusSearch:
                ShowSearch();
                return true;
            case KeyCommandKind.ClearSearch:
                SearchBox.Clear();
                return true;
            case KeyCommandKind.HidePopup:
                Hide();
                return true;
            default:
                return false;
        }
    }

    void BeginFromKeyboard(InboxRow? row, ItemAction? action)
    {
        if (row is null || action is not { } a) return;
        var button = row.Actions.FirstOrDefault(x => x.Action == a);
        if (button is null) return;
        if (!button.Enabled)
        {
            if (button.DisabledReason == AppController.BatchBusy) ShowNotice(AppController.BatchBusy);
            return;
        }
        _comments.Remove(row.Key);
        _methods.Remove(row.Key);
        _deleteBranch.Remove(row.Key);
        _ = _host.BeginActionAsync(row.Key, a);
    }

    void ConfirmFromKeyboard(InboxRow? row)
    {
        if (row is { Mode: RowMode.Confirming })
        {
            if (row.ConfirmEnabled) _ = _host.ConfirmActionAsync(row.Key, InputFor(row.Key)); // the 600 ms guard still applies
            return;
        }
        var batch = _host.Batch;
        if (batch.Phase == BatchPhase.Done) _host.CancelBatch();
        else if (batch.CanConfirm(DateTimeOffset.Now)) _ = _host.ConfirmBatchAsync(_batchComment?.Text);
    }

    // ----- confirm panel extras -----

    void OnOpenInBrowser(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string url) _host.OpenUrl(url);
    }

    /// <summary>The confirm statement names the method that will be used, so it follows the picker.</summary>
    static void ShowStatement(StackPanel panel, InboxRow row, MergeMethod method)
    {
        if (row.Statement is not { Length: > 0 } statement) return;
        if ((panel.Parent as Panel)?.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "StatementText") is { } text)
            text.Text = MergeStatement.WithMethod(statement, method);
    }

    static string MethodLabel(MergeMethod m) => m switch
    {
        MergeMethod.Squash => "Squash",
        MergeMethod.Rebase => "Rebase",
        _ => "Merge",
    };

    /// <summary>Fills the merge panel: one radio per allowed method, and Delete branch when the provider offers it.</summary>
    void OnMergePanelLoaded(object sender, RoutedEventArgs e)
    {
        var panel = (StackPanel)sender;
        panel.Children.Clear();
        if (panel.DataContext is not InboxRow { Options: { } options } row) return;
        var key = row.Key;
        var enabled = row.Mode == RowMode.Confirming;
        var selected = _methods.TryGetValue(key, out var chosen) && options.Allowed.Contains(chosen) ? chosen : options.Selected;

        var methods = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Text = "Method", FontSize = 11.5, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        methods.Children.Add(label);
        foreach (var method in options.Allowed)
        {
            var radio = new RadioButton
            {
                Content = MethodLabel(method),
                GroupName = "merge:" + key,
                IsChecked = method == selected,
                IsEnabled = enabled,
                Margin = new Thickness(0, 0, 12, 0),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            radio.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            radio.Checked += (_, _) =>
            {
                _methods[key] = method;
                ShowStatement(panel, row, method);
            };
            methods.Children.Add(radio);
        }
        panel.Children.Add(methods);
        ShowStatement(panel, row, selected);

        if (!options.CanDeleteBranch) return;
        var box = new CheckBox
        {
            Content = "Delete branch",
            IsChecked = _deleteBranch.TryGetValue(key, out var del) ? del : options.DeleteBranch,
            IsEnabled = enabled,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 11.5,
        };
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.Click += (_, _) => _deleteBranch[key] = box.IsChecked == true;
        panel.Children.Add(box);
    }
}
