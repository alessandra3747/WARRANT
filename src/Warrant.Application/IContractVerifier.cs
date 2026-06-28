namespace Warrant.Application;

public interface IContractVerifier 
{
    Task<(bool IsValid, string? PayloadJson)> VerifyAsync(string jws, CancellationToken ct = default);
}