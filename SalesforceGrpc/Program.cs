using Dapper;
using Database.Repositories;
using Database.Repositories.DbDataRepositories;
using Database.Repositories.Interfaces;
using Database.Utilities;
using GrpcClient;
using Microsoft.Extensions.Options;
using SalesforceGrpc;
using SalesforceGrpc.Salesforce;
using SalesforceGrpc.Strategies;
using System.Net.Http.Headers;
using static System.Console;

IHost host = Host.CreateDefaultBuilder(args)
.ConfigureServices((context, services) => {
    // one way to bind settings
    //var settings = new SalesforceConfig();
    //context.Configuration.GetSection("Salesforce").Bind(settings);
    //services.AddSingleton(settings);

    // another way to bind settings
    //services.Configure<SalesforceConfig>(options => context.Configuration.GetSection("Salesforce").Bind(options));
    //services.AddSingleton(sp => sp.GetRequiredService<IOptions<SalesforceConfig>>().Value);

    //services.AddOptions<SalesforceConfig>().Bind(context.Configuration.GetSection("Salesforce"));

    // another way to bind settings
    var config = context.Configuration;
    services.Configure<SalesforceConfig>(config.GetSection(nameof(SalesforceConfig)));
    services.AddSingleton(sp => sp.GetRequiredService<IOptions<SalesforceConfig>>().Value);

    services.AddMemoryCache();
    SqlMapper.AddTypeHandler(new SqlTimeOnlyTypeHandler());
    services.AddSingleton<IMetaRepository, MetaRepository>();
    
    // Register data repository based on configuration
    services.AddSingleton<IDataRepository>(sp => {
        var targetingDbType = config.GetValue<string>("TargetingDatabaseType") 
            ?? throw new InvalidOperationException("TargetingDatabaseType is not configured in appsettings.json");
        return DataRepositoryFactory.Create(targetingDbType, sp);
    });
    
    services.AddTransient<IEventStrategy, CreateStrategy>();
    services.AddTransient<IEventStrategy, UpdateStrategy>();
    services.AddTransient<IEventStrategy, DeleteStrategy>();
    services.AddTransient<IEventStrategy, UndeleteStrategy>();
    services.AddTransient<EventResolver>();
    
    services.AddSingleton<ISalesforceTokenProvider, SalesforceTokenProvider>();
    services.AddTransient<SalesforceAuthHandler>();
    services.AddGrpcClient<PubSub.PubSubClient>("SFPubSubClient", options => {
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

    services.AddHttpClient<SalesforceClient>(async (serviceProvider, client) => {
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

    services.AddHostedService<Worker>();
})
.Build();

await host.RunAsync();