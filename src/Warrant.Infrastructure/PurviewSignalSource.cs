using Microsoft.Extensions.Logging;
using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Infrastructure;

public sealed class PurviewSignalSource(ILogger<PurviewSignalSource> log) : IExternalSignalSource
{
    public string Capability => "Purview";
    public string Mode => "Native";
    private readonly ILogger<PurviewSignalSource> _log = log;

    public Task<IReadOnlyList<Signal>> ReadAsync(string logicalName, CancellationToken ct = default)
    {
        _log.LogInformation("Purview (native) signal regarding {Asset}", logicalName);
        IReadOnlyList<Signal> s = [new Signal("PurviewDLP", "NoSensitiveGroundingBlock", "Purview", "Native", DateTimeOffset.UtcNow)];
        return Task.FromResult(s);
    }
}