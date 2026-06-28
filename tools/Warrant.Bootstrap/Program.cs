using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

const string prefix = "wrnt";
var url    = Environment.GetEnvironmentVariable("DV_URL")!;
var appId  = Environment.GetEnvironmentVariable("DV_CLIENTID")!;
var secret = Environment.GetEnvironmentVariable("DV_SECRET")!;

using var svc = new ServiceClient($"AuthType=ClientSecret;Url={url};ClientId={appId};ClientSecret={secret}");
if (!svc.IsReady)
{
    throw new Exception($"Dataverse is not ready: {svc.LastError}");
}

Console.WriteLine("Creating wrnt_dataasset...");  CreateAssetTable(svc);
Console.WriteLine("Creating wrnt_contract...");   CreateContractTable(svc);
Console.WriteLine("Creating wrnt_waiver...");   CreateWaiverTable(svc);
Console.WriteLine("Done.");

static void CreateAssetTable(IOrganizationService svc)
{
    var e = new EntityMetadata { SchemaName = $"{prefix}_dataasset", DisplayName = L("Data Asset"),
        DisplayCollectionName = L("Data Assets"), OwnershipType = OwnershipTypes.UserOwned };
    
    var primary = new StringAttributeMetadata { SchemaName = $"{prefix}_name", DisplayName = L("Name"),
        MaxLength = 200, RequiredLevel = new(AttributeRequiredLevel.ApplicationRequired) };
    
    svc.Execute(new CreateEntityRequest { Entity = e, PrimaryAttribute = primary });
    
    AddString(svc, e.SchemaName, "logicalname", "Logical Name", 200);
    AddOptionSet(svc, e.SchemaName, "sourcetype", "Source Type", ("Dataverse",1), ("SharePoint",2), ("External",3));
    AddString(svc, e.SchemaName, "owner_email", "Owner Email", 320);
    AddOptionSet(svc, e.SchemaName, "sensitivity", "Sensitivity", ("None",1), ("PII",2), ("Sensitive",3));
}

static void CreateContractTable(IOrganizationService svc)
{
    var e = new EntityMetadata { SchemaName = $"{prefix}_contract", DisplayName = L("Grounding Contract"),
        DisplayCollectionName = L("Grounding Contracts"), OwnershipType = OwnershipTypes.UserOwned };
    
    var primary = new StringAttributeMetadata { SchemaName = $"{prefix}_name", DisplayName = L("Contract Id"),
        MaxLength = 100, RequiredLevel = new(AttributeRequiredLevel.ApplicationRequired) };
    
    svc.Execute(new CreateEntityRequest { Entity = e, PrimaryAttribute = primary });
    
    AddOptionSet(svc, e.SchemaName, "verdict", "Verdict", ("Ready",1), ("Conditional",2), ("No",3));
    AddDecimal(svc, e.SchemaName, "hallucinationrisk", "Hallucination Risk", 0, 1);
    AddDateTime(svc, e.SchemaName, "validfrom", "Valid From");
    AddDateTime(svc, e.SchemaName, "validuntil", "Valid Until");
    AddOptionSet(svc, e.SchemaName, "status", "Status", ("Valid",1), ("Revoked",2));
    AddInteger(svc, e.SchemaName, "version", "Version");
    AddMemo(svc, e.SchemaName, "jws", "JWS Signature", 100000);
    AddString(svc, e.SchemaName, "asset_logical", "Asset Logical Name", 200);
    AddString(svc, e.SchemaName, "agent_id", "Agent Id", 100);
    AddMemo(svc, e.SchemaName, "signal_provenance", "Signal Provenance", 100000);
    AddString(svc, e.SchemaName, "contenthash", "Content Hash", 100);
    AddMemo(svc, e.SchemaName, "findings", "Findings", 100000);
}

static void CreateWaiverTable(IOrganizationService svc)
{
    var e = new EntityMetadata { SchemaName = $"{prefix}_waiver", DisplayName = L("Waiver"),
        DisplayCollectionName = L("Waivers"), OwnershipType = OwnershipTypes.UserOwned };
    
    var primary = new StringAttributeMetadata { SchemaName = $"{prefix}_name", DisplayName = L("Waiver Id"),
        MaxLength = 200, RequiredLevel = new(AttributeRequiredLevel.ApplicationRequired) };
    
    svc.Execute(new CreateEntityRequest { Entity = e, PrimaryAttribute = primary });
    
    AddString(svc, e.SchemaName, "asset_logical", "Asset Logical Name", 200);
    AddString(svc, e.SchemaName, "agent_id", "Agent Id", 100);
    AddString(svc, e.SchemaName, "category", "Finding Category", 100);
    AddString(svc, e.SchemaName, "field", "Field", 200);
    AddDateTime(svc, e.SchemaName, "until", "Until");
    AddBool(svc, e.SchemaName, "active", "Active");
}

static void AddBool(IOrganizationService s, string en, string n, string d) =>
    s.Execute(new CreateAttributeRequest { EntityName = en, Attribute =
        new BooleanAttributeMetadata {
            SchemaName = $"{prefix}_{n}", DisplayName = L(d), RequiredLevel = Opt(),
            OptionSet = new BooleanOptionSetMetadata(new OptionMetadata(L("Yes"), 1), new OptionMetadata(L("No"), 0)) }
        }
    );

static Label L(string s) => new(s, 1033);

static AttributeRequiredLevelManagedProperty Opt() => new(AttributeRequiredLevel.None);

static void AddString(IOrganizationService s, string en, string n, string d, int len) =>
    s.Execute(new CreateAttributeRequest { EntityName = en, Attribute =
        new StringAttributeMetadata { SchemaName = $"{prefix}_{n}", DisplayName = L(d), MaxLength = len, RequiredLevel = Opt() }});

static void AddMemo(IOrganizationService s, string en, string n, string d, int len) =>
    s.Execute(new CreateAttributeRequest { EntityName = en, Attribute =
        new MemoAttributeMetadata { SchemaName = $"{prefix}_{n}", DisplayName = L(d), MaxLength = len, RequiredLevel = Opt() }});

static void AddInteger(IOrganizationService s, string en, string n, string d) =>
    s.Execute(new CreateAttributeRequest { EntityName = en, Attribute =
        new IntegerAttributeMetadata { SchemaName = $"{prefix}_{n}", DisplayName = L(d), RequiredLevel = Opt() }});

static void AddDecimal(IOrganizationService s, string en, string n, string d, decimal mn, decimal mx) =>
    s.Execute(new CreateAttributeRequest { EntityName = en, Attribute =
        new DecimalAttributeMetadata { SchemaName = $"{prefix}_{n}", DisplayName = L(d), MinValue = mn, MaxValue = mx, Precision = 4, RequiredLevel = Opt() }});

static void AddDateTime(IOrganizationService s, string en, string n, string d) =>
    s.Execute(new CreateAttributeRequest { EntityName = en, Attribute =
        new DateTimeAttributeMetadata { SchemaName = $"{prefix}_{n}", DisplayName = L(d), Format = DateTimeFormat.DateAndTime, RequiredLevel = Opt() }});

static void AddOptionSet(IOrganizationService s, string en, string n, string d, params (string l,int v)[] opts)
{
    var os = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
    foreach (var (l,v) in opts) os.Options.Add(new OptionMetadata(L(l), v));
    s.Execute(new CreateAttributeRequest { EntityName = en, Attribute =
        new PicklistAttributeMetadata { SchemaName = $"{prefix}_{n}", DisplayName = L(d), OptionSet = os, RequiredLevel = Opt() }});
}