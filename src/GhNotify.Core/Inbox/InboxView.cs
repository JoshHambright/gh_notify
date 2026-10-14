namespace GhNotify.Core;

public enum Attention { Normal, Muted, Waiting, Snoozed, Hidden }

public sealed record ViewEntry(InboxEntry Entry, Attention Attention, string? MutedBy);
public sealed record SnoozedEntry(InboxEntry Entry, Snooze Snooze);

/// <summary>Classifies reconciled entries for display and alerting. Never removes anything from the reconciler.</summary>
public sealed class InboxView
{
    readonly Dictionary<string, Attention> _attention;
    public IReadOnlyList<ViewEntry> Active { get; }
    public IReadOnlyList<ViewEntry> Waiting { get; }
    public IReadOnlyList<SnoozedEntry> Snoozed { get; }
    public int BadgeCount => Active.Count(e => e.Attention == Attention.Normal);

    InboxView(List<ViewEntry> active, List<ViewEntry> waiting, List<SnoozedEntry> snoozed, Dictionary<string, Attention> attention)
    {
        Active = active;
        Waiting = waiting;
        Snoozed = snoozed;
        _attention = attention;
    }

    public Attention AttentionFor(string key) => _attention.GetValueOrDefault(key, Attention.Normal);

    public static InboxView Compose(IReadOnlyList<InboxEntry> entries, FiltersConfig filters, IReadOnlyList<TimedMute> mutes,
        IReadOnlyList<Snooze> snoozes, DateTimeOffset now)
    {
        var hide = Parse(filters.Hide);
        var mute = Parse(filters.Mute).Concat(Parse(mutes.Where(m => m.Until is not { } u || u > now).Select(m => m.Pattern))).ToList();
        var snoozed = snoozes.GroupBy(s => s.Key).ToDictionary(g => g.Key, g => g.First());
        var active = new List<ViewEntry>();
        var waiting = new List<ViewEntry>();
        var sleeping = new List<SnoozedEntry>();
        var attention = new Dictionary<string, Attention>();
        foreach (var e in entries)
        {
            var item = e.Item;
            Attention a;
            if (hide.Any(p => p.Matches(item))) a = Attention.Hidden;
            else if (snoozed.TryGetValue(item.Key, out var s)) { a = Attention.Snoozed; sleeping.Add(new SnoozedEntry(e, s)); }
            else
            {
                var mutedBy = mute.FirstOrDefault(p => p.Matches(item))?.Text;
                if (item.Kind == ItemKind.MyPrWaiting) { a = Attention.Waiting; waiting.Add(new ViewEntry(e, a, mutedBy)); }
                else { a = mutedBy is null ? Attention.Normal : Attention.Muted; active.Add(new ViewEntry(e, a, mutedBy)); }
            }
            attention[item.Key] = a;
        }
        return new InboxView(active, waiting, sleeping, attention);
    }

    static List<FilterPattern> Parse(IEnumerable<string> patterns) =>
        patterns.Select(t => FilterPattern.TryParse(t, out var p, out _) ? p : null).OfType<FilterPattern>().ToList();
}
