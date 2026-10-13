using GhNotify.Core;

namespace GhNotify.Tests.Support;

public sealed class FakeTokenProvider(string token = "tok") : ITokenProvider
{
    public int Calls { get; private set; }
    public int ForcedCalls { get; private set; }
    public Exception? Throw { get; set; }

    public Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        Calls++;
        if (forceRefresh) ForcedCalls++;
        if (Throw is not null) throw Throw;
        return Task.FromResult(token);
    }
}
