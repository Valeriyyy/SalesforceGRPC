using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Database.Targets;

public sealed class SqlServerEngineProfile : TargetEngineProfile {
    public SqlServerEngineProfile(ILoggerFactory loggerFactory, bool debugQuery = false)
        : base(loggerFactory, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.SqlServer;

    public const string EncryptOption = "encrypt";
    public const string TrustServerCertificateOption = "trustServerCertificate";

    public override IReadOnlyList<FieldDefinition> Fields { get; } = [
        new() { Name = FieldNames.Host, Label = "Server", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Port, Label = "Port", Kind = FieldKind.Int, Required = true, Default = "1433" },
        new() { Name = FieldNames.DatabaseName, Label = "Database", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Username, Label = "Username", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Password, Label = "Password", Kind = FieldKind.Secret, Required = true },
        // Microsoft.Data.SqlClient defaults to Encrypt=true and fails outright against a self-signed
        // on-prem certificate unless TrustServerCertificate is also set — surfaced as a choice here so
        // that failure is a validation decision, not an opaque driver exception.
        new() { Name = EncryptOption, Label = "Encrypt", Kind = FieldKind.Bool, Default = "true" },
        new() { Name = TrustServerCertificateOption, Label = "Trust server certificate", Kind = FieldKind.Bool, Default = "false" }
    ];

    public override string BuildConnectionString(TargetConnectionDetails details) =>
        new SqlConnectionStringBuilder {
            DataSource = $"{details.Host},{details.Port ?? 1433}",
            InitialCatalog = details.DatabaseName,
            UserID = details.Username,
            Password = details.Password,
            Encrypt = bool.Parse(details.Options.GetValueOrDefault(EncryptOption, "true")),
            TrustServerCertificate = bool.Parse(details.Options.GetValueOrDefault(TrustServerCertificateOption, "false"))
        }.ConnectionString;

    public override IRepository CreateRepository(string connectionString) =>
        new SqlServerRepository(LoggerFactory.CreateLogger<SqlServerRepository>(), connectionString, DebugQuery);
}
