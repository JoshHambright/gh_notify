using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>Value converters used by the popup's compiled bindings (ported from the WPF Converters.cs).</summary>
public static class PopupConverters
{
    /// <summary>null, "", false and empty collections → false (hidden); anything else → true.</summary>
    public static readonly IValueConverter HasValue = new FuncValueConverter<object?, bool>(v => v switch
    {
        null => false,
        string s => s.Length > 0,
        bool b => b,
        ICollection c => c.Count > 0,
        _ => true,
    });

    /// <summary>null → false; anything else → true.</summary>
    public static readonly IValueConverter NotNull = new FuncValueConverter<object?, bool>(v => v is not null);

    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string?>(s => s?.ToUpper(CultureInfo.CurrentCulture));

    /// <summary>Row → its detail list with the full repo name first (the meta line shows the shortened one).</summary>
    public static readonly IValueConverter RowDetails = new FuncValueConverter<InboxRow?, IReadOnlyList<ItemDetail>>(r =>
        r is null ? [] : For(r));

    public static IReadOnlyList<ItemDetail> For(InboxRow r) =>
        [new ItemDetail("Repo", r.Repo), .. r.Details.Where(d => d.Label != "Repo")];

    /// <summary>value.ToString() equals the converter parameter (an enum member name, or a literal such as "✓ Ready").</summary>
    public static readonly IValueConverter Eq = new EqualsConverter(negate: false);

    /// <summary>value.ToString() differs from the converter parameter.</summary>
    public static readonly IValueConverter NotEq = new EqualsConverter(negate: true);

    /// <summary>The confirm panel takes a comment: it is open (confirming / sending), has a confirm button, and the action is not merge or re-run.</summary>
    public static readonly IValueConverter ShowsComment = new FuncValueConverter<InboxRow?, bool>(r =>
        r is { Mode: RowMode.Confirming or RowMode.Sending } && !string.IsNullOrEmpty(r.ConfirmLabel)
        && r.ActiveAction is not (ItemAction.Merge or ItemAction.RerunFailed));

    /// <summary>The confirm button shows while confirming or sending, when the preview named a confirm label.</summary>
    public static readonly IValueConverter ShowsConfirm = new FuncValueConverter<InboxRow?, bool>(r =>
        r is { Mode: RowMode.Confirming or RowMode.Sending } && !string.IsNullOrEmpty(r.ConfirmLabel));

    /// <summary>"Cancel", or "Close" once the action finished or failed.</summary>
    public static readonly IValueConverter CancelLabel = new FuncValueConverter<RowMode, string>(m => m is RowMode.Done or RowMode.Failed ? "Close" : "Cancel");

    /// <summary>Reject and re-run are secondary actions (ghost pills); everything else is primary.</summary>
    public static readonly IValueConverter IsGhostAction = new FuncValueConverter<ItemAction, bool>(a => a is ItemAction.Reject or ItemAction.RerunFailed);

    public static readonly IValueConverter IsPrimaryAction = new FuncValueConverter<ItemAction, bool>(a => a is not (ItemAction.Reject or ItemAction.RerunFailed));

    sealed class EqualsConverter(bool negate) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) != negate;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
