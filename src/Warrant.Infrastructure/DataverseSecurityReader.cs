using System.Collections.Concurrent;
using Microsoft.Xrm.Sdk.Metadata;
using Warrant.Application;

namespace Warrant.Infrastructure;

public sealed class DataverseSecurityReader(IDataverseClientFactory f, EntityMetadataCache metaCache) : IDataverseSecurityReader
{
    private readonly IDataverseClientFactory _factory = f;
    private readonly EntityMetadataCache _metaCache = metaCache;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, AccessPosture P)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public async Task<AccessPosture> GetAccessPostureAsync(string logicalName, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(logicalName, out var hit) && DateTimeOffset.UtcNow - hit.At < Ttl)
        {
            return hit.P;
        }
        
        var meta = await _metaCache.GetAsync(_factory, logicalName, EntityFilters.Entity, ct);
        var orgOwned = meta.OwnershipType == OwnershipTypes.OrganizationOwned;
        var posture = new AccessPosture(orgOwned, orgOwned ? "Org-owned table (broad reach)" : "User-owned table (role-scoped)");
        _cache[logicalName] = (DateTimeOffset.UtcNow, posture);
        
        return posture;
    }
}