using Application.Bindings;
using Application.Connections;
using Application.Services;
using Application.Services.Interfaces;
using Application.Targets;
using Dapper;
using Database.DataProtection;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Database.Targets;
using Database.Utilities;
using GrpcClient;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Salesforce;
using Salesforce.Auth;
using Salesforce.Clients;
using SalesforceGrpc;
using SalesforceGrpc.Health;
using SalesforceGrpc.Schemas;
using SalesforceGrpc.Strategies;
using Serilog;
using System.Net.Http.Headers;
using static System.Console;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Deployment-level settings only. Everything org-specific — credentials, org URL, org id, channel — lives in
// the App Database as the Org Connection, so nothing here is a secret and nothing here needs a restart to change.
builder.Services.Configure<SalesforceConfig>(config.GetSection(nameof(SalesforceConfig)));
builder.Services.AddSingleton(TimeProvider.System);

builder.Logging.AddSerilog();
builder.Services.AddSerilog((serilogServices, lc) => lc
    .ReadFrom.Configuration(config)
    .ReadFrom.Services(serilogServices)
    .Enrich.FromLogContext());

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => {
        resource.AddService(serviceName: "SalesforceGrpcService")
            .AddAttributes(new Dictionary<string, object> {
                { "service.namespace", "SalesforceGrpcService" },
                { "service.version", "1.0.0" },
                { "service.instance.id", "SalesforceGrpcService-1" }
            });
    })
    .WithMetrics(meterProviderBuilder => {
        meterProviderBuilder.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
    }).WithTracing(tracerProviderBuilder => {
        tracerProviderBuilder.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    });

builder.Services.AddMemoryCache();
SqlMapper.AddTypeHandler(new SqlTimeOnlyTypeHandler());
builder.Services.AddSingleton<IMetaRepository, MetaRepository>();
builder.Services.AddSingleton<IAvroSchemaRepository, AvroSchemaRepository>();
builder.Services.AddSingleton<IPlatformEventChannelRepository, PlatformEventChannelRepository>();
builder.Services.AddSingleton<IOrgConnectionRepository, OrgConnectionRepository>();

#region Data Protection
// The one secret this application stores is a private key it generated for itself. The key ring lives in the
// App Database so a restart needs no operator, and is itself protected by a certificate resolved from OUTSIDE
// that database — a key ring sitting beside the ciphertext it protects defends against nothing. See
// docs/adr/0002.
builder.Services.AddSingleton<IProtectingCertificateResolver, ProtectingCertificateResolver>();
builder.Services.AddSingleton<DapperXmlRepository>();

builder.Services.AddSingleton<ISecretProtector>(sp => {
    var resolver = sp.GetRequiredService<IProtectingCertificateResolver>();
    var resolution = resolver.Resolve();
    var logger = sp.GetRequiredService<ILogger<SecretProtector>>();

    if (!resolution.Resolved) {
        // No protecting key means no key ring is persisted at all. Writing an unprotected one to the database
        // would be worse than useless: it would look like encryption while providing none, and the operator
        // would not learn otherwise until it mattered. The host still starts, and the API says what to supply.
        var attempts = string.Join("; ", resolution.Attempts);
        sp.GetRequiredService<ILogger<Program>>().LogWarning(
            "No Data Protection protecting certificate was found, so Salesforce credentials cannot be stored. " +
            "Supply one through DataProtection:ProtectingCertificate. Tried: {Attempts}", attempts);

        return new SecretProtector(null, attempts, logger);
    }

    var dataProtection = new ServiceCollection()
        .AddLogging()
        .AddDataProtection()
        // Explicit, so purpose strings stay stable across deployments and container names.
        .SetApplicationName("SalesforceGrpc")
        .ProtectKeysWithCertificate(resolution.Certificate!)
        .Services
        .AddSingleton<Microsoft.AspNetCore.DataProtection.Repositories.IXmlRepository>(
            _ => sp.GetRequiredService<DapperXmlRepository>())
        .BuildServiceProvider();

    return new SecretProtector(
        dataProtection.GetRequiredService<IDataProtectionProvider>(), resolution.Source, logger);
});
#endregion

builder.Services.AddSingleton<IOrgConnectionProvider, OrgConnectionProvider>();
builder.Services.AddSingleton<IBootstrapStateStore, BootstrapStateStore>();
builder.Services.AddSingleton<IOrgConnectionSource, StoredOrgConnectionSource>();
builder.Services.AddSingleton<IBootstrapOAuthClient, BootstrapOAuthClient>();
// Self-Configuration rests on an assumption not yet proved against a real org — that an OAuth access token is
// accepted as the Metadata API SessionHeader. Until it is, this stands in and tells the user what to do in
// Setup by hand, which is the documented fallback for orgs that would refuse the deploy anyway.
builder.Services.AddSingleton<IOrgSelfConfigurator, ManualRegistrationConfigurator>();
builder.Services.AddScoped<IOrgConnectionService, OrgConnectionService>();


