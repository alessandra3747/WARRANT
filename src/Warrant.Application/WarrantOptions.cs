namespace Warrant.Application;

public sealed class WarrantOptions
{
    public const string SectionName = "Warrant";
    public DataverseOptions Dataverse { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
    public KeyVaultOptions KeyVault { get; set; } = new();
    public StorageOptions Storage { get; set; } = new();
    public WarrantCapabilities Capabilities { get; set; } = new();
    public DecisionPolicyOptions Decision { get; set; } = new();
    public NotificationOptions Notifications { get; set; } = new();
    public OnDemandOptions OnDemand { get; set; } = new();
    public int MetadataCacheMinutes { get; set; } = 60;
}

public sealed class DataverseOptions
{
    public string Url { get; set; } = "";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public bool UseManagedIdentity { get; set; } = false;
}

public sealed class AzureOpenAIOptions 
{ 
    public string Endpoint { get; set; } = ""; 
    public string Deployment { get; set; } = "gpt-4.1-mini"; 
    public string? ApiKey { get; set; } 
}

public sealed class KeyVaultOptions 
{ 
    public string KeyId { get; set; } = ""; 
}

public sealed class StorageOptions 
{ 
    public string TableEndpoint { get; set; } = ""; 
    public string? ConnectionString { get; set; } 
}

public sealed class WarrantCapabilities
{
    public bool Purview { get; set; } = false;
    public bool Defender { get; set; } = false;
    public bool Agent365 { get; set; } = false;
    public bool SharePointAdvancedManagement { get; set; } = false;
    public bool FabricIQ { get; set; } = false;
}

public sealed class DecisionPolicyOptions
{
    public double NoRiskThreshold { get; set; } = 0.66;
    public double MinCompletenessForReady { get; set; } = 0.80;
    public double MinCompletenessForConditional { get; set; } = 0.50;
    public int MaxDuplicates { get; set; } = 0;
}

public sealed class NotificationOptions 
{ 
    public string? TeamsWebhookUrl { get; set; } 
}

public sealed class OnDemandOptions 
{ 
    public string? CertifyEndpoint { get; set; } 
    public string? FunctionKey { get; set; } 
}