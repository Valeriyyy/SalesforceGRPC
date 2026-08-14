using Application.Services;
using Database.Models;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Salesforce.Clients;
using SalesforceGrpc.Salesforce;
using System.ComponentModel.DataAnnotations;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers the platform event channel logic that can be exercised without a Salesforce org: the member
/// FullName construction rules and the validation that runs before any callout is made.
/// </summary>
/// <remarks>
/// The service is built over a real <see cref="SalesforceToolingClient"/> whose HttpClient uses a handler
/// that fails the test if it is ever invoked. That makes "validation rejected this before calling
/// Salesforce" an assertion rather than an assumption.
/// </remarks>
public class PlatformEventChannelTests {

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    #region BuildMemberFullName

    [Theory]
    // Channel and entity are joined, then every double underscore is flattened to a single one.
    [InlineData("SalesEvents__chn", "AccountChangeEvent", "SalesEvents_chn_AccountChangeEvent")]
    [InlineData("Order_Channel__chn", "Order_NorthAmer__e", "Order_Channel_chn_Order_NorthAmer_e")]
    [InlineData("ChangeEvents", "AccountChangeEvent", "ChangeEvents_AccountChangeEvent")]
    [InlineData("MyChannel__chn", "MyObject__ChangeEvent", "MyChannel_chn_MyObject_ChangeEvent")]
    public void BuildMemberFullName_FlattensDoubleUnderscores(string channel, string entity, string expected) {
        Assert.Equal(expected, SalesforceToolingClient.BuildMemberFullName(channel, entity));
    }

    [Fact]
    public void BuildMemberFullName_NeverLeavesConsecutiveUnderscores() {
        var result = SalesforceToolingClient.BuildMemberFullName("ns__Deep____Name__chn", "Some__Entity__e");
        Assert.DoesNotContain("__", result);
    }

    [Theory]
    [InlineData("", "AccountChangeEvent")]
    [InlineData("  ", "AccountChangeEvent")]
    [InlineData("SalesEvents__chn", "")]
    public void BuildMemberFullName_RejectsBlankInput(string channel, string entity) {
        Assert.Throws<ArgumentException>(() => SalesforceToolingClient.BuildMemberFullName(channel, entity));
    }

    #endregion

    #region Channel name validation

    [Theory]
    [InlineData("SalesEvents")]              // missing the __chn suffix
    [InlineData("")]
    [InlineData("__chn")]                    // no name before the suffix
    [InlineData("9Sales__chn")]              // must start with a letter
    [InlineData("Sales_-Events__chn")]       // invalid character
    [InlineData("Sales__Events__chn")]       // consecutive underscores in the name
    [InlineData("SalesEvents___chn")]        // trailing underscore before the suffix
    public async Task CreateChannel_RejectsInvalidFullName(string fullName) {
        var harness = new Harness();

        await Assert.ThrowsAsync<ValidationException>(() => harness.Service.CreateChannelAsync(
            new CreateChannelDTO { FullName = fullName, Label = "Test", ChannelType = "data" }, Ct));

        Assert.False(harness.CalledSalesforce);
    }

    [Fact]
    public async Task CreateChannel_RejectsMissingLabel() {
        var harness = new Harness();

        await Assert.ThrowsAsync<ValidationException>(() => harness.Service.CreateChannelAsync(
            new CreateChannelDTO { FullName = "SalesEvents__chn", Label = "  ", ChannelType = "data" }, Ct));

        Assert.False(harness.CalledSalesforce);
    }

    [Theory]
    [InlineData("cdc")]
    [InlineData("")]
    [InlineData("Change Data Capture")]
    public async Task CreateChannel_RejectsUnknownChannelType(string channelType) {
        var harness = new Harness();

        await Assert.ThrowsAsync<ValidationException>(() => harness.Service.CreateChannelAsync(
            new CreateChannelDTO { FullName = "SalesEvents__chn", Label = "Test", ChannelType = channelType }, Ct));

        Assert.False(harness.CalledSalesforce);
    }

    [Fact]
    public async Task CreateChannel_RejectsUnknownEventType() {
        var harness = new Harness();

        await Assert.ThrowsAsync<ValidationException>(() => harness.Service.CreateChannelAsync(
            new CreateChannelDTO {
                FullName = "SalesEvents__chn", Label = "Test", ChannelType = "data", EventType = "realtime"
            }, Ct));

        Assert.False(harness.CalledSalesforce);
    }

    #endregion

    #region Immutable fields

    [Fact]
    public async Task UpdateChannel_RejectsChangedChannelType() {
        var harness = new Harness();
        harness.WithChannel(ChannelFixture("data"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => harness.Service.UpdateChannelAsync(
            1, new UpdateChannelDTO { Label = "New label", ChannelType = "event" }, Ct));

        Assert.Contains("cannot be changed", ex.Message);
        Assert.False(harness.CalledSalesforce);
    }

    [Fact]
    public async Task UpdateChannel_AllowsMatchingChannelType() {
        var harness = new Harness();
        harness.WithChannel(ChannelFixture("data"));

        // Resending the unchanged value is legal, so this gets far enough to attempt the callout.
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Service.UpdateChannelAsync(
            1, new UpdateChannelDTO { Label = "New label", ChannelType = "data" }, Ct));

        Assert.True(harness.CalledSalesforce);
    }

