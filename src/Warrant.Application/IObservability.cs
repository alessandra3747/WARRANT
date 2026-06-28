namespace Warrant.Application;

public interface INotifier 
{ 
    Task NotifyDowngradeAsync(string asset, string agentId, string fromVerdict, string toVerdict, IReadOnlyList<string> reasons, CancellationToken ct = default); 
}

public interface IMetrics 
{ 
    void Increment(string name, IReadOnlyDictionary<string, string>? tags = null); 
    void Measure(string name, double value, IReadOnlyDictionary<string, string>? tags = null); 
}