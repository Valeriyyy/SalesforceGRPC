using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salesforce.Auth;
using Salesforce.Writeback;
using System.Net;
using System.Text;

namespace Salesforce.Clients;

/// <summary>
/// The Salesforce REST data API: Source Object describes, and the record writes Writeback makes.
/// </summary>
public class SalesforceRestClient : BaseSalesforceClient, ISalesforceRecordWriter, ISObjectDescriber {

    /// <summary>
    /// Sent on every Writeback call. Salesforce copies it into the <c>changeOrigin</c> of the change event the
    /// write causes, so a record's history shows the change came from this application (ADR 0007: kept for
    /// traceability, not to suppress that event).
    /// </summary>
    public const string CallOptionsHeader = "Sforce-Call-Options";
    public const string CallOptionsClient = "client=StarPush";

    public SalesforceRestClient(HttpClient httpClient, IOrgConnectionSource connections,
        IOptions<SalesforceConfig> config, ILogger<SalesforceRestClient> logger)
        : base(httpClient, config.Value, logger, connections) {
    }

    public async Task<SObjectDescribe?> DescribeAsync(string sObject, CancellationToken cancellationToken = default) {
        var url = await DataUrlAsync($"sobjects/{Uri.EscapeDataString(sObject)}/describe", cancellationToken).ConfigureAwait(false);

        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) {
            return null;
        }

        var content = await ReadForWritebackAsync(response, "describe", cancellationToken).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<SObjectDescribe>(content);
    }

    public Task<IReadOnlyList<RecordSaveResult>> UpdateAsync(string sObject,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records, CancellationToken cancellationToken = default) =>
        SendCollectionAsync("composite/sobjects", sObject, records, cancellationToken);

    public Task<IReadOnlyList<RecordSaveResult>> UpsertAsync(string sObject, string externalIdField,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records, CancellationToken cancellationToken = default) =>
        SendCollectionAsync(
            $"composite/sobjects/{Uri.EscapeDataString(sObject)}/{Uri.EscapeDataString(externalIdField)}",
            sObject, records, cancellationToken);

    private async Task<IReadOnlyList<RecordSaveResult>> SendCollectionAsync(string relativePath, string sObject,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records, CancellationToken cancellationToken) {
        if (records.Count is 0) {
            return [];
        }
        if (records.Count > ISalesforceRecordWriter.MaxRecordsPerCall) {
            throw new ArgumentException(
                $"One call takes at most {ISalesforceRecordWriter.MaxRecordsPerCall} records; {records.Count} were given.", nameof(records));
        }

        var url = await DataUrlAsync(relativePath, cancellationToken).ConfigureAwait(false);

        // Built as a JObject rather than serialized with the client's shared settings, which drop nulls: an
        // explicit null is how Writeback clears a Salesforce field.
        var body = new JObject {
            ["allOrNone"] = false,
            ["records"] = new JArray(records.Select(r => {
                var record = new JObject { ["attributes"] = new JObject { ["type"] = sObject } };
                foreach (var (field, value) in r) {
                    record[field] = value is null ? JValue.CreateNull() : JToken.FromObject(value);
                }
                return record;
            }))
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, url) {
            Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Add(CallOptionsHeader, CallOptionsClient);

        HttpResponseMessage response;
        try {
            response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        } catch (HttpRequestException ex) {
            throw new SalesforceTransientException($"Salesforce could not be reached: {ex.Message}", ex);
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new SalesforceTransientException("Salesforce did not answer in time.", ex);
        }

        using (response) {
            var content = await ReadForWritebackAsync(response, sObject, cancellationToken).ConfigureAwait(false);
            var results = JsonConvert.DeserializeObject<List<RecordSaveResult>>(content) ?? [];
            if (results.Count != records.Count) {
                throw new SalesforceTransientException(
                    $"Salesforce answered {results.Count} result(s) for {records.Count} record(s); the call is treated as undecided.");
            }
            return results;
        }
    }

    private async Task<string> DataUrlAsync(string relativePath, CancellationToken cancellationToken) {
        var orgUrl = await _connections.GetOrgUrlAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(orgUrl)) {
            throw new NoOrgConnectionException();
        }
        return $"{orgUrl.TrimEnd('/')}/services/data/v{_config.ApiVersion}/{relativePath}";
    }

    /// <summary>
    /// Reads a successful body, or turns a failed call into what Writeback needs to know: may it be retried,
    /// or is the session refused?
    /// </summary>
    /// <remarks>
    /// A refusal of the whole call (malformed request, an object the user cannot see) is not transient, but no
    /// record was decided either; it is reported per record by the caller as if every record were refused.
    /// </remarks>
    private async Task<string> ReadForWritebackAsync(HttpResponseMessage response, string what,
        CancellationToken cancellationToken) {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) {
            return content;
        }

        _logger.LogWarning("Salesforce {What} call failed with {StatusCode}: {Body}", what, (int)response.StatusCode, content);

        var codes = ParseStatusCodes(content);
        if (response.StatusCode == HttpStatusCode.Unauthorized || codes.Contains("INVALID_SESSION_ID")) {
            throw new SalesforceUnauthorizedException(
                $"Salesforce refused the Run-as User's session ({(int)response.StatusCode}). Check the Org Connection.");
        }

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests
            || codes.Any(RecordError.TransientStatusCodes.Contains)) {
            throw new SalesforceTransientException(
                $"Salesforce answered {(int)response.StatusCode} {string.Join(", ", codes)}; the call will be retried.");
        }

        throw new SalesforceRequestRefusedException((int)response.StatusCode, codes, content);
    }

    private static List<string> ParseStatusCodes(string content) {
        try {
            return JToken.Parse(content) switch {
                JArray array => array.OfType<JObject>()
                    .Select(e => e.Value<string>("errorCode") ?? e.Value<string>("statusCode"))
                    .OfType<string>().ToList(),
                JObject single => [single.Value<string>("errorCode") ?? single.Value<string>("statusCode") ?? ""],
                _ => []
            };
        } catch (JsonException) {
            return [];
        }
    }
}
