using Application.Bindings;
using Application.Services;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NSubstitute;
using Salesforce;
using Salesforce.Auth;
using Salesforce.Clients;
using System.Net;
using System.Text;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers what managing Channels and Channel Members does beyond Salesforce itself: the Binding State it leaves
/// behind, the Bindings it re-links, and when it tells the worker to re-plan.
/// </summary>
/// <remarks>
/// The service runs over a real <see cref="SalesforceToolingClient"/> whose HttpClient talks to
/// <see cref="FakeTooling"/>, a scripted stand-in for the Tooling API, so every call the service makes travels
/// the same path it would against an org.
/// </remarks>
public class ChannelManagementTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    #region Creating a Channel

    [Fact]
    public async Task CreatingAChannel_StoresItsStartingPoint_AndCanMakeItThePrimaryChannel() {
        var harness = new Harness();
        var created = Channel(5, "SalesEvents__chn");
        harness.ScriptChannelCreate(created, alreadyInSalesforce: false);

        var result = await harness.Service.CreateDataChannelAsync(new DTO.NewChannelDTO {
            FullName = "SalesEvents__chn", Label = "Sales Events", StartingPoint = "Earliest", MakePrimary = true
        }, Ct);

        Assert.False(result.Adopted);
        var mirrored = Assert.Single(harness.Mirror.Channels);
        Assert.Equal(result.ChannelId, mirrored.Id);
        Assert.Equal("data", mirrored.ChannelType);
        Assert.Equal(StartingPoint.Earliest, mirrored.StartingPoint);
        Assert.True(mirrored.IsPrimary);
        harness.Signal.Received().Signal();
    }

    [Fact]
    public async Task CreatingAChannelSalesforceAlreadyHas_AdoptsItWithItsMembers() {
        var harness = new Harness();
        var existing = Channel(5, "SalesEvents__chn");
        harness.ScriptChannelCreate(existing, alreadyInSalesforce: true);
        harness.ScriptOrg((existing, ["AccountChangeEvent", "ContactChangeEvent"]));

        var result = await harness.Service.CreateDataChannelAsync(new DTO.NewChannelDTO {
            FullName = "SalesEvents__chn", Label = "Sales Events"
        }, Ct);

        Assert.True(result.Adopted);
        Assert.Equal(2, result.MemberCount);
        Assert.False(Assert.Single(harness.Mirror.Channels).IsPrimary);
        harness.Signal.DidNotReceive().Signal();
    }

    [Fact]
    public async Task CreatingAChannelThatIsAlreadyHere_IsRefused_SoItsCheckpointIsNeverSilentlyResumed() {
        var harness = new Harness();
        harness.WithChannel(Channel(5, "SalesEvents__chn"));

        var ex = await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            harness.Service.CreateDataChannelAsync(new DTO.NewChannelDTO {
                FullName = "SalesEvents__chn", Label = "Sales Events", MakePrimary = true
            }, Ct));

        Assert.Contains("already", ex.Message);
        Assert.Empty(harness.Tooling.Requests);
        Assert.False(harness.Mirror.Channels.Single().IsPrimary);
    }

    #endregion

    #region Removing a Channel Member

    [Fact]
    public async Task RemovingAMemberOfThePrimaryChannel_SetsItsBindingInactive_AndSignalsTheWorker() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        var member = harness.WithMember(Member(10, channel, "AccountChangeEvent", bindingId: 7));
        harness.WithBinding(Binding(7, "AccountChangeEvent", BindingState.Active));

        await harness.Service.RemoveChannelMemberAsync(member.Id, Ct);

        await harness.Meta.Received(1).SetBindingState(7, BindingState.Inactive);
        harness.Signal.Received().Signal();
    }

    [Fact]
    public async Task RemovingAMemberOfAnotherChannel_ChangesNoBinding() {
        var harness = new Harness();
        harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        var other = harness.WithChannel(Channel(2, "Archive__chn"));
        var member = harness.WithMember(Member(20, other, "AccountChangeEvent", bindingId: 7));
        harness.WithBinding(Binding(7, "AccountChangeEvent", BindingState.Active));

        await harness.Service.RemoveChannelMemberAsync(member.Id, Ct);

        await harness.Meta.DidNotReceiveWithAnyArgs().SetBindingState(default, default);
        harness.Signal.DidNotReceive().Signal();
    }

    #endregion

    #region Adding a Channel Member

    [Fact]
    public async Task AddingAnEntityThatAlreadyHasABinding_LinksTheNewMemberToIt() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn"));
        harness.WithBinding(Binding(7, "AccountChangeEvent", BindingState.Inactive));
        harness.ScriptMemberCreate(channel, "AccountChangeEvent", sfId: "0v8NEW0000000001");

        var added = await harness.Service.AddChannelMemberAsync(channel.Id,
            new DTO.CreateChannelMemberDTO { SelectedEntity = "AccountChangeEvent" }, Ct);

        Assert.Equal(7, Assert.Single(harness.Mirror.Members, m => m.Id == added.Id).CdcSchemaId);
    }

    [Fact]
    public async Task AddingAMemberToThePrimaryChannel_SignalsTheWorker() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        harness.ScriptMemberCreate(channel, "AccountChangeEvent", sfId: "0v8NEW0000000001");

        await harness.Service.AddChannelMemberAsync(channel.Id,
            new DTO.CreateChannelMemberDTO { SelectedEntity = "AccountChangeEvent" }, Ct);

        harness.Signal.Received().Signal();
    }

    #endregion

    #region Adding several Entities at once

    [Fact]
    public async Task AddingSeveralEntities_SendsOneAllOrNothingRequest_AndMirrorsEveryMember() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        harness.ScriptCompositeCreate(("0v8NEW0000000001", null), ("0v8NEW0000000002", null));
        harness.ScriptMemberRead(channel, "AccountChangeEvent", "0v8NEW0000000001");
        harness.ScriptMemberRead(channel, "ContactChangeEvent", "0v8NEW0000000002");

        var result = await harness.Service.AddChannelMembersAsync(channel.Id, Batch("AccountChangeEvent", "ContactChangeEvent"), Ct);

        Assert.True(result.Added);
        Assert.Equal(["Added", "Added"], result.Outcomes.Select(o => o.Status));
        var composite = Assert.Single(harness.Tooling.Requests, r => r.Path.EndsWith("/tooling/composite"));
        Assert.Contains("\"allOrNone\":true", composite.Body);
        Assert.Equal(["AccountChangeEvent", "ContactChangeEvent"], harness.Mirror.Members.Select(m => m.SelectedEntity));
        harness.Signal.Received(1).Signal();
    }

    [Fact]
    public async Task AddingSeveralEntities_WhenOneIsRejected_AddsNone_AndNamesTheOneThatFailed() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        harness.ScriptCompositeCreate((null, "PROCESSING_HALTED"), (null, "FIELD_INTEGRITY_EXCEPTION"));

        var result = await harness.Service.AddChannelMembersAsync(channel.Id, Batch("AccountChangeEvent", "NopeChangeEvent"), Ct);

        Assert.False(result.Added);
        Assert.Equal(["NotAdded", "Failed"], result.Outcomes.Select(o => o.Status));
        Assert.Contains("FIELD_INTEGRITY_EXCEPTION", result.Outcomes[1].Message);
        Assert.Empty(harness.Mirror.Members);
        harness.Signal.DidNotReceive().Signal();
    }

    [Fact]
    public async Task AddingSeveralEntities_WhenSalesforceDidNotRollBack_RemovesTheOnesItCreated() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn"));
        harness.ScriptCompositeCreate(("0v8NEW0000000001", null), (null, "FIELD_INTEGRITY_EXCEPTION"));

        var result = await harness.Service.AddChannelMembersAsync(channel.Id, Batch("AccountChangeEvent", "NopeChangeEvent"), Ct);

        Assert.False(result.Added);
        Assert.Contains(harness.Tooling.Requests, r =>
            r.Method == HttpMethod.Delete && r.Path.EndsWith("sobjects/PlatformEventChannelMember/0v8NEW0000000001"));
        Assert.Empty(result.LeftInSalesforce);
    }

    [Fact]
    public async Task AddingAnEntityTheChannelAlreadyCarries_IsRejectedBeforeCallingSalesforce() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn"));
        harness.WithMember(Member(10, channel, "AccountChangeEvent"));

        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            harness.Service.AddChannelMembersAsync(channel.Id, Batch("ContactChangeEvent", "AccountChangeEvent"), Ct));

        Assert.Empty(harness.Tooling.Requests);
    }

    private static DTO.AddChannelMembersDTO Batch(params string[] entities) => new() {
        Members = entities.Select(e => new DTO.CreateChannelMemberDTO { SelectedEntity = e }).ToList()
    };

    #endregion

    #region Deleting a Channel

    [Fact]
    public async Task DeletingThePrimaryChannel_LeavesBindingsAlone_AndSignalsTheWorker() {
        var harness = new Harness();
        var channel = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        harness.WithMember(Member(10, channel, "AccountChangeEvent", bindingId: 7));
        harness.WithBinding(Binding(7, "AccountChangeEvent", BindingState.Active));

        await harness.Service.DeleteChannelAsync(channel.Id, Ct);

        Assert.Empty(harness.Mirror.Channels);
        await harness.Meta.DidNotReceiveWithAnyArgs().SetBindingState(default, default);
        harness.Signal.Received().Signal();
    }

    [Fact]
    public async Task DeletingAnotherChannel_DoesNotSignalTheWorker() {
        var harness = new Harness();
        harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        var other = harness.WithChannel(Channel(2, "Archive__chn"));

        await harness.Service.DeleteChannelAsync(other.Id, Ct);

        harness.Signal.DidNotReceive().Signal();
    }

    #endregion

    #region Resync

    [Fact]
    public async Task Resync_ReportsWhatSalesforceChanged_AndAppliesItToBindings() {
        var harness = new Harness();
        var sales = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        var old = harness.WithChannel(Channel(2, "Old__chn"));
        harness.WithMember(Member(10, sales, "AccountChangeEvent", bindingId: 7));
        harness.WithMember(Member(11, sales, "ContactChangeEvent"));
        harness.WithBinding(Binding(7, "AccountChangeEvent", BindingState.Active));
        harness.WithBinding(Binding(9, "LeadChangeEvent", BindingState.Inactive));
        var archive = Channel(3, "Archive__chn");
        harness.ScriptOrg((sales, ["ContactChangeEvent", "LeadChangeEvent"]), (archive, []));

        var report = await harness.Service.ResyncFromSalesforceAsync(Ct);

        Assert.Equal(["Archive__chn"], report.ChannelsAdded);
        Assert.Equal(["Old__chn"], report.ChannelsRemoved);
        var added = Assert.Single(report.MembersAdded);
        Assert.Equal(("SalesEvents__chn", "LeadChangeEvent"), (added.Channel, added.SelectedEntity));
        var removed = Assert.Single(report.MembersRemoved);
        Assert.Equal(("SalesEvents__chn", "AccountChangeEvent", "salesforce.account"),
            (removed.Channel, removed.SelectedEntity, removed.BindingSetInactive));
        Assert.Null(report.PrimaryChannelRemoved);

        await harness.Meta.Received(1).SetBindingState(7, BindingState.Inactive);
        Assert.Equal(9, Assert.Single(harness.Mirror.Members, m => m.SelectedEntity == "LeadChangeEvent").CdcSchemaId);
        harness.Signal.Received().Signal();
    }

    [Fact]
    public async Task Resync_WhenThePrimaryChannelIsGone_SaysSo_AndLeavesBindingsAlone() {
        var harness = new Harness();
        var sales = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        harness.WithMember(Member(10, sales, "AccountChangeEvent", bindingId: 7));
        harness.WithBinding(Binding(7, "AccountChangeEvent", BindingState.Active));
        harness.ScriptOrg();

        var report = await harness.Service.ResyncFromSalesforceAsync(Ct);

        Assert.Equal("SalesEvents__chn", report.PrimaryChannelRemoved);
        Assert.Empty(report.MembersRemoved);
        await harness.Meta.DidNotReceiveWithAnyArgs().SetBindingState(default, default);
        harness.Signal.Received().Signal();
    }

    [Fact]
    public async Task Resync_WhenNothingChanged_ReportsNothing_AndDoesNotSignal() {
        var harness = new Harness();
        var sales = harness.WithChannel(Channel(1, "SalesEvents__chn", isPrimary: true));
        harness.WithMember(Member(10, sales, "AccountChangeEvent"));
        harness.ScriptOrg((sales, ["AccountChangeEvent"]));

        var report = await harness.Service.ResyncFromSalesforceAsync(Ct);

        Assert.False(report.HasChanges);
        harness.Signal.DidNotReceive().Signal();
    }

    #endregion

    #region Fixtures

    private static PlatformEventChannelEntity Channel(int id, string fullName, bool isPrimary = false,
        string channelType = "data") => new() {
        Id = id,
        SfId = $"0YL00000000000{id:D4}",
        FullName = fullName,
        DeveloperName = fullName.Replace("__chn", ""),
        MasterLabel = fullName,
        ChannelType = channelType,
        IsPrimary = isPrimary
    };

    private static PlatformEventChannelMemberEntity Member(int id, PlatformEventChannelEntity channel, string entity,
        int? bindingId = null) => new() {
        Id = id,
        ChannelId = channel.Id,
        SfId = $"0v800000000000{id:D4}",
        FullName = SalesforceToolingClient.BuildMemberFullName(channel.FullName, entity),
        SelectedEntity = entity,
        CdcSchemaId = bindingId
    };

    private static CDCSchema Binding(int id, string entity, BindingState state) => new() {
        Id = id,
        EntityName = entity,
        DbSchemaFullName = $"salesforce.{entity.Replace("ChangeEvent", "").ToLowerInvariant()}",
        BindingState = state
    };

    /// <summary>A member as the Tooling API's retrieve endpoint returns it, readable names in Metadata.</summary>
    private static Salesforce.Dtos.PlatformEventChannelMember SalesforceMember(PlatformEventChannelEntity channel,
        string entity, string sfId) => new() {
        Id = sfId,
        FullName = SalesforceToolingClient.BuildMemberFullName(channel.FullName, entity),
        DeveloperName = SalesforceToolingClient.BuildMemberFullName(channel.FullName, entity),
        Metadata = new Salesforce.Dtos.PlatformEventChannelMemberMetadata {
            EventChannel = channel.FullName,
            SelectedEntity = entity
        }
    };

    #endregion

    /// <summary>
    /// The real service over an in-memory Mirror, a substituted Binding store and worker signal, and a scripted
    /// Tooling API.
    /// </summary>
    private sealed class Harness {
        public InMemoryChannelMirror Mirror { get; } = new();
        public IMetaRepository Meta { get; } = Substitute.For<IMetaRepository>();
        public IConfigurationChangeSignal Signal { get; } = Substitute.For<IConfigurationChangeSignal>();
        public FakeTooling Tooling { get; } = new();
        public PlatformEventService Service { get; }

        public Harness() {
            var connections = Substitute.For<IOrgConnectionSource>();
            connections.GetOrgUrlAsync(Arg.Any<CancellationToken>()).Returns("https://example.my.salesforce.com");

            var toolingClient = new SalesforceToolingClient(new HttpClient(Tooling), connections,
                Options.Create(new SalesforceConfig { ApiVersion = "61.0" }),
                NullLogger<SalesforceToolingClient>.Instance);

            Service = new PlatformEventService(toolingClient, Mirror, Meta, Signal,
                NullLogger<PlatformEventService>.Instance);
        }

        public PlatformEventChannelEntity WithChannel(PlatformEventChannelEntity channel) => Mirror.Add(channel);

        public PlatformEventChannelMemberEntity WithMember(PlatformEventChannelMemberEntity member) => Mirror.Add(member);

        public CDCSchema WithBinding(CDCSchema binding) {
            Meta.GetSchemaByEntityName(binding.EntityName).Returns(binding);
            Meta.GetSchemaById(binding.Id).Returns(binding);
            return binding;
        }

        /// <summary>
        /// Scripts Salesforce accepting a new member for an Entity, and the read-back that follows it.
        /// </summary>
        public void ScriptMemberCreate(PlatformEventChannelEntity channel, string entity, string sfId) {
            ScriptMemberRead(channel, entity, sfId);
            Tooling.On(HttpMethod.Post, "sobjects/PlatformEventChannelMember", HttpStatusCode.Created,
                new { id = sfId, success = true, errors = Array.Empty<object>() });
        }

        public void ScriptMemberRead(PlatformEventChannelEntity channel, string entity, string sfId) =>
            Tooling.On(HttpMethod.Get, $"sobjects/PlatformEventChannelMember/{sfId}", HttpStatusCode.OK,
                SalesforceMember(channel, entity, sfId));

        /// <summary>
        /// Scripts the composite create's answer, one entry per subrequest: an id for a member Salesforce
        /// created, or an error code for one it refused.
        /// </summary>
        public void ScriptCompositeCreate(params (string? SfId, string? ErrorCode)[] results) =>
            Tooling.On(HttpMethod.Post, "tooling/composite", HttpStatusCode.OK, new {
                compositeResponse = results.Select((r, i) => r.SfId is not null
                    ? (object)new {
                        referenceId = $"member{i}", httpStatusCode = 201,
                        body = new { id = r.SfId, success = true, errors = Array.Empty<object>() }
                    }
                    : new {
                        referenceId = $"member{i}", httpStatusCode = 400,
                        body = new[] { new { errorCode = r.ErrorCode, message = $"{r.ErrorCode}: refused" } }
                    }).ToArray()
            });

        /// <summary>
        /// Scripts the duplicate check a create starts with, and either Salesforce creating the channel or
        /// finding it already there.
        /// </summary>
        public void ScriptChannelCreate(PlatformEventChannelEntity channel, bool alreadyInSalesforce) {
            Tooling.On(HttpMethod.Get, $"WHERE DeveloperName = '{channel.DeveloperName}'", HttpStatusCode.OK, new {
                totalSize = alreadyInSalesforce ? 1 : 0, done = true,
                records = alreadyInSalesforce ? new[] { new { Id = channel.SfId } } : []
            });
            Tooling.On(HttpMethod.Post, "sobjects/PlatformEventChannel", HttpStatusCode.Created,
                new { id = channel.SfId, success = true, errors = Array.Empty<object>() });
            Tooling.On(HttpMethod.Get, $"sobjects/PlatformEventChannel/{channel.SfId}", HttpStatusCode.OK, new {
                Id = channel.SfId, channel.FullName, channel.DeveloperName, channel.MasterLabel, channel.ChannelType
            });
        }

        /// <summary>
        /// Scripts what Salesforce holds for a Resync: the channels, and for each the Entities it carries.
        /// </summary>
        public void ScriptOrg(params (PlatformEventChannelEntity Channel, string[] Entities)[] org) {
            Tooling.On(HttpMethod.Get, "FROM PlatformEventChannel ORDER BY DeveloperName", HttpStatusCode.OK, new {
                totalSize = org.Length, done = true,
                records = org.Select(o => new { Id = o.Channel.SfId }).ToArray()
            });

            var memberIndex = 0;
            foreach (var (channel, entities) in org) {
                Tooling.On(HttpMethod.Get, $"sobjects/PlatformEventChannel/{channel.SfId}", HttpStatusCode.OK, new {
                    Id = channel.SfId, channel.FullName, channel.DeveloperName, channel.MasterLabel, channel.ChannelType
                });

                var sfIds = entities.Select(e =>
                    Mirror.Members.FirstOrDefault(m => m.ChannelId == channel.Id && m.SelectedEntity == e)?.SfId
                    ?? $"0v8ORG{memberIndex++:D10}").ToList();
                Tooling.On(HttpMethod.Get, $"WHERE EventChannel = '{channel.SfId}'", HttpStatusCode.OK, new {
                    totalSize = sfIds.Count, done = true, records = sfIds.Select(id => new { Id = id }).ToArray()
                });
                for (var i = 0; i < entities.Length; i++) {
                    ScriptMemberRead(channel, entities[i], sfIds[i]);
                }
            }
        }
    }

    /// <summary>
    /// A scripted Tooling API: answers each request from the last route whose fragment its path contains, records every request, and
    /// fails any request nothing was scripted for, so a test cannot silently depend on an unplanned call.
    /// </summary>
    internal sealed class FakeTooling : HttpMessageHandler {
        private readonly List<(HttpMethod Method, string PathSuffix, Func<string?, HttpResponseMessage> Respond)> _routes = [];

        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        public FakeTooling On(HttpMethod method, string pathSuffix, Func<string?, HttpResponseMessage> respond) {
            _routes.Add((method, pathSuffix, respond));
            return this;
        }

        public FakeTooling On(HttpMethod method, string pathSuffix, HttpStatusCode status, object? body = null) =>
            On(method, pathSuffix, _ => Json(status, body));

        public static HttpResponseMessage Json(HttpStatusCode status, object? body) => new(status) {
            Content = new StringContent(body is null ? "" : JsonConvert.SerializeObject(body), Encoding.UTF8,
                "application/json")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) {
            var path = System.Web.HttpUtility.UrlDecode(request.RequestUri!.PathAndQuery);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, path, body));

            // Deletes are scripted as succeeding unless a test says otherwise.
            var route = _routes.LastOrDefault(r => r.Method == request.Method && path.Contains(r.PathSuffix, StringComparison.Ordinal));
            if (route.Respond is not null) {
                return route.Respond(body);
            }
            if (request.Method == HttpMethod.Delete) {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException($"No scripted Tooling response for {request.Method} {path}");
        }
    }
}