#region Target Connection
// The Target Database is reached through the stored Target Connection, never through configuration. One
// profile per engine; the catalog refuses to construct if any engine is missing a profile.
var debugQuery = config.GetValue<bool>("DebugQuery");
builder.Services.AddSingleton<ITargetEngineProfile>(sp => new PostgresEngineProfile(sp.GetRequiredService<ILoggerFactory>(), debugQuery));
builder.Services.AddSingleton<ITargetEngineProfile>(sp => new SqlServerEngineProfile(sp.GetRequiredService<ILoggerFactory>(), debugQuery));
builder.Services.AddSingleton<ITargetEngineProfile>(sp => new MySqlEngineProfile(sp.GetRequiredService<ILoggerFactory>(), debugQuery));
builder.Services.AddSingleton<ITargetEngineProfile>(sp => new SqliteEngineProfile(sp.GetRequiredService<ILoggerFactory>(), debugQuery));
builder.Services.AddSingleton<ITargetEngineCatalog, TargetEngineCatalog>();
builder.Services.AddSingleton<ITargetConnectionRepository, TargetConnectionRepository>();
builder.Services.AddSingleton<ITargetConnectionProvider, TargetConnectionProvider>();
builder.Services.AddScoped<ITargetConnectionService, TargetConnectionService>();
#endregion

builder.Services.AddTransient<IEventStrategy, CreateStrategy>();
builder.Services.AddTransient<IEventStrategy, UpdateStrategy>();
builder.Services.AddTransient<IEventStrategy, DeleteStrategy>();
builder.Services.AddTransient<IEventStrategy, UndeleteStrategy>();
builder.Services.AddTransient<EventResolver>();

builder.Services.AddSingleton<IConfigurationChangeSignal, ConfigurationChangeSignal>();
builder.Services.AddScoped<IEntitySchemaProvider, PubSubEntitySchemaProvider>();
builder.Services.AddScoped<IBindingService, BindingService>();
builder.Services.AddScoped<ISchemaService, SchemaService>();
builder.Services.AddScoped<IPlatformEventService, PlatformEventService>();
     
builder.Services.AddSingleton<ISalesforceTokenProvider, SalesforceTokenProvider>();
builder.Services.AddTransient<SalesforceAuthHandler>();

// Bare named clients: the token and authorize endpoints are on login/test.salesforce.com, which is derived
// from the connection's production/sandbox flag rather than configured, and neither carries a bearer token.
builder.Services.AddHttpClient(SalesforceTokenProvider.HttpClientName)
    .AddPolicyHandler(SalesforcePollyPolicies.RetryWithBackoff());
builder.Services.AddHttpClient(BootstrapOAuthClient.HttpClientName)
    .AddPolicyHandler(SalesforcePollyPolicies.RetryWithBackoff());

var salesforceConfig = config.GetSection(nameof(SalesforceConfig)).Get<SalesforceConfig>() ?? new SalesforceConfig();

builder.Services.AddGrpcClient<PubSub.PubSubClient>("SFPubSubClient", options => {
    // One endpoint for production and sandbox alike; configurable only for EU data-residency orgs.
    options.Address = new Uri(salesforceConfig.PubSubEndpoint);
}).AddCallCredentials(async (_, metadata, serviceProvider) => {
    // The stream runs as the Run-as User, whose permissions bound what it can see. The tenant id comes from
    // the Org Connection's discovered org id — it is not something the user is asked for, and it is not in
    // configuration any more.
    var tokenProvider = serviceProvider.GetRequiredService<ISalesforceTokenProvider>();
    var connections = serviceProvider.GetRequiredService<IOrgConnectionProvider>();

    var connection = await connections.GetAsync().ConfigureAwait(false)
                     ?? throw new NoOrgConnectionException();
    var authResponse = await tokenProvider.GetAuthToken(SalesforceIdentity.RunAsUser).ConfigureAwait(false);

    metadata.Add("accesstoken", authResponse.AccessToken!);
    metadata.Add("instanceurl", authResponse.InstanceUrl!);
    metadata.Add("tenantid", connection.OrgId
        ?? throw new InvalidOperationException(
            "The Org Connection has no org id, so the Pub/Sub tenant is unknown. Verify the connection first."));
});

// No BaseAddress. The org's host is discovered on the first successful token exchange, so at the moment these
// clients are constructed there may be no org to point at — and Disconnect can replace it without a restart.
// BaseSalesforceClient resolves it per request instead.
builder.Services.AddHttpClient<SalesforceRestClient>(client => {
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    }).AddHttpMessageHandler<SalesforceAuthHandler>()
.AddPolicyHandler(SalesforcePollyPolicies.RetryWithBackoff());

builder.Services.AddHttpClient<SalesforceToolingClient>(client => {
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).AddHttpMessageHandler<SalesforceAuthHandler>()
.AddPolicyHandler(SalesforcePollyPolicies.RetryWithBackoff());
     
//create the directory to save avro files
var schemaSaveDir = config.GetValue<string>("AvroSchemaSaveDirectory");
if (schemaSaveDir != null && !Directory.Exists(schemaSaveDir)) {
    Directory.CreateDirectory(schemaSaveDir);
}

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<SecretProtectionStartupCheck>();

builder.Services.AddHealthChecks()
    .AddCheck<OrgConnectionHealthCheck>(OrgConnectionHealthCheck.Name)
    .AddCheck<TargetConnectionHealthCheck>(TargetConnectionHealthCheck.Name);

builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
