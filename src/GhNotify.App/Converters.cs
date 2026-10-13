using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GhNotify.Core;

namespace GhNotify.App;

/// <summary>null, "", false and empty collections → Collapsed; anything else → Visible.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => Visibility.Collapsed,
        string s => s.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
        bool b => b ? Visibility.Visible : Visibility.Collapsed,
        ICollection c => c.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Row → its detail list with the full repo name first (the meta line shows the shortened one).</summary>
public sealed class RowDetailsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is InboxRow r ? For(r) : Array.Empty<ItemDetail>();

    public static IReadOnlyList<ItemDetail> For(InboxRow r) =>
        [new ItemDetail("Repo", r.Repo), .. r.Details.Where(d => d.Label != "Repo")];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class UpperCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpper(CultureInfo.CurrentCulture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
