using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GhNotify.Core;

namespace GhNotify.UI;

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

    void OnBatchApprove(object? sender, RoutedEventArgs e) => BeginBatch(ItemAction.Approve);

    void OnBatchReject(object? sender, RoutedEventArgs e) => BeginBatch(ItemAction.Reject);

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

    void OnBatchSlotLoaded(object? sender, RoutedEventArgs e)
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
            Dispatcher.UIThread.Post(() =>
            {
                if (_batchComment is not { IsEnabled: true, IsVisible: true } box) return;
                box.Focus();
                box.CaretIndex = Math.Min(caret, box.Text?.Length ?? 0);
            }, DispatcherPriority.Loaded);
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
                _batchPanel.IsVisible = false;
                _batchComment!.Clear();
                _batchCommentCaret = null;
                _batchViews.Clear();
                _batchLines!.Children.Clear();
            }
            return;
        }

        var panel = BatchPanel();
        panel.IsVisible = true;
        if (!sections.Any(s => s.Id == BatchApproval.SectionId)) AttachBatchPanel(BatchFallbackSlot);
        else if (panel.Parent is null or Border { Name: "BatchFallbackSlot" })
        {
            // No rebuild happened, so the section's slot is already there; after a rebuild its Loaded handler attaches the panel.
            var slot = Sections.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => string.IsNullOrEmpty(b.Name) && b.Tag as string == BatchApproval.SectionId);
            if (slot is not null) AttachBatchPanel(slot);
        }

        var lines = batch.Lines;
        var reject = batch.BatchAction == ItemAction.Reject;
        var verb = reject ? "Reject" : "Approve";
        _batchTitle!.Text = batch.Phase == BatchPhase.Previewing
            ? $"{verb} all — checking {lines.Count} items…"
            : batch.Phase == BatchPhase.Done ? $"{verb} all — done" : $"{verb} all — {lines.Count} waiting";

        SyncBatchLines(lines, batch.Phase);

        _batchCommentLabel!.IsVisible = _batchComment!.IsVisible =
            batch.Phase is BatchPhase.Previewing or BatchPhase.Ready;
        _batchComment.IsEnabled = batch.Phase == BatchPhase.Ready;

        var status = batch.Phase switch
        {
            BatchPhase.Running => "Sending…",
            BatchPhase.Done => batch.Summary,
            _ => null,
        };
        _batchStatus!.Text = status ?? "";
        _batchStatus.IsVisible = !string.IsNullOrEmpty(status);

        _batchCancel!.IsVisible = batch.Phase != BatchPhase.Running;
        _batchCancel.Content = batch.Phase == BatchPhase.Done ? "Close" : "Cancel";
        _batchConfirm!.IsVisible = batch.Phase is BatchPhase.Previewing or BatchPhase.Ready or BatchPhase.Running;
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
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0), MinWidth = 0 };
                box.Click += (_, _) => _host.Batch.SetChecked(key, box.IsChecked == true); // a user click only; syncing sets IsChecked too
                var head = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
                BindToken(head, TextBlock.ForegroundProperty, "Brush.Text");
                var detail = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
                var text = new StackPanel();
                text.Children.Add(head);
                text.Children.Add(detail);
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
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
            ToolTip.SetTip(head, line.Item.Title);
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
            BindToken(detail, TextBlock.ForegroundProperty, brush);
        }
    }

    Border BatchPanel()
    {
        if (_batchPanel is not null) return _batchPanel;

        _batchTitle = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 12 };
        _batchLines = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _batchCommentLabel = new TextBlock { Text = "Comment for all (optional)", FontSize = 11, Margin = new Thickness(0, 8, 0, 3) };
        BindToken(_batchCommentLabel, TextBlock.ForegroundProperty, "Brush.Muted");
        _batchComment = new TextBox
        {
            Theme = (ControlTheme)this.FindResource("Style.FieldBox")!,
            FontSize = 11.5,
            Padding = new Thickness(6, 4, 6, 4),
            MaxLength = 2000,
            BorderThickness = new Thickness(1),
        };
        _batchStatus = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        BindToken(_batchStatus, TextBlock.ForegroundProperty, "Brush.Text");

        var small = (ControlTheme)this.FindResource("Small")!;
        var primary = (ControlTheme)this.FindResource("SmallPrimary")!;
        _batchCancel = new Button { Theme = small, Content = "Cancel", Tag = BatchGuardKey, Name = "BatchCancel" };
        _batchCancel.Click += (_, _) => _host.CancelBatch();
        _batchConfirm = new Button { Theme = primary, Tag = BatchGuardKey, IsEnabled = false, Name = "BatchConfirm" };
        _batchConfirm.Click += OnBatchConfirm;
        HookConfirm(_batchConfirm);
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

        _batchPanel = new Border
        {
            Name = "BatchPanel",
            Margin = new Thickness(10, 4, 10, 4),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = content,
        };
        BindToken(_batchPanel, Border.BackgroundProperty, "Brush.Raised");
        BindToken(_batchPanel, Border.BorderBrushProperty, "Brush.Hairline");
        return _batchPanel;
    }

    void OnBatchConfirm(object? sender, RoutedEventArgs e)
    {
        // Same pointer guard as a row's confirm: a pointer click only counts after the pointer moved onto the enabled button.
        if (_pointerInput && !Guard(BatchGuardKey).IsArmed) return;
        _ = _host.ConfirmBatchAsync(_batchComment?.Text);
    }
}
