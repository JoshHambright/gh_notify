using System.Collections.Concurrent;
using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

public sealed record AdoOrgContext(string Org, string MyId, IReadOnlySet<string> MyIdentityIds);

public sealed record AdoProject(string Id, string Name);

public sealed class AdoDirectory(AdoClient client, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly ConcurrentDictionary<string, (AdoOrgContext Value, DateTimeOffset At)> _contexts = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, (IReadOnlyList<AdoProject> Value, DateTimeOffset At)> _projects = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<string>> DiscoverOrgsAsync(CancellationToken ct)
    {
        var profile = await client.GetAsync("https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1", ct);
        var id = profile.Str("id") ?? throw new HttpRequestException("ADO profile has no id");
        var accounts = await client.GetAsync($"https://app.vssps.visualstudio.com/_apis/accounts?memberId={id}&api-version=7.1", ct);
        return accounts.Arr("value").Select(a => a.Str("accountName")).OfType<string>().ToList();
    }

    public async Task<AdoOrgContext> GetOrgContextAsync(string org, CancellationToken ct)
    {
        if (_contexts.TryGetValue(org, out var hit) && _time.GetUtcNow() - hit.At < TimeSpan.FromHours(24)) return hit.Value;

        var conn = await client.GetAsync($"https://dev.azure.com/{org}/_apis/connectionData", ct);
        var me = conn.Obj("authenticatedUser").Str("id") ?? throw new HttpRequestException($"ADO {org}: no authenticated user");
        var ident = await client.GetAsync(
            $"https://vssps.dev.azure.com/{org}/_apis/identities?identityIds={me}&queryMembership=expanded&api-version=7.1", ct);
        var descriptors = ident.Arr("value").FirstOrDefault().Arr("memberOf").Select(Descriptor).OfType<string>().ToList();

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { me };
        foreach (var chunk in descriptors.Chunk(50))
        {
            var groups = await client.GetAsync(
                $"https://vssps.dev.azure.com/{org}/_apis/identities?descriptors={Uri.EscapeDataString(string.Join(",", chunk))}&api-version=7.1", ct);
            foreach (var g in groups.Arr("value"))
                if (g.Str("id") is { } gid) ids.Add(gid);
        }
        var ctx = new AdoOrgContext(org, me, ids);
        _contexts[org] = (ctx, _time.GetUtcNow());
        return ctx;
    }

    public async Task<IReadOnlyList<AdoProject>> GetProjectsAsync(string org, CancellationToken ct)
    {
        if (_projects.TryGetValue(org, out var hit) && _time.GetUtcNow() - hit.At < TimeSpan.FromHours(1)) return hit.Value;
        var json = await client.GetAsync($"https://dev.azure.com/{org}/_apis/projects?$top=500&api-version=7.1", ct);
        var list = json.Arr("value")
            .Select(p => p.Str("id") is { } id && p.Str("name") is { } name ? new AdoProject(id, name) : null)
            .OfType<AdoProject>().ToList();
        _projects[org] = (list, _time.GetUtcNow());
        return list;
    }

    static string? Descriptor(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Object when e.Str("identityType") is { } t && e.Str("identifier") is { } i => $"{t};{i}",
        _ => null,
    };
}
