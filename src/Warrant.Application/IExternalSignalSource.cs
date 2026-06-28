using Warrant.Domain;
namespace Warrant.Application;

public interface IExternalSignalSource
{
    string Capability { get; }
    string Mode { get; }
    Task<IReadOnlyList<Signal>> ReadAsync(string logicalName, CancellationToken ct = default);
}

public interface IDataverseSecurityReader 
{ 
    Task<AccessPosture> GetAccessPostureAsync(string logicalName, CancellationToken ct = default); 
}

public record AccessPosture(bool IsOverShared, string Detail);