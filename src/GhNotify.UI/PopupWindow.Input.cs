using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GhNotify.Core;

namespace GhNotify.UI;

// Keyboard navigation, selection, search and the merge options (v1.3 spec §2 and §6). Key logic lives in Core KeyMap;
// this file only translates Avalonia input to KeyInput / PopupContext and carries out the resulting command.
public partial class PopupWindow
{
    // The last input was a pointer press (a confirm click then needs the pointer guard); a key press clears it.
    bool _pointerInput;
    // "/" opens the search box; the character that follows the key press must not land in it.
    bool _swallowSlash;

    // ----- search -----

    void OnSearchButton(object? sender, RoutedEventArgs e)
    {
        if (SearchBar.IsVisible && string.IsNullOrEmpty(SearchBox.Text)) HideSearch();
        else ShowSearch();
    }

    void ShowSearch()
    {
        SearchBar.IsVisible = true;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    void HideSearch()
    {
        SearchBox.Clear();
        SearchBar.IsVisible = false;
        _query = "";
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        Render();
    }

    void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text ?? "";
        if (IsVisible) Render();
    }

    // ----- notice (a short line above the footer; the popup never uses a modal dialog) -----

    void ShowNotice(string text)
    {
        NoticeText.Text = text;
        NoticeText.IsVisible = true;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    // ----- selection -----

    InboxRow? SelectedRow() => _selectedKey is null ? null : _rows.FirstOrDefault(r => r.Key == _selectedKey);

    /// <summary>The key of the keyboard-selected row, or null.</summary>
    internal string? SelectedKey => _selectedKey;

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
        foreach (var ring in SelectionRings())
        {
            var selected = _selectedKey is not null && ring.Tag as string == _selectedKey;
            ring.IsVisible = selected;
            if (selected && scrollIntoView) ring.BringIntoView();
        }
    }

    /// <summary>A new row visual reads the selection when it first loads, so a rebuild never loses the ring.</summary>
    void OnRingLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Border ring) ring.IsVisible = _selectedKey is not null && ring.Tag as string == _selectedKey;
    }

    IEnumerable<Border> SelectionRings() =>
        Sections.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "SelectionRing");

    Border? RingFor(string key) => SelectionRings().FirstOrDefault(b => b.Tag as string == key);

    /// <summary>A click on a row selects it; hovering never does. The click itself carries on to whatever was hit.</summary>
    void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerInput = true;
        for (var cur = e.Source as Visual; cur is not null; cur = cur.GetVisualParent())
        {
            if (cur is Border { Name: "RowCard", Tag: string key })
            {
                SelectKey(key, scrollIntoView: false);
                return;
            }
        }
    }

    // ----- keys -----

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        _pointerInput = false;
        if (ToKeyInput(e) is not { } input) return;
        var command = KeyMap.Resolve(input, Context());
        if (command.Kind != KeyCommandKind.None && Execute(command)) e.Handled = true;
    }

    void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (!_swallowSlash) return;
        _swallowSlash = false;
        if (e.Text == "/") e.Handled = true;
    }

    static KeyInput? ToKeyInput(KeyEventArgs e)
    {
        var name = e.Key switch
        {
            Key.Down => "Down",
            Key.Up => "Up",
            Key.Home => "Home",
            Key.End => "End",
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Escape => "Esc",
            Key.OemQuestion or Key.Divide => "/",
            >= Key.A and <= Key.Z => e.Key.ToString(),
            _ => e.KeySymbol == "/" ? "/" : null,
        };
        if (name is null) return null;
        var m = e.KeyModifiers;
        return new KeyInput(name, m.HasFlag(KeyModifiers.Control), m.HasFlag(KeyModifiers.Shift), m.HasFlag(KeyModifiers.Alt));
    }

    PopupContext Context()
    {
        var focused = FocusedElement();
        var row = SelectedRow();
        var batch = _host.Batch.Active;
        return new PopupContext(
            TextBoxFocused: focused is TextBox,
            SearchFocused: ReferenceEquals(focused, SearchBox),
            SearchEmpty: string.IsNullOrEmpty(SearchBox.Text),
            SelectedRowMode: row?.Mode,
            SelectedRowActions: row?.Actions.Select(a => a.Action).ToList(),
            ConfirmOpen: batch,
            BatchOpen: batch,
            ButtonFocused: focused is Button);
    }

    /// <summary>Carries out a resolved command; false when it did nothing (so the key keeps its normal meaning).</summary>
    bool Execute(KeyCommand command)
    {
        var row = SelectedRow();
        switch (command.Kind)
        {
            case KeyCommandKind.MoveDown:
                if (ReferenceEquals(FocusedElement(), SearchBox))
                {
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus(); // Down from the search box lands on the first row
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
                if (row is not null && RingFor(row.Key) is { } ring && RowMenuFor(ring, row.Key) is { } menu) ShowMenu(menu, ring);
                return true;
            case KeyCommandKind.Confirm:
                ConfirmFromKeyboard(row);
                return true;
            case KeyCommandKind.CancelConfirm:
                if (row is { Mode: RowMode.Confirming }) _host.CancelAction(row.Key);
                else _host.CancelBatch();
                return true;
            case KeyCommandKind.FocusSearch:
                _swallowSlash = true;
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

    void OnOpenInBrowser(object? sender, RoutedEventArgs e)
    {
        if (((Control)sender!).Tag is string url) _host.OpenUrl(url);
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
    void OnMergePanelLoaded(object? sender, RoutedEventArgs e)
    {
        var panel = (StackPanel)sender!;
        panel.Children.Clear();
        if (panel.DataContext is not InboxRow { Options: { } options } row) return;
        var key = row.Key;
        var enabled = row.Mode == RowMode.Confirming;
        var selected = _methods.TryGetValue(key, out var chosen) && options.Allowed.Contains(chosen) ? chosen : options.Selected;

        var methods = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        var label = new TextBlock
        {
            Text = "Method",
            FontSize = 11.5,
            Margin = new Avalonia.Thickness(0, 0, 10, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        BindToken(label, TextBlock.ForegroundProperty, "Brush.Muted");
        methods.Children.Add(label);
        foreach (var method in options.Allowed)
        {
            var radio = new RadioButton
            {
                Content = MethodLabel(method),
                GroupName = "merge:" + key,
                IsChecked = method == selected,
                IsEnabled = enabled,
                Margin = new Avalonia.Thickness(0, 0, 12, 0),
                FontSize = 11.5,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            BindToken(radio, TemplatedControl.ForegroundProperty, "Brush.Text");
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked != true) return;
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
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
            FontSize = 11.5,
        };
        BindToken(box, TemplatedControl.ForegroundProperty, "Brush.Text");
        box.IsCheckedChanged += (_, _) => _deleteBranch[key] = box.IsChecked == true;
        panel.Children.Add(box);
    }
}
