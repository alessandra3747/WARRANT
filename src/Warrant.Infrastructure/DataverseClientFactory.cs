using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Warrant.Application;

namespace Warrant.Infrastructure;

public interface IDataverseClientFactory 
{ 
    Task<ServiceClient> CreateAsync(CancellationToken ct = default); 
}

public sealed class DataverseClientFactory(IOptions<WarrantOptions> opt) : IDataverseClientFactory
{
    private readonly DataverseOptions _o = opt.Value.Dataverse;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ServiceClient? _client;

    public async Task<ServiceClient> CreateAsync(CancellationToken ct = default)
    {
        if (_client is { IsReady: true })
        {
            return _client;
        }

        await _lock.WaitAsync(ct);

        try
        {
            if (_client is { IsReady: true })
            {
                return _client;
            }

            _client = Build();
            
            if (!_client.IsReady)
            {
                throw new InvalidOperationException($"Dataverse is not ready: {_client.LastError}");
            }
            
            return _client;
        }
        finally 
        { 
            _lock.Release(); 
        }
    }

    private ServiceClient Build()
    {
        if (_o.UseManagedIdentity)
        {
            var cred = new DefaultAzureCredential();
            var scope = $"{_o.Url.TrimEnd('/')}/.default";
            return new ServiceClient(new Uri(_o.Url), async _ => (await cred.GetTokenAsync(new TokenRequestContext([scope]))).Token);
        }
        return new ServiceClient($"AuthType=ClientSecret;Url={_o.Url};ClientId={_o.ClientId};ClientSecret={_o.ClientSecret}");
    }
}