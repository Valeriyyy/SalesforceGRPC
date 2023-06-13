using GrpcClient;
using Npgsql;
using SalesforceGrpc;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Salesforce;
using SqlKata.Compilers;
using SqlKata.Execution;

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

    services.AddSingleton((e) => {
        var connection = new NpgsqlConnection(config.GetConnectionString("postgresLocal"));
        var compiler = new PostgresCompiler();
        return new QueryFactory(connection, compiler);
    });

    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));


    services.AddSingleton<SalesforceAvroDeserializer>();
    services.AddHttpClient<SalesforceAuthClient>();
    services.AddGrpcClient<PubSub.PubSubClient>("SFPubSubClient", options => {
        options.Address = new Uri("https://api.pubsub.salesforce.com:7443");
    }).AddCallCredentials(async (context, metadata, serviceProvider) => {
        var authClient = serviceProvider.GetRequiredService<SalesforceAuthClient>();
        var authResponse = await authClient.GetToken();
        metadata.Add("accesstoken", $"Bearer {authResponse.AccessToken}");
        metadata.Add("instanceurl", authResponse.InstanceUrl);
        metadata.Add("tenantid", config.GetValue<string>("SalesforceConfig:OrgId")!);
    }); ;
    services.AddHostedService<Worker>();
})
.Build();

await host.RunAsync();

