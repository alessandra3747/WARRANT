using System.Collections.Concurrent;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Warrant.Infrastructure;

public sealed class EntityMetadataCache(TimeSpan? ttl = null)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, EntityMetadata Meta)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(60);

    public async Task<EntityMetadata> GetAsync(IDataverseClientFactory factory, string logicalName, EntityFilters filters, CancellationToken ct)
    {
        var key = $"{logicalName}:{filters}";
        
        if (_cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.At < _ttl)
        {
            return hit.Meta;
        }

        var svc = await factory.CreateAsync(ct);
        var meta = ((RetrieveEntityResponse)await svc.ExecuteAsync(new RetrieveEntityRequest { LogicalName = logicalName, EntityFilters = filters })).EntityMetadata;
        _cache[key] = (DateTimeOffset.UtcNow, meta);
        
        return meta;
    }
}