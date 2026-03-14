using Dapper;
using Database;
using GrpcClient;
using Npgsql;
using SalesforceGrpc;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Salesforce;
using SqlKata.Compilers;
using SqlKata.Execution;
using System.Net.Http.Headers;
using System.Reflection;
using Database;
using Microsoft.Extensions.Options;
using static System.Console;
using Polly.Extensions.Http;
using Polly;
using SalesforceGrpc.Database;

var config = new ConfigurationBuilder().AddConfiguration().Build();
IHost host = Host.CreateDefaultBuilder(args)
.ConfigureServices((context, services) => {
    // one way to bind settings
    //var settings = new SalesforceConfig();
    //context.Configuration.GetSection("Salesforce").Bind(settings);
    //services.AddSingleton(settings);

    // another another way to bind settings
    //services.Configure<SalesforceConfig>(options => context.Configuration.GetSection("Salesforce").Bind(options));
    //services.AddSingleton(sp => sp.GetRequiredService<IOptions<SalesforceConfig>>().Value);

    //services.AddOptions<SalesforceConfig>().Bind(context.Configuration.GetSection("Salesforce"));

    // another way to bind settings
    var config = context.Configuration;
    services.Configure<SalesforceConfig>(config.GetSection(nameof(SalesforceConfig)));
    services.AddSingleton(sp => sp.GetRequiredService<IOptions<SalesforceConfig>>().Value);
    services.AddTransient((e) => {
        var connection = new NpgsqlConnection(config.GetConnectionString("postgres"));
        return new QueryFactory(connection, new PostgresCompiler());
    });

    services.AddMemoryCache();
    SqlMapper.AddTypeHandler(new SqlTimeOnlyTypeHandler());
    services.AddSingleton<IMetaRepository, MetaRepository>();

    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

    services.AddHttpClient<SalesforceAuthClient>("SalesforceAuthClient");
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

/*var services = host.Services;
using IServiceScope serviceScope = services.CreateScope();
IServiceProvider provider = serviceScope.ServiceProvider;
var sfClient = provider.GetRequiredService<SalesforceClient>();
await sfClient.GetRecordTypes();

var authClient = provider.GetRequiredService<SalesforceAuthClient>();
var authResponse = await authClient.GetToken();
WriteLine(authResponse.AccessToken);*/