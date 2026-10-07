using Database.Models;
using SalesforceGrpc.ViewModels;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers what the Channels pages show: which Channels appear, what each row says, and what the Channel page
/// offers for the Primary Channel, its Checkpoint and its members.
/// </summary>
public class ChannelsViewTests {
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    #region The list

    [Fact]
    public void TheList_ShowsOnlyChangeDataCaptureChannels() {
        var view = ChannelsView.For(orgConnectionReady: true,
            [Channel(1, "SalesEvents__chn"), Channel(2, "Orders__chn", channelType: "event")],
            Checkpoints(), Now, notice: null);

        Assert.Equal(["SalesEvents__chn"], view.Channels.Select(c => c.FullName));
    }

    [Fact]
    public void ARow_CountsItsMembersAndThoseWithNoBinding() {
        var sales = Channel(1, "SalesEvents__chn", members: [("AccountChangeEvent", 7), ("ContactChangeEvent", null), ("LeadChangeEvent", null)]);

        var row = Assert.Single(ChannelsView.For(true, [sales], Checkpoints(), Now, null).Channels);

        Assert.Equal(3, row.MemberCount);
        Assert.Equal(2, row.UnboundCount);
    }

    [Fact]
    public void ThePrimaryChannelsRow_SaysHowOldItsCheckpointIs() {
        var sales = Channel(1, "SalesEvents__chn", isPrimary: true);

        var row = Assert.Single(ChannelsView.For(true, [sales],
            Checkpoints((1, Now.AddHours(-3))), Now, null).Channels);

        Assert.True(row.IsPrimary);
        Assert.Equal("Checkpoint 3 h ago, expires in 69 h", row.Streaming);
    }

    [Fact]
    public void ThePrimaryChannelsRow_WithoutACheckpoint_SaysWhereItStarts() {
        var sales = Channel(1, "SalesEvents__chn", isPrimary: true, startingPoint: StartingPoint.Earliest);

        var row = Assert.Single(ChannelsView.For(true, [sales], Checkpoints(), Now, null).Channels);

        Assert.Equal("No Checkpoint yet · starts at Earliest", row.Streaming);
    }

    [Fact]
    public void TheList_ReportsWhenItWasLastSynced() {
        var sales = Channel(1, "SalesEvents__chn");
        sales.LastSyncedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(sales.LastSyncedAt, ChannelsView.For(true, [sales], Checkpoints(), Now, null).LastSyncedAt);
    }

    #endregion

    #region The Channel page

    [Fact]
    public void AnEventChannel_IsNotFound() {
        Assert.Null(ChannelView.For(true, Channel(2, "Orders__chn", channelType: "event"), Bindings(), null, Now, null).Channel);
    }

    [Fact]
    public void AMember_ShowsItsBindingsTargetTableAndState_OrNothingWhenUnbound() {
        var sales = Channel(1, "SalesEvents__chn", members: [("AccountChangeEvent", 7), ("ContactChangeEvent", null)]);

        var members = ChannelView.For(true, sales,
            Bindings((7, "salesforce.account", BindingState.Inactive)), null, Now, null).Channel!.Members;

        Assert.Equal(("salesforce.account", BindingState.Inactive), (members[0].Binding!.TargetTable, members[0].Binding!.State));
        Assert.Null(members[1].Binding);
    }

    [Fact]
    public void AMember_ShowsItsEnrichedFieldsAsAList() {
        var sales = Channel(1, "SalesEvents__chn", members: [("AccountChangeEvent", null)]);
        sales.Members[0].EnrichedFields = """["Industry","Name"]""";
        sales.Members[0].FilterExpression = "Industry = 'Energy'";

        var member = Assert.Single(ChannelView.For(true, sales, Bindings(), null, Now, null).Channel!.Members);

        Assert.Equal(["Industry", "Name"], member.EnrichedFields);
        Assert.Equal("Industry = 'Energy'", member.FilterExpression);
    }

    [Fact]
    public void AChannelWithACheckpoint_SaysTheStartingPointIsNotUsed_AndCanResume() {
        var sales = Channel(1, "SalesEvents__chn");

        var checkpoint = ChannelView.For(true, sales, Bindings(), SavedAt(1, Now.AddHours(-5)), Now, null).Channel!.Checkpoint!;

        Assert.True(checkpoint.CanResume);
        Assert.Equal("5 h", checkpoint.Age);
        Assert.Equal("67 h", checkpoint.ExpiresIn);
    }

    [Fact]
    public void AnExpiredCheckpoint_CannotBeResumed() {
        var sales = Channel(1, "SalesEvents__chn");

        var checkpoint = ChannelView.For(true, sales, Bindings(), SavedAt(1, Now.AddHours(-80)), Now, null).Channel!.Checkpoint!;

        Assert.False(checkpoint.CanResume);
        Assert.True(checkpoint.Expired);
    }

    #endregion

    #region The new Channel page

    [Fact]
    public void WithNoPrimaryChannel_MakingTheNewOnePrimary_IsTheDefault() {
        Assert.True(NewChannelView.For(true, primaryChannelFullName: null).MakePrimaryByDefault);
        Assert.False(NewChannelView.For(true, primaryChannelFullName: "SalesEvents__chn").MakePrimaryByDefault);
    }

    #endregion

    #region Fixtures

    private static PlatformEventChannelEntity Channel(int id, string fullName, bool isPrimary = false,
        string channelType = "data", StartingPoint startingPoint = StartingPoint.Latest,
        (string Entity, int? BindingId)[]? members = null) => new() {
        Id = id,
        SfId = $"0YL{id:D15}",
        FullName = fullName,
        DeveloperName = fullName.Replace("__chn", ""),
        MasterLabel = fullName.Replace("__chn", " label"),
        ChannelType = channelType,
        IsPrimary = isPrimary,
        StartingPoint = startingPoint,
        Members = (members ?? []).Select((m, i) => new PlatformEventChannelMemberEntity {
            Id = id * 100 + i,
            ChannelId = id,
            SfId = $"0v8{id * 100 + i:D15}",
            FullName = $"{fullName}_{m.Entity}",
            SelectedEntity = m.Entity,
            CdcSchemaId = m.BindingId
        }).ToList()
    };

    private static IReadOnlyDictionary<int, CDCSchema> Bindings(params (int Id, string Table, BindingState State)[] bindings) =>
        bindings.ToDictionary(b => b.Id, b => new CDCSchema {
            Id = b.Id, EntityName = "", DbSchemaFullName = b.Table, BindingState = b.State
        });

    private static IReadOnlyDictionary<int, Checkpoint> Checkpoints(params (int ChannelId, DateTimeOffset SavedAt)[] checkpoints) =>
        checkpoints.ToDictionary(c => c.ChannelId, c => SavedAt(c.ChannelId, c.SavedAt));

    private static Checkpoint SavedAt(int channelId, DateTimeOffset savedAt) => new() {
        ChannelId = channelId, ReplayId = [1], SavedAt = savedAt.UtcDateTime
    };

    #endregion
}
