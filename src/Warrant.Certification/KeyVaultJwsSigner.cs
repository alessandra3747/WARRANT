using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Options;
using Warrant.Application;

namespace Warrant.Certification;

public interface IContractSigner 
{ 
    Task<string> SignAsync(object payload, CancellationToken ct = default); 
}

public sealed class KeyVaultJwsSigner : IContractSigner
{
    private readonly CryptographyClient _crypto;
    private readonly string _kid;

    public KeyVaultJwsSigner(IOptions<WarrantOptions> opt)
    {
        _kid = opt.Value.KeyVault.KeyId; 
        _crypto = new CryptographyClient(new Uri(_kid), new DefaultAzureCredential()); 
    }

    public async Task<string> SignAsync(object payload, CancellationToken ct = default)
    {
        var header = new { alg = "ES256", typ = "JWT", kid = _kid };
        var input = $"{B64(JsonSerializer.SerializeToUtf8Bytes(header))}.{B64(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(input));
        var sig = await _crypto.SignAsync(SignatureAlgorithm.ES256, digest, ct);
        return $"{input}.{B64(sig.Signature)}";
    }

    private static string B64(byte[] b)  => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

}