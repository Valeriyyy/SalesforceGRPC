using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Salesforce;
using Salesforce.Auth;
using Salesforce.Clients;
using System.Net;
using System.Text;

namespace StarPush.Tests;

/// <summary>
/// The Salesforce calls Writeback makes, driven through <see cref="SalesforceRestClient"/> over a handler that
/// records each request and answers with a scripted response.
/// </summary>
/// <remarks>
/// Asserts the request as Salesforce sees it (method, URL, header, JSON body) and how its answers are read,
/// because a wrong body shape fails only against a real org.
/// </remarks>
public class SalesforceRecordWriterTests {

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string OrgUrl = "https://example.my.salesforce.com";

    private sealed class ScriptedHandler : HttpMessageHandler {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
        public List<(HttpMethod Method, string Url, string Body, HttpRequestMessage Request)> Requests { get; } = [];

        public ScriptedHandler Respond(HttpStatusCode status, string body) {
            _responses.Enqueue((status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body, request));
            var (status, responseBody) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }

    private static SalesforceRestClient Client(ScriptedHandler handler) {
        var connections = Substitute.For<IOrgConnectionSource>();
        connections.GetOrgUrlAsync(Arg.Any<CancellationToken>()).Returns(OrgUrl);
        return new SalesforceRestClient(new HttpClient(handler), connections,
            Options.Create(new SalesforceConfig { ApiVersion = "61.0" }), NullLogger<SalesforceRestClient>.Instance);
    }

    private static Dictionary<string, object?> Record(params (string Field, object? Value)[] values) =>
        values.ToDictionary(v => v.Field, v => v.Value);

    [Fact]
    public async Task Upsert_PatchesTheCollectionOnTheExternalIdField_KeepingNullsAndNamingTheClient() {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.OK,
            """[{"id":"001000000000001AAA","success":true,"created":true,"errors":[]}]""");

        var results = await Client(handler).UpsertAsync("Account", "Row_Key__c",
            [Record(("Row_Key__c", "7"), ("Phone", "555"), ("Fax", null))], Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"{OrgUrl}/services/data/v61.0/composite/sobjects/Account/Row_Key__c", request.Url);
        Assert.Equal("client=StarPush", Assert.Single(request.Request.Headers.GetValues("Sforce-Call-Options")));

        var body = JObject.Parse(request.Body);
        Assert.False(body.Value<bool>("allOrNone"));
        var record = (JObject)Assert.Single(body["records"]!);
        Assert.Equal("Account", record["attributes"]!.Value<string>("type"));
        Assert.Equal("7", record.Value<string>("Row_Key__c"));
        Assert.Equal("555", record.Value<string>("Phone"));
        Assert.Equal(JTokenType.Null, record["Fax"]!.Type);

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.True(result.Created);
        Assert.Equal("001000000000001AAA", result.Id);
    }

    [Fact]
    public async Task Update_PatchesTheCollection_AndReportsEachRefusedRecordWithSalesforcesReason() {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.OK, """
            [{"id":"001000000000001AAA","success":true,"errors":[]},
             {"success":false,"errors":[{"statusCode":"FIELD_CUSTOM_VALIDATION_EXCEPTION","message":"Phone must be E.164","fields":["Phone"]}]}]
            """);

        var results = await Client(handler).UpdateAsync("Account", [
            Record(("Id", "001000000000001AAA"), ("Phone", "1")),
            Record(("Id", "001000000000002AAA"), ("Phone", "2"))
        ], Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"{OrgUrl}/services/data/v61.0/composite/sobjects", request.Url);
        var records = (JArray)JObject.Parse(request.Body)["records"]!;
        Assert.Equal(["001000000000001AAA", "001000000000002AAA"], records.Select(r => r.Value<string>("Id")));

        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        var error = Assert.Single(results[1].Errors);
        Assert.Equal("FIELD_CUSTOM_VALIDATION_EXCEPTION", error.StatusCode);
        Assert.Equal("Phone must be E.164", error.Message);
        Assert.False(error.IsTransient);
    }

    [Fact]
    public void ALockedRow_IsTransient_BecauseTheSameRecordCanSucceedWhenSentAgain() {
        Assert.True(new Salesforce.Writeback.RecordError { StatusCode = "UNABLE_TO_LOCK_ROW" }.IsTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, """[{"errorCode":"SERVER_UNAVAILABLE","message":"down"}]""")]
    [InlineData(HttpStatusCode.Forbidden, """[{"errorCode":"REQUEST_LIMIT_EXCEEDED","message":"TotalRequests Limit exceeded."}]""")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    public async Task AnOutageOrAnApiLimit_IsTransient_SoTheCallIsRetriedNotRecordedAsAFailure(HttpStatusCode status, string body) {
        var handler = new ScriptedHandler().Respond(status, body);

        await Assert.ThrowsAsync<Salesforce.Writeback.SalesforceTransientException>(() =>
            Client(handler).UpdateAsync("Account", [Record(("Id", "001000000000001AAA"), ("Phone", "1"))], Ct));
    }

    [Fact]
    public async Task ARefusedSession_SaysSo_BecauseRetryingCannotHelpUntilTheOrgConnectionIsFixed() {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.Unauthorized,
            """[{"errorCode":"INVALID_SESSION_ID","message":"Session expired or invalid"}]""");

        await Assert.ThrowsAsync<Salesforce.Writeback.SalesforceUnauthorizedException>(() =>
            Client(handler).UpdateAsync("Account", [Record(("Id", "001000000000001AAA"), ("Phone", "1"))], Ct));
    }

    [Fact]
    public async Task ARefusedCall_IsNeitherTransientNorASessionProblem() {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.BadRequest,
            """[{"errorCode":"INVALID_FIELD","message":"No such column 'Nope__c' on sobject of type Account"}]""");

        var refused = await Assert.ThrowsAsync<SalesforceRequestRefusedException>(() =>
            Client(handler).UpdateAsync("Account", [Record(("Id", "001000000000001AAA"), ("Nope__c", "1"))], Ct));
        Assert.Contains("INVALID_FIELD", refused.StatusCodes);
    }

    [Fact]
    public async Task Describe_ReadsTheFieldPropertiesWritebackDecidesOn() {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.OK, """
            {"name":"Contact","fields":[
              {"name":"FirstName","type":"string","updateable":true,"createable":true,"nillable":true,"length":40,"compoundFieldName":"Name"},
              {"name":"LastName","type":"string","updateable":true,"createable":true,"nillable":false,"defaultedOnCreate":false,"length":80,"compoundFieldName":"Name"},
              {"name":"Row_Key__c","type":"string","updateable":true,"createable":true,"nillable":true,"externalId":true,"unique":true,"length":50},
              {"name":"Score__c","type":"double","calculated":true,"precision":18,"scale":2}
            ]}
            """);

        var describe = await Client(handler).DescribeAsync("Contact", Ct);

        Assert.Equal($"{OrgUrl}/services/data/v61.0/sobjects/Contact/describe", Assert.Single(handler.Requests).Url);
        Assert.NotNull(describe);
        Assert.Equal("Name", describe.Field("FirstName")!.CompoundFieldName);
        Assert.True(describe.Field("LastName")!.IsRequiredOnCreate);
        Assert.False(describe.Field("FirstName")!.IsRequiredOnCreate);
        Assert.True(describe.Field("Row_Key__c")!.ExternalId);
        Assert.False(describe.Field("Score__c")!.IsWritable);
    }
}
