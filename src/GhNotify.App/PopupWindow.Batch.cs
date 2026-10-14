using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GhNotify.Core;

namespace GhNotify.App;

// The batch approve/reject panel (v1.3 spec §5). One panel instance lives for the window's lifetime and is moved under the
// Approvals header (or to a fallback slot when that section is gone); its lines are updated in place so a click is never
// swallowed by a rebuild.
public partial class PopupWindow
{
    const string BatchGuardKey = "batch";

    Border? _batchPanel;
    TextBlock? _batchTitle;
    StackPanel? _batchLines;
    TextBlock? _batchCommentLabel;
    TextBox? _batchComment;
    TextBlock? _batchStatus;
    Button? _batchCancel;
    Button? _batchConfirm;
    int? _batchCommentCaret;
    readonly Dictionary<string, (CheckBox Box, TextBlock Head, TextBlock Detail)> _batchViews = new();

    void OnBatchApprove(object sender, RoutedEventArgs e) => BeginBatch(ItemAction.Approve);

    void OnBatchReject(object sender, RoutedEventArgs e) => BeginBatch(ItemAction.Reject);

    async void BeginBatch(ItemAction action)
    {
        try
        {
            if (await _host.BeginBatchAsync(action) is { } message) ShowNotice(message);
        }
        catch (Exception ex)
        {
            Log.Error("could not open the batch panel", ex);
        }
    }

