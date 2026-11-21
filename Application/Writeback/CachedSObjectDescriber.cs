using Microsoft.Extensions.Caching.Memory;
using Salesforce.Clients;
using Salesforce.Writeback;

namespace Application.Writeback;

/// <summary>
/// Source Object describes, kept for a while: every Binding validation and every Writeback plan reads one, and
/// an object's fields change only when someone edits it in Setup.
/// </summary>
/// <remarks>
/// Ten minutes, so a field made External ID or Unique in Setup is seen without a restart. A describe that
/// failed is not kept.
/// </remarks>
public sealed class CachedSObjectDescriber : ISObjectDescriber {
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IMemoryCache _cache;
    private readonly SalesforceRestClient _client;

    public CachedSObjectDescriber(IMemoryCache cache, SalesforceRestClient client) {
        _cache = cache;
        _client = client;
    }

    public async Task<SObjectDescribe?> DescribeAsync(string sObject, CancellationToken cancellationToken = default) {
        var key = $"describe_{sObject}";
        if (_cache.TryGetValue(key, out SObjectDescribe? cached)) {
            return cached;
        }

        var describe = await _client.DescribeAsync(sObject, cancellationToken).ConfigureAwait(false);
        if (describe is not null) {
            _cache.Set(key, describe, Lifetime);
        }
        return describe;
    }
}
