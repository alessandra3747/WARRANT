using Warrant.Application;
using Warrant.Certification;
using Warrant.Infrastructure;
using Warrant.Mcp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<WarrantOptions>().BindConfiguration(WarrantOptions.SectionName).ValidateOnStart();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<IDataverseClientFactory, DataverseClientFactory>();
builder.Services.AddSingleton<IContractStore, DataverseContractStore>();
builder.Services.AddSingleton<IContractVerifier, KeyVaultJwsVerifier>();
builder.Services.AddSingleton<ICertifyTrigger, HttpCertifyTrigger>();

builder.Services.AddMcpServer().WithHttpTransport().WithTools<WarrantTools>();

var app = builder.Build();
app.MapMcp();
app.Run();