    void OnBatchSlotLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border slot && slot.Tag as string == BatchApproval.SectionId && _host.Batch.Active) AttachBatchPanel(slot);
    }

    void AttachBatchPanel(Border slot)
    {
        var panel = BatchPanel();
        if (ReferenceEquals(panel.Parent, slot)) return;
        if (panel.Parent is Border old) old.Child = null;
        slot.Child = panel;
        if (_batchCommentCaret is { } caret)
        {
            _batchCommentCaret = null;
            Dispatcher.InvokeAsync(() =>
            {
                if (_batchComment is not { IsEnabled: true, IsVisible: true } box) return;
                box.Focus();
                Keyboard.Focus(box);
                box.CaretIndex = Math.Min(caret, box.Text.Length);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Syncs the panel with the coordinator and puts it where it belongs (called on every render).</summary>
    void UpdateBatchPanel(IReadOnlyList<InboxSection> sections)
    {
        var batch = _host.Batch;
        if (batch.Phase == BatchPhase.Idle)
        {
            if (_batchPanel is not null)
            {
                if (_batchPanel.Parent is Border parent) parent.Child = null;
                _batchPanel.Visibility = Visibility.Collapsed;
                _batchComment!.Clear();
                _batchCommentCaret = null;
                _batchViews.Clear();
                _batchLines!.Children.Clear();
            }
            return;
        }

        var panel = BatchPanel();
        panel.Visibility = Visibility.Visible;
        if (!sections.Any(s => s.Id == BatchApproval.SectionId)) AttachBatchPanel(BatchFallbackSlot);
        else if (panel.Parent is null or Border { Name: "BatchFallbackSlot" })
        {
            // No rebuild happened, so the section's slot is already there; after a rebuild its Loaded handler attaches the panel.
            var slot = Descendants(Sections).OfType<Border>().FirstOrDefault(b => b.Name == "" && b.Tag as string == BatchApproval.SectionId);
            if (slot is not null) AttachBatchPanel(slot);
        }

        var lines = batch.Lines;
        var reject = batch.BatchAction == ItemAction.Reject;
        var verb = reject ? "Reject" : "Approve";
        _batchTitle!.Text = batch.Phase == BatchPhase.Previewing
            ? $"{verb} all — checking {lines.Count} items…"
            : batch.Phase == BatchPhase.Done ? $"{verb} all — done" : $"{verb} all — {lines.Count} waiting";

        SyncBatchLines(lines, batch.Phase);

        _batchCommentLabel!.Visibility = _batchComment!.Visibility =
            batch.Phase is BatchPhase.Previewing or BatchPhase.Ready ? Visibility.Visible : Visibility.Collapsed;
        _batchComment.IsEnabled = batch.Phase == BatchPhase.Ready;

        var status = batch.Phase switch
        {
            BatchPhase.Running => "Sending…",
            BatchPhase.Done => batch.Summary,
            _ => null,
        };
        _batchStatus!.Text = status ?? "";
        _batchStatus.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;

        _batchCancel!.Visibility = batch.Phase == BatchPhase.Running ? Visibility.Collapsed : Visibility.Visible;
        _batchCancel.Content = batch.Phase == BatchPhase.Done ? "Close" : "Cancel";
        _batchConfirm!.Visibility = batch.Phase is BatchPhase.Previewing or BatchPhase.Ready or BatchPhase.Running ? Visibility.Visible : Visibility.Collapsed;
        _batchConfirm.Content = batch.Phase == BatchPhase.Previewing ? "Checking…" : batch.ConfirmLabel;
        _batchConfirm.IsEnabled = batch.CanConfirm(DateTimeOffset.Now);
    }

    void SyncBatchLines(IReadOnlyList<BatchLine> lines, BatchPhase phase)
    {
        if (!_batchViews.Keys.SequenceEqual(lines.Select(l => l.Key)))
        {
            _batchViews.Clear();
            _batchLines!.Children.Clear();
            foreach (var line in lines)
            {
                var key = line.Key;
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
                box.Click += (_, _) => _host.Batch.SetChecked(key, box.IsChecked == true);
                var head = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
                head.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                var detail = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
                var text = new StackPanel();
                text.Children.Add(head);
                text.Children.Add(detail);
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(text, 1);
                grid.Children.Add(box);
                grid.Children.Add(text);
                _batchLines.Children.Add(grid);
                _batchViews[key] = (box, head, detail);
            }
        }
        foreach (var line in lines)
        {
            var (box, head, detail) = _batchViews[line.Key];
            box.IsChecked = line.Checked;
            box.IsEnabled = phase == BatchPhase.Ready && line.Status == BatchLineStatus.Ready;
            head.Text = $"{line.RepoDisplay} · {line.Item.Title}";
            head.ToolTip = line.Item.Title;
            var (glyph, brush) = line.Status switch
            {
                BatchLineStatus.Ok => ("✓ ", "Brush.ReadyText"),
                BatchLineStatus.Stale => ("⚠ ", "Brush.Text"),
                BatchLineStatus.Failed => ("✗ ", "Brush.Red"),
                BatchLineStatus.Refused => ("", "Brush.Red"),
                _ => ("", "Brush.Muted"),
            };
            detail.Text = line.Status switch
            {
                BatchLineStatus.Checking => "Checking…",
                BatchLineStatus.Running => "Sending…",
                _ => glyph + line.Message,
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }
    }

    Border BatchPanel()
    {
        if (_batchPanel is not null) return _batchPanel;

        _batchTitle = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 12 };
        _batchLines = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _batchCommentLabel = new TextBlock { Text = "Comment for all (optional)", FontSize = 11, Margin = new Thickness(0, 8, 0, 3) };
        _batchCommentLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        _batchComment = new TextBox { FontSize = 11.5, Padding = new Thickness(6, 4, 6, 4), MaxLength = 2000, BorderThickness = new Thickness(1) };
        _batchComment.SetResourceReference(Control.BackgroundProperty, "Brush.Raised");
        _batchComment.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _batchComment.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Brush.Text");
        _batchComment.SetResourceReference(Control.BorderBrushProperty, "Brush.Hairline");
        _batchStatus = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        _batchStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        var small = (Style)FindResource("Small");
        var primary = (Style)FindResource("SmallPrimary");
        _batchCancel = new Button { Style = small, Content = "Cancel", Tag = BatchGuardKey };
        _batchCancel.Click += (_, _) => _host.CancelBatch();
        _batchConfirm = new Button { Style = primary, Tag = BatchGuardKey, IsEnabled = false };
        _batchConfirm.Click += OnBatchConfirm;
        _batchConfirm.MouseEnter += OnConfirmMouseEnter;
        _batchConfirm.MouseMove += OnConfirmMouseMove;
        _batchConfirm.MouseLeave += OnConfirmMouseLeave;
        _batchConfirm.IsEnabledChanged += OnConfirmEnabledChanged;
        _batchConfirm.PreviewMouseLeftButtonDown += OnConfirmPreviewMouseDown;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_batchCancel);
        buttons.Children.Add(_batchConfirm);

        var content = new StackPanel();
        content.Children.Add(_batchTitle);
        content.Children.Add(_batchLines);
        content.Children.Add(_batchCommentLabel);
        content.Children.Add(_batchComment);
        content.Children.Add(_batchStatus);
        content.Children.Add(buttons);

        _batchPanel = new Border { Margin = new Thickness(10, 4, 10, 4), Padding = new Thickness(10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = content };
        _batchPanel.SetResourceReference(Border.BackgroundProperty, "Brush.Raised");
        _batchPanel.SetResourceReference(Border.BorderBrushProperty, "Brush.Hairline");
        return _batchPanel;
    }

    void OnBatchConfirm(object sender, RoutedEventArgs e)
    {
        // Same pointer guard as a row's confirm: a mouse click only counts after the pointer moved onto the enabled button.
        if (InputManager.Current.MostRecentInputDevice is MouseDevice && !Guard(BatchGuardKey).IsArmed) return;
        _ = _host.ConfirmBatchAsync(_batchComment?.Text);
    }
}
