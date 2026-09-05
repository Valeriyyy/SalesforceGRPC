using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.DataProtection;

/// <summary>
/// Persists the ASP.NET Data Protection key ring to the app database, so the application can decrypt its own
/// stored secrets after a restart with nobody present.
/// </summary>
/// <remarks>
/// Hand-written because the packaged key-ring store is EF Core-backed and there is no EF Core in this
/// solution. The interface is small enough that this is the smaller cost.
/// <para>
/// The database is the store rather than a file because the service is expected to run in a container the
/// operator did not necessarily give a volume to, and a key ring that vanishes on redeploy takes every stored
/// credential with it. What makes that safe is that these rows are themselves encrypted with a certificate
/// resolved from outside the database — see <see cref="ProtectingCertificateResolver"/> and docs/adr/0002.
/// </para>
/// <para>
/// <see cref="IXmlRepository"/> is synchronous by contract, so the two methods here block. They run at most
/// once per key-ring refresh, not per protect call.
/// </para>
/// </remarks>
public sealed class DapperXmlRepository : IXmlRepository {
    private readonly string _connectionString;
    private readonly ILogger<DapperXmlRepository> _logger;

    public DapperXmlRepository(IConfiguration configuration, ILogger<DapperXmlRepository> logger) {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("appDatabase")
                            ?? throw new InvalidOperationException(
                                "ConnectionStrings:appDatabase is not configured, so the Data Protection key ring has nowhere to live.");
    }

    public IReadOnlyCollection<XElement> GetAllElements() {
        using var connection = new NpgsqlConnection(_connectionString);

        var rows = connection.Query<string>(
            "SELECT xml FROM salesforce.data_protection_keys ORDER BY id").ToList();

        var elements = new List<XElement>(rows.Count);
        foreach (var row in rows) {
            try {
                elements.Add(XElement.Parse(row));
            } catch (System.Xml.XmlException ex) {
                // One unreadable element must not take the whole key ring with it: the others may still
                // decrypt what is stored. Loud, because it means something wrote to this table by hand.
                _logger.LogError(ex, "Skipping an unreadable Data Protection key element");
            }
        }

        return elements;
    }

    public void StoreElement(XElement element, string friendlyName) {
        ArgumentNullException.ThrowIfNull(element);

        using var connection = new NpgsqlConnection(_connectionString);

        connection.Execute(
            @"INSERT INTO salesforce.data_protection_keys (friendly_name, xml)
              VALUES (@FriendlyName, @Xml)",
            new { FriendlyName = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });

        _logger.LogInformation("Stored a new Data Protection key element ({FriendlyName})", friendlyName);
    }
}
