using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Options;
using Warrant.Application;

namespace Warrant.Certification;

public sealed class KeyVaultJwsVerifier(IOptions<WarrantOptions> opt) : IContractVerifier
{
    private readonly CryptographyClient _crypto = new CryptographyClient(new Uri(opt.Value.KeyVault.KeyId), new DefaultAzureCredential());

    public async Task<(bool IsValid, string? PayloadJson)> VerifyAsync(string jws, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jws))
        {
            return (false, null);
        }
        var parts = jws.Split('.');
        if (parts.Length != 3)
        {
            return (false, null);
        }
        var signingInput = $"{parts[0]}.{parts[1]}";
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(signingInput));

        byte[] sig;
        try 
        { 
            sig = FromB64Url(parts[2]); 
        }
        catch { return (false, null); }
        try
        {
            var res = await _crypto.VerifyAsync(SignatureAlgorithm.ES256, digest, sig, ct);
            if (!res.IsValid)
            {
                return (false, null);
            }
            var payloadJson = Encoding.UTF8.GetString(FromB64Url(parts[1]));
            return (true, payloadJson);
        }
        catch { return (false, null); }
    }

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) 
        { 
            case 2: s += "=="; break; 
            case 3: s += "="; break; 
        }
        return Convert.FromBase64String(s);
    }
}