    [Fact]
    public async Task UpdateChannelMember_RejectsChangedSelectedEntity() {
        var harness = new Harness();
        harness.WithChannel(ChannelFixture("data"));
        harness.WithMember(MemberFixture("AccountChangeEvent"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => harness.Service.UpdateChannelMemberAsync(
            1, new UpdateChannelMemberDTO { SelectedEntity = "ContactChangeEvent" }, Ct));

        Assert.Contains("cannot be changed", ex.Message);
        Assert.False(harness.CalledSalesforce);
    }

    #endregion

    #region Event product mismatch

    [Fact]
    public async Task AddChannelMember_RejectsPlatformEventOnDataChannel() {
        var harness = new Harness();
        harness.WithChannel(ChannelFixture("data"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => harness.Service.AddChannelMemberAsync(
            1, new CreateChannelMemberDTO { SelectedEntity = "Order_Event__e" }, Ct));

        Assert.Contains("not a Change Data Capture entity", ex.Message);
        Assert.False(harness.CalledSalesforce);
    }

    [Fact]
    public async Task AddChannelMember_RejectsChangeEventOnEventChannel() {
        var harness = new Harness();
        harness.WithChannel(ChannelFixture("event"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => harness.Service.AddChannelMemberAsync(
            1, new CreateChannelMemberDTO { SelectedEntity = "AccountChangeEvent" }, Ct));

        Assert.Contains("is a Change Data Capture entity", ex.Message);
        Assert.False(harness.CalledSalesforce);
    }

    [Fact]
    public async Task AddChannelMember_RejectsMissingEntity() {
        var harness = new Harness();
        harness.WithChannel(ChannelFixture("data"));

        await Assert.ThrowsAsync<ValidationException>(() => harness.Service.AddChannelMemberAsync(
            1, new CreateChannelMemberDTO { SelectedEntity = "" }, Ct));

        Assert.False(harness.CalledSalesforce);
    }

    [Fact]
    public async Task AddChannelMember_AcceptsChangeEventOnDataChannel() {
        var harness = new Harness();
        harness.WithChannel(ChannelFixture("data"));

        // A valid pairing passes validation, so the failure comes from the blocked HTTP call instead.
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Service.AddChannelMemberAsync(
            1, new CreateChannelMemberDTO { SelectedEntity = "AccountChangeEvent" }, Ct));

        Assert.True(harness.CalledSalesforce);
    }

    #endregion

    #region Unknown IDs

    [Fact]
    public async Task GetChannelMembers_ThrowsForUnknownChannel() {
        var harness = new Harness();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => harness.Service.GetChannelMembersAsync(99, Ct));
    }

    [Fact]
    public async Task RemoveChannelMember_ThrowsForUnknownMember() {
        var harness = new Harness();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => harness.Service.RemoveChannelMemberAsync(99, Ct));
        Assert.False(harness.CalledSalesforce);
    }

    #endregion

    private static PlatformEventChannelEntity ChannelFixture(string channelType) => new() {
        Id = 1,
        SfId = "0YLRM0000004CEI4A2",
        FullName = "SalesEvents__chn",
        DeveloperName = "SalesEvents",
        MasterLabel = "Sales Events",
        ChannelType = channelType
    };

    private static PlatformEventChannelMemberEntity MemberFixture(string selectedEntity) => new() {
        Id = 1,
        ChannelId = 1,
        SfId = "0v8RM0000000N6uYAE",
        FullName = "SalesEvents_chn_AccountChangeEvent",
        SelectedEntity = selectedEntity
    };

    /// <summary>
    /// Builds a PlatformEventService whose Salesforce calls are blocked, so tests can prove that
    /// validation rejected a request before any callout was attempted.
    /// </summary>
    private sealed class Harness {
        public IPlatformEventChannelRepository Repo { get; }
        public PlatformEventService Service { get; }
        public bool CalledSalesforce => _handler.WasCalled;

        private readonly BlockingHandler _handler = new();

        public Harness() {
            Repo = Substitute.For<IPlatformEventChannelRepository>();

            var config = Options.Create(new SalesforceConfig {
                OrgUrl = "https://example.my.salesforce.com",
                ApiVersion = "61.0"
            });

            var toolingClient = new SalesforceToolingClient(
                new HttpClient(_handler),
                Substitute.For<ISalesforceTokenProvider>(),
                config,
                NullLogger<SalesforceToolingClient>.Instance);

            Service = new PlatformEventService(toolingClient, Repo, NullLogger<PlatformEventService>.Instance);
        }

        public void WithChannel(PlatformEventChannelEntity channel) =>
            Repo.GetChannelByIdAsync(channel.Id, Arg.Any<CancellationToken>()).Returns(channel);

        public void WithMember(PlatformEventChannelMemberEntity member) =>
            Repo.GetMemberByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
    }

    /// <summary>
    /// Records that a request was attempted and then fails it, so no test can reach a real org.
    /// </summary>
    private sealed class BlockingHandler : HttpMessageHandler {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            WasCalled = true;
            throw new HttpRequestException("Salesforce calls are blocked in unit tests.");
        }
    }
}
