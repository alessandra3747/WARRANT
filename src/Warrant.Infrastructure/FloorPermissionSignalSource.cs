using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Infrastructure;

public sealed class FloorPermissionSignalSource(IDataverseSecurityReader rls) : IExternalSignalSource
{
    public string Capability => "Purview";
    public string Mode => "Floor";
    private readonly IDataverseSecurityReader _rls = rls;

    public async Task<IReadOnlyList<Signal>> ReadAsync(string logicalName, CancellationToken ct = default)
    {
        var p = await _rls.GetAccessPostureAsync(logicalName, ct);
        return [new Signal("PurviewDLP", p.IsOverShared ? "PossibleOverShare" : "Scoped", "DataverseRLS(floor)", "Floor", DateTimeOffset.UtcNow)];
    }
}