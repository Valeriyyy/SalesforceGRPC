using Application.Services;
using Application.Services.Interfaces;
using Dapper;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Database.Utilities;
using GrpcClient;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SalesforceGrpc;
using SalesforceGrpc.Salesforce;
using SalesforceGrpc.Strategies;
using Serilog;
using System.Net.Http.Headers;
using static System.Console;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.Configure<SalesforceConfig>(config.GetSection(nameof(SalesforceConfig)));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SalesforceConfig>>().Value);

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

builder.Services.AddSingleton<IRepository>(sp => {
     var targetingDbType = config.GetValue<string>("TargetingDatabaseType") 
         ?? throw new InvalidOperationException("TargetingDatabaseType is not configured in appsettings.json");
     return RepositoryFactory.Create(targetingDbType, sp);
 });

builder.Services.AddTransient<IEventStrategy, CreateStrategy>();
builder.Services.AddTransient<IEventStrategy, UpdateStrategy>();
builder.Services.AddTransient<IEventStrategy, DeleteStrategy>();
builder.Services.AddTransient<IEventStrategy, UndeleteStrategy>();
builder.Services.AddTransient<EventResolver>();

builder.Services.AddScoped<ISchemaService, SchemaService>();
builder.Services.AddScoped<IFieldMappingService, IFieldMappingService>();
     
builder.Services.AddSingleton<ISalesforceTokenProvider, SalesforceTokenProvider>();
builder.Services.AddTransient<SalesforceAuthHandler>();
builder.Services.AddGrpcClient<PubSub.PubSubClient>("SFPubSubClient", options => {
    options.Address = new Uri("https://api.pubsub.salesforce.com:7443");
}).AddCallCredentials(async (_, metadata, serviceProvider) => {
    // var authClient = serviceProvider.GetRequiredService<SalesforceAuthClient>();
    // var authResponse = await authClient.GetToken();
    var tokenProvider = serviceProvider.GetRequiredService<ISalesforceTokenProvider>();
    var authResponse = await tokenProvider.GetAuthToken();
    metadata.Add("accesstoken", authResponse.AccessToken!);
    metadata.Add("instanceurl", authResponse.InstanceUrl!);
    metadata.Add("tenantid", config.GetValue<string>("SalesforceConfig:OrgId")!);
});

builder.Services.AddHttpClient<SalesforceClient>(async (serviceProvider, client) => {
    var sfConfig = serviceProvider.GetRequiredService<SalesforceConfig>();
    client.BaseAddress = new Uri(sfConfig.OrgUrl!);
    var tokenProvider = serviceProvider.GetRequiredService<ISalesforceTokenProvider>();
    var authResponse = await tokenProvider.GetAuthToken();
    WriteLine("This is access token " + authResponse.AccessToken);
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", authResponse.AccessToken);
}).AddHttpMessageHandler<SalesforceAuthHandler>()
.AddPolicyHandler(SalesforcePollyPolicies.RetryWithBackoff());
     
//create the directory to save avro files
var schemaSaveDir = config.GetValue<string>("AvroSchemaSaveDirectory");
if (schemaSaveDir != null && !Directory.Exists(schemaSaveDir)) {
    Directory.CreateDirectory(schemaSaveDir);
}

builder.Services.AddHostedService<Worker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
