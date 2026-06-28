using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Warrant.Application;
using Warrant.Certification;
using Warrant.Domain;
using Warrant.Guardians;
using Warrant.Infrastructure;
using Warrant.Orchestration;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, s) =>
    {
        s.AddApplicationInsightsTelemetryWorkerService();
        s.ConfigureFunctionsApplicationInsights();
        s.AddHttpClient();

        s.AddOptions<WarrantOptions>()
            .BindConfiguration(WarrantOptions.SectionName)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Dataverse.Url)
                        && !string.IsNullOrWhiteSpace(o.AzureOpenAI.Endpoint)
                        && !string.IsNullOrWhiteSpace(o.KeyVault.KeyId), "Missing required Warrant configuration.")
            .ValidateOnStart();

        s.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<WarrantOptions>>().Value.AzureOpenAI;
            var b = Kernel.CreateBuilder();
            if (string.IsNullOrEmpty(o.ApiKey))
                b.AddAzureOpenAIChatCompletion(o.Deployment, o.Endpoint, new DefaultAzureCredential());
            else
                b.AddAzureOpenAIChatCompletion(o.Deployment, o.Endpoint, o.ApiKey);
            return b.Build();
        });

        s.AddSingleton(sp => new EntityMetadataCache(
            TimeSpan.FromMinutes(sp.GetRequiredService<IOptions<WarrantOptions>>().Value.MetadataCacheMinutes)));

        s.AddSingleton(sp =>
        {
            var d = sp.GetRequiredService<IOptions<WarrantOptions>>().Value.Decision;
            return new DecisionPolicy(d.NoRiskThreshold, d.MinCompletenessForReady, d.MinCompletenessForConditional, d.MaxDuplicates);
        });

        s.AddSingleton<IEventStore, AzureTableEventStore>();
        s.AddSingleton<IDataverseClientFactory, DataverseClientFactory>();
        s.AddSingleton<IAssetLoader, DataverseAssetLoader>();
        s.AddSingleton<IDataverseSecurityReader, DataverseSecurityReader>();

        s.AddSingleton<IGuardian, OntologyMapper>();
        s.AddSingleton<IGuardian, GroundingScorer>();
        s.AddSingleton<IGuardian, QualitySentinel>();
        s.AddSingleton<IGuardian, SignalAggregator>();

        s.AddSingleton<IContractSigner, KeyVaultJwsSigner>();
        s.AddSingleton<IContractProjection, DataverseContractProjection>();
        s.AddSingleton<IWaiverStore, DataverseWaiverStore>();
        s.AddSingleton<INotifier, TeamsNotifier>();
        s.AddSingleton<IMetrics, LoggerMetrics>();
        s.AddSingleton<ICertifier, Certifier>();
        s.AddSingleton<Activities>();

        var caps = ctx.Configuration.GetSection($"{WarrantOptions.SectionName}:Capabilities").Get<WarrantCapabilities>() ?? new();
        s.AddAdaptiveSignalSources(caps);
    })
    .Build();

host.Run();