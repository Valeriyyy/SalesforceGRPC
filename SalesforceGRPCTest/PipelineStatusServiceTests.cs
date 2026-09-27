using Application.Pipeline;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace SalesforceGRPCTest;

/// <summary>
/// The Stage Status rules, driven through <see cref="IPipelineStatusService"/> — the one seam the UI shell is
/// tested at.
/// </summary>
/// <remarks>
/// Every store is substituted and answers as a fresh install unless a test says otherwise. What is asserted is
/// each Stage's status, what it waits on, why, and the facts it reports — never how the service read them.
/// </remarks>
public class PipelineStatusServiceTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly IOrgConnectionRepository _org = Substitute.For<IOrgConnectionRepository>();
    private readonly IPlatformEventChannelRepository _channels = Substitute.For<IPlatformEventChannelRepository>();
    private readonly ICheckpointRepository _checkpoints = Substitute.For<ICheckpointRepository>();
    private readonly ITargetConnectionRepository _target = Substitute.For<ITargetConnectionRepository>();
    private readonly IMetaRepository _meta = Substitute.For<IMetaRepository>();
    private readonly FakeTimeProvider _time = new(Now);

    public PipelineStatusServiceTests() {
        _meta.CountBindingsByStateAsync(Arg.Any<CancellationToken>()).Returns(new BindingStateCounts());
    }

    private Task<PipelineStatus> Status() =>
        new PipelineStatusService(_org, _channels, _checkpoints, _target, _meta, _time).GetAsync(Ct);

    #region Arrange

    private void WithOrg(ConnectionState state) =>
        _org.GetAsync(Arg.Any<CancellationToken>()).Returns(new OrgConnection {
            Id = 1, ConsumerKey = "key", AdministeringUsername = "admin@acme.com", RunAsUsername = "integration@acme.com",
            SigningPrivateKey = "enc", SigningCertificate = "cert", CertificateFingerprint = "AA:BB",
            ConnectionState = state,
            OrgUrl = state == ConnectionState.Connected ? "https://acme.my.salesforce.com" : null,
            OrgId = state == ConnectionState.Connected ? "00D000000000001" : null,
            LastConnectedAt = state == ConnectionState.Connected ? Now.UtcDateTime.AddHours(-1) : null
        });

    private void WithPrimaryChannel(int members = 2) =>
        _channels.GetPrimaryChannelAsync(Arg.Any<CancellationToken>()).Returns(new PlatformEventChannelEntity {
            Id = 7, SfId = "0YL000000000001", FullName = "SyncChannel__chn", DeveloperName = "SyncChannel",
            ChannelType = "data", IsPrimary = true, StartingPoint = StartingPoint.Earliest,
            Members = Enumerable.Range(1, members).Select(i => new PlatformEventChannelMemberEntity {
                Id = i, ChannelId = 7, SfId = $"0v8{i}", FullName = $"SyncChannel_chn_Entity{i}", SelectedEntity = $"Entity{i}"
            }).ToList()
        });

    private void WithCheckpointAged(TimeSpan age) =>
        _checkpoints.GetAsync(7, Arg.Any<CancellationToken>()).Returns(new Checkpoint {
            ChannelId = 7, ReplayId = [1, 2, 3], SavedAt = (Now - age).UtcDateTime
        });

    private void WithTarget(ConnectionState state) =>
        _target.GetAsync(Arg.Any<CancellationToken>()).Returns(new TargetConnection {
            Id = 1, Engine = TargetDatabaseEngine.Postgres, Host = "db.internal", Port = 5432, DatabaseName = "warehouse",
            ConnectionState = state, LastConnectedAt = state == ConnectionState.Connected ? Now.UtcDateTime.AddDays(-2) : null
        });

    private void WithBindings(int active = 0, int incomplete = 0, int inactive = 0, int forced = 0) =>
        _meta.CountBindingsByStateAsync(Arg.Any<CancellationToken>()).Returns(new BindingStateCounts {
            Active = active, Incomplete = incomplete, Inactive = inactive, ForcedIncomplete = forced
        });

    private void WithEverythingOk() {
        WithOrg(ConnectionState.Connected);
        WithPrimaryChannel();
        WithCheckpointAged(TimeSpan.FromMinutes(5));
        WithTarget(ConnectionState.Connected);
        WithBindings(active: 3, inactive: 1);
    }

    #endregion

    [Fact]
    public async Task Stages_AreReportedInSetupOrder() {
        var status = await Status();

        Assert.Equal(
            [PipelineStage.OrgConnection, PipelineStage.PrimaryChannel, PipelineStage.TargetConnection, PipelineStage.Bindings],
            status.Stages.Select(s => s.Stage));
    }

    [Fact]
    public async Task FreshInstall_OrgConnectionIsToDo_AndEveryLaterStageWaits() {
        var status = await Status();

        Assert.Equal(StageStatus.ToDo, status.OrgConnection.Status);
        Assert.Equal([StageReason.NoOrgConnection], status.OrgConnection.Reasons);

        Assert.Equal(StageStatus.Waiting, status.PrimaryChannel.Status);
        Assert.Equal([PipelineStage.OrgConnection], status.PrimaryChannel.WaitingOn);
        Assert.Equal(StageStatus.Waiting, status.TargetConnection.Status);
        Assert.Equal([PipelineStage.OrgConnection], status.TargetConnection.WaitingOn);
        Assert.Equal(StageStatus.Waiting, status.Bindings.Status);
        Assert.Equal([PipelineStage.PrimaryChannel, PipelineStage.TargetConnection], status.Bindings.WaitingOn);

        Assert.Equal(PipelineStage.OrgConnection, status.NextStep);
        Assert.False(status.AllOk);
    }

    [Fact]
    public async Task OrgConnectionStartedButNeverConnected_IsToDo_NotNeedsAttention() {
        WithOrg(ConnectionState.Incomplete);

        var status = await Status();

        Assert.Equal(StageStatus.ToDo, status.OrgConnection.Status);
        Assert.Equal([StageReason.OrgConnectionIncomplete], status.OrgConnection.Reasons);
        Assert.Equal("integration@acme.com", status.OrgConnection.RunAsUsername);
    }

    [Fact]
    public async Task FailedOrgConnection_NeedsAttention() {
        WithOrg(ConnectionState.Failed);

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.OrgConnection.Status);
        Assert.Equal([StageReason.OrgConnectionFailed], status.OrgConnection.Reasons);
    }

    [Fact]
    public async Task ConnectedOrgConnection_IsOk_AndReportsWhoAndWhere() {
        WithOrg(ConnectionState.Connected);

        var status = await Status();

        Assert.Equal(StageStatus.Ok, status.OrgConnection.Status);
        Assert.Empty(status.OrgConnection.Reasons);
        Assert.Equal("integration@acme.com", status.OrgConnection.RunAsUsername);
        Assert.Equal("https://acme.my.salesforce.com", status.OrgConnection.OrgUrl);
        Assert.Equal(Now.UtcDateTime.AddHours(-1), status.OrgConnection.LastConnectedAt);
    }

    [Fact]
    public async Task OnceTheOrgIsConnected_PrimaryChannelAndTargetConnectionAreBothToDo() {
        WithOrg(ConnectionState.Connected);

        var status = await Status();

        Assert.Equal(StageStatus.ToDo, status.PrimaryChannel.Status);
        Assert.Equal([StageReason.NoPrimaryChannel], status.PrimaryChannel.Reasons);
        Assert.Empty(status.PrimaryChannel.WaitingOn);
        Assert.Equal(StageStatus.ToDo, status.TargetConnection.Status);
        Assert.Equal([StageReason.NoTargetConnection], status.TargetConnection.Reasons);
        Assert.Equal(PipelineStage.PrimaryChannel, status.NextStep);
    }

    [Fact]
    public async Task PrimaryChannelWithMembersAndNoCheckpoint_IsOk_AndReportsItsStartingPoint() {
        WithOrg(ConnectionState.Connected);
        WithPrimaryChannel(members: 3);

        var status = await Status();

        Assert.Equal(StageStatus.Ok, status.PrimaryChannel.Status);
        Assert.Equal("SyncChannel__chn", status.PrimaryChannel.ChannelFullName);
        Assert.Equal(3, status.PrimaryChannel.MemberCount);
        Assert.Null(status.PrimaryChannel.CheckpointSavedAt);
        Assert.Equal(StartingPoint.Earliest, status.PrimaryChannel.StartingPoint);
    }

    [Fact]
    public async Task PrimaryChannelWithNoMembers_NeedsAttention() {
        WithOrg(ConnectionState.Connected);
        WithPrimaryChannel(members: 0);

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.PrimaryChannel.Status);
        Assert.Equal([StageReason.PrimaryChannelHasNoMembers], status.PrimaryChannel.Reasons);
    }

    [Fact]
    public async Task CheckpointYoungerThan48Hours_IsOk_AndReportsItsAgeAndTimeToExpiry() {
        WithOrg(ConnectionState.Connected);
        WithPrimaryChannel();
        WithCheckpointAged(TimeSpan.FromHours(47));

        var status = await Status();

        Assert.Equal(StageStatus.Ok, status.PrimaryChannel.Status);
        Assert.Equal(TimeSpan.FromHours(47), status.PrimaryChannel.CheckpointAge);
        Assert.Equal(TimeSpan.FromHours(25), status.PrimaryChannel.CheckpointExpiresIn);
    }

    [Fact]
    public async Task CheckpointOlderThan48Hours_NeedsAttention_AsNearExpiry() {
        WithOrg(ConnectionState.Connected);
        WithPrimaryChannel();
        WithCheckpointAged(TimeSpan.FromHours(49));

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.PrimaryChannel.Status);
        Assert.Equal([StageReason.CheckpointNearExpiry], status.PrimaryChannel.Reasons);
        Assert.Equal(TimeSpan.FromHours(23), status.PrimaryChannel.CheckpointExpiresIn);
    }

    [Fact]
    public async Task CheckpointOlderThan72Hours_NeedsAttention_AsExpired() {
        WithOrg(ConnectionState.Connected);
        WithPrimaryChannel();
        WithCheckpointAged(TimeSpan.FromHours(73));

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.PrimaryChannel.Status);
        Assert.Equal([StageReason.CheckpointExpired], status.PrimaryChannel.Reasons);
        Assert.Equal(TimeSpan.Zero, status.PrimaryChannel.CheckpointExpiresIn);
    }

    [Fact]
    public async Task TargetConnectionSavedButNeverProved_IsToDo() {
        WithOrg(ConnectionState.Connected);
        WithTarget(ConnectionState.Incomplete);

        var status = await Status();

        Assert.Equal(StageStatus.ToDo, status.TargetConnection.Status);
        Assert.Equal([StageReason.TargetConnectionNotProved], status.TargetConnection.Reasons);
    }

    [Fact]
    public async Task TargetConnectionWhoseLastProofFailed_NeedsAttention() {
        WithOrg(ConnectionState.Connected);
        WithTarget(ConnectionState.Failed);

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.TargetConnection.Status);
        Assert.Equal([StageReason.TargetConnectionFailed], status.TargetConnection.Reasons);
    }

    [Fact]
    public async Task ProvedTargetConnection_IsOk_AndReportsWhereItPoints() {
        WithOrg(ConnectionState.Connected);
        WithTarget(ConnectionState.Connected);

        var status = await Status();

        Assert.Equal(StageStatus.Ok, status.TargetConnection.Status);
        Assert.Equal(TargetDatabaseEngine.Postgres, status.TargetConnection.Engine);
        Assert.Equal("db.internal", status.TargetConnection.Host);
        Assert.Equal("warehouse", status.TargetConnection.DatabaseName);
        Assert.Equal(Now.UtcDateTime.AddDays(-2), status.TargetConnection.LastConnectedAt);
    }

    [Fact]
    public async Task BindingsWait_UntilBothPrimaryChannelAndTargetConnectionAreOk() {
        WithOrg(ConnectionState.Connected);
        WithPrimaryChannel();

        var status = await Status();

        Assert.Equal(StageStatus.Waiting, status.Bindings.Status);
        Assert.Equal([PipelineStage.TargetConnection], status.Bindings.WaitingOn);
    }

    [Fact]
    public async Task NoBindings_IsToDo() {
        WithEverythingOk();
        WithBindings();

        var status = await Status();

        Assert.Equal(StageStatus.ToDo, status.Bindings.Status);
        Assert.Equal([StageReason.NoActiveBindings], status.Bindings.Reasons);
    }

    [Fact]
    public async Task OnlyInactiveBindings_IsToDo_NotNeedsAttention() {
        WithEverythingOk();
        WithBindings(inactive: 2);

        var status = await Status();

        Assert.Equal(StageStatus.ToDo, status.Bindings.Status);
        Assert.Equal([StageReason.NoActiveBindings], status.Bindings.Reasons);
    }

    [Fact]
    public async Task IncompleteBindingsStillBeingBuilt_DoNotNeedAttention() {
        WithEverythingOk();
        WithBindings(active: 2, incomplete: 1, inactive: 1);

        var status = await Status();

        Assert.Equal(StageStatus.Ok, status.Bindings.Status);
        Assert.Equal(new BindingStateCounts { Active = 2, Incomplete = 1, Inactive = 1 }, status.Bindings.Counts);
    }

    [Fact]
    public async Task ABindingTheWorkerForcedBackToIncomplete_NeedsAttention() {
        WithEverythingOk();
        WithBindings(active: 2, incomplete: 1, forced: 1);

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.Bindings.Status);
        Assert.Equal([StageReason.BindingForcedIncomplete], status.Bindings.Reasons);
    }

    [Fact]
    public async Task EveryBindingForcedBackToIncomplete_NeedsAttention_AndSaysNoneAreActive() {
        WithEverythingOk();
        WithBindings(incomplete: 2, forced: 2);

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.Bindings.Status);
        Assert.Equal([StageReason.BindingForcedIncomplete, StageReason.NoActiveBindings], status.Bindings.Reasons);
    }

    [Fact]
    public async Task FailedOrgConnection_DoesNotHideProblemsInStagesAlreadySetUp() {
        WithEverythingOk();
        WithOrg(ConnectionState.Failed);
        WithCheckpointAged(TimeSpan.FromHours(61));
        WithBindings(active: 2, incomplete: 1, forced: 1);

        var status = await Status();

        Assert.Equal(StageStatus.NeedsAttention, status.OrgConnection.Status);
        Assert.Equal(StageStatus.NeedsAttention, status.PrimaryChannel.Status);
        Assert.Equal([StageReason.CheckpointNearExpiry], status.PrimaryChannel.Reasons);
        Assert.Equal(StageStatus.Ok, status.TargetConnection.Status);
        Assert.Equal(StageStatus.NeedsAttention, status.Bindings.Status);
        Assert.Equal(PipelineStage.OrgConnection, status.NextStep);
    }

    [Fact]
    public async Task FailedOrgConnection_MakesStagesNotYetSetUpWait() {
        WithOrg(ConnectionState.Failed);
        WithPrimaryChannel();

        var status = await Status();

        Assert.Equal(StageStatus.Ok, status.PrimaryChannel.Status);
        Assert.Equal(StageStatus.Waiting, status.TargetConnection.Status);
        Assert.Equal([PipelineStage.OrgConnection], status.TargetConnection.WaitingOn);
        Assert.Equal(StageStatus.Waiting, status.Bindings.Status);
        Assert.Equal([PipelineStage.TargetConnection], status.Bindings.WaitingOn);
    }

    [Fact]
    public async Task NextStep_IsTheEarliestStageThatIsNotOk() {
        WithEverythingOk();
        WithTarget(ConnectionState.Failed);
        WithBindings(incomplete: 1, forced: 1);

        var status = await Status();

        Assert.Equal(PipelineStage.TargetConnection, status.NextStep);
    }

    [Fact]
    public async Task EverythingOk_IsAllOk_WithNoNextStep() {
        WithEverythingOk();

        var status = await Status();

        Assert.All(status.Stages, s => Assert.Equal(StageStatus.Ok, s.Status));
        Assert.True(status.AllOk);
        Assert.Null(status.NextStep);
    }
}
