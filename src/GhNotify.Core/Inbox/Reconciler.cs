namespace GhNotify.Core;

public sealed record ItemChange(InboxItem Old, InboxItem New)
{
    public bool HeadChanged => !string.Equals(Old.HeadSha, New.HeadSha, StringComparison.Ordinal);
}

public sealed record InboxDiff(
    IReadOnlyList<InboxItem> Added,
    IReadOnlyList<InboxItem> Removed,
    IReadOnlyList<ItemChange> Changed,
    IReadOnlyList<InboxItem> InitialLoad)
{
    public static readonly InboxDiff Empty = new([], [], [], []);
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0 && InitialLoad.Count == 0;
}

public sealed record SourceStatus(string Name, bool Healthy, string? Problem, DateTimeOffset? LastSuccess);

public sealed record InboxEntry(InboxItem Item, string Source, bool Stale);

public sealed class Reconciler
{
    readonly object _gate = new();
    readonly Dictionary<string, Dictionary<string, InboxItem>> _bySource = new();
    readonly Dictionary<string, SourceStatus> _status = new();

    public Reconciler(IReadOnlyDictionary<string, IReadOnlyList<InboxItem>>? snapshot = null)
    {
        if (snapshot is null) return;
        foreach (var (name, items) in snapshot)
        {
            _bySource[name] = Index(items);
            _status[name] = new SourceStatus(name, true, null, null);
        }
    }

    public InboxDiff Apply(IReadOnlyDictionary<string, SourceResult> results, DateTimeOffset now)
    {
        lock (_gate)
        {
            List<InboxItem> added = [], removed = [], initial = [];
            List<ItemChange> changed = [];
            foreach (var (name, result) in results)
            {
                if (!result.Healthy)
                {
                    _status[name] = new SourceStatus(name, false, result.Problem, _status.GetValueOrDefault(name)?.LastSuccess);
                    continue;
                }
                var fresh = Index(result.Items);
                if (!_bySource.TryGetValue(name, out var old))
                {
                    initial.AddRange(fresh.Values);
                }
                else
                {
                    foreach (var item in fresh.Values)
                    {
                        if (!old.TryGetValue(item.Key, out var prev)) added.Add(item);
                        else if (prev.HeadSha != item.HeadSha || prev.Detail != item.Detail || prev.Title != item.Title)
                            changed.Add(new ItemChange(prev, item));
                    }
                    removed.AddRange(old.Values.Where(o => !fresh.ContainsKey(o.Key)));
                }
                _bySource[name] = fresh;
                _status[name] = new SourceStatus(name, true, null, now);
            }
            foreach (var name in _status.Keys.Where(n => !results.ContainsKey(n)).ToList())
                _status[name] = _status[name] with { Healthy = false, Problem = "not polled" };
            return new InboxDiff(added, removed, changed, initial);
        }
    }

    public InboxDiff RetireWhere(Func<string, bool> sourcePredicate)
    {
        lock (_gate)
        {
            var removed = new List<InboxItem>();
            foreach (var name in _status.Keys.Where(sourcePredicate).ToList())
            {
                if (_bySource.Remove(name, out var items)) removed.AddRange(items.Values);
                _status.Remove(name);
            }
            return removed.Count == 0 ? InboxDiff.Empty : new InboxDiff([], removed, [], []);
        }
    }

    public IReadOnlyList<InboxEntry> Entries
    {
        get
        {
            lock (_gate)
                return _bySource
                    .SelectMany(s => s.Value.Values.Select(i => new InboxEntry(i, s.Key, !_status[s.Key].Healthy)))
                    .OrderByDescending(e => e.Item.UpdatedAt)
                    .ToList();
        }
    }

    public IReadOnlyList<SourceStatus> Statuses
    {
        get { lock (_gate) return _status.Values.OrderBy(s => s.Name).ToList(); }
    }

    /// <summary>The last-known items of one source (empty when unknown); lets sources fall back after a restart.</summary>
    public IReadOnlyList<InboxItem> ItemsFor(string source)
    {
        lock (_gate) return _bySource.TryGetValue(source, out var items) ? items.Values.ToList() : [];
    }

    public Dictionary<string, List<InboxItem>> Snapshot()
    {
        lock (_gate) return _bySource.ToDictionary(s => s.Key, s => s.Value.Values.ToList());
    }

    static Dictionary<string, InboxItem> Index(IEnumerable<InboxItem> items)
    {
        var d = new Dictionary<string, InboxItem>();
        foreach (var i in items) d.TryAdd(i.Key, i);
        return d;
    }
}
