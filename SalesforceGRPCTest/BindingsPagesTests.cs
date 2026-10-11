using Application.Pipeline;
using Application.Services.Interfaces;
using Database.Models;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SalesforceGrpc.Controllers.Pages;
using SalesforceGrpc.ViewModels;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceGRPCTest;

/// <summary>
/// The Bindings pages, driven through <see cref="BindingsPageController"/>: what each page shows, what it waits
/// on, and where it sends the user instead.
/// </summary>
/// <remarks>
/// The controller is the seam because the pages' decisions — redirect, not found, the Target Database being
/// down — are made while loading, not only while shaping a view. The page view is read back from the props the
/// Svelte component would receive.
/// </remarks>
public class BindingsPagesTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IPipelineStatusService _pipeline = Substitute.For<IPipelineStatusService>();
    private readonly IBindingService _bindings = Substitute.For<IBindingService>();
    private readonly IPlatformEventChannelRepository _channels = Substitute.For<IPlatformEventChannelRepository>();

    private const int PrimaryChannelId = 1;
    private const int BindingId = 42;

    public BindingsPagesTests() {
        _pipeline.GetAsync(Arg.Any<CancellationToken>()).Returns(Ready());
        _bindings.GetBindingsAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private BindingsPageController NewController() => new(_pipeline, _bindings, _channels);

    #region Waiting

    [Fact]
    public async Task WithNoPrimaryChannel_TheListWaitsOnIt_AndLinksToChannels() {
        _pipeline.GetAsync(Arg.Any<CancellationToken>()).Returns(Ready() with {
            PrimaryChannel = new PrimaryChannelStage { Status = StageStatus.ToDo, Reasons = [StageReason.NoPrimaryChannel] }
        });

        var view = Page<BindingsView>(await NewController().Index(Ct));

        var step = Assert.Single(view.Waiting);
        Assert.Equal("/channels", step.Href);
        Assert.Empty(view.Rows);
    }

    [Fact]
    public async Task WithNoPrimaryChannel_BecauseTheOrgIsNotConnected_ItWaitsOnTheOrgConnectionFirst() {
        _pipeline.GetAsync(Arg.Any<CancellationToken>()).Returns(Ready() with {
            OrgConnection = new OrgConnectionStage { Status = StageStatus.ToDo, Reasons = [StageReason.NoOrgConnection] },
            PrimaryChannel = new PrimaryChannelStage {
                Status = StageStatus.Waiting, WaitingOn = [PipelineStage.OrgConnection]
            }
        });

        var view = Page<BindingsView>(await NewController().Index(Ct));

        Assert.Equal(["/org-connection", "/channels"], view.Waiting.Select(s => s.Href));
    }

    [Theory]
    [InlineData(StageReason.NoTargetConnection)]
    [InlineData(StageReason.TargetConnectionNotProved)]
    public async Task WithoutAUsableTargetConnection_EveryPageWaitsOnIt(StageReason reason) {
        _pipeline.GetAsync(Arg.Any<CancellationToken>()).Returns(Ready() with {
            TargetConnection = new TargetConnectionStage { Status = StageStatus.ToDo, Reasons = [reason] }
        });

        var list = Page<BindingsView>(await NewController().Index(Ct));
        var create = Page<NewBindingView>(await NewController().New(member: 7, Ct));
        var editor = Page<BindingView>(await NewController().Show(BindingId, Ct));

        Assert.Equal("/target-connection", Assert.Single(list.Waiting).Href);
        Assert.Equal("/target-connection", Assert.Single(create.Waiting).Href);
        Assert.Equal("/target-connection", Assert.Single(editor.Waiting).Href);
        await _bindings.DidNotReceive().GetTargetTablesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithAFailedTargetConnection_TheListStillWorks() {
        // The list reads the App Database only, so a Target Database outage is the editor's to report.
        _pipeline.GetAsync(Arg.Any<CancellationToken>()).Returns(Ready() with {
            TargetConnection = new TargetConnectionStage {
                Status = StageStatus.NeedsAttention, Reasons = [StageReason.TargetConnectionFailed],
                Engine = TargetDatabaseEngine.Postgres
            }
        });
        ArrangePrimaryChannel(("AccountChangeEvent", null));

        var view = Page<BindingsView>(await NewController().Index(Ct));

        Assert.Empty(view.Waiting);
        Assert.Single(view.Rows);
    }

    #endregion

    #region The list

    [Fact]
    public async Task TheList_ShowsOnlyThePrimaryChannelsEntities() {
        ArrangePrimaryChannel(("AccountChangeEvent", null));
        _bindings.GetBindingsAsync(Arg.Any<CancellationToken>()).Returns([
            Binding(BindingId, "AccountChangeEvent"),
            Binding(43, "OpportunityChangeEvent")
        ]);

        var view = Page<BindingsView>(await NewController().Index(Ct));

        Assert.Equal(["AccountChangeEvent"], view.Rows.Select(r => r.Entity));
    }

    [Fact]
    public async Task TheList_MatchesAMemberToItsBinding_ByEntity_NotByTheMembersLink() {
        // Bound through another Channel's member, so this member's own link is empty.
        ArrangePrimaryChannel(("AccountChangeEvent", null));
        _bindings.GetBindingsAsync(Arg.Any<CancellationToken>()).Returns([
            Binding(BindingId, "AccountChangeEvent", keyColumn: "sf_id", fieldMappings: 12, fields: 40)
        ]);

        var row = Assert.Single(Page<BindingsView>(await NewController().Index(Ct)).Rows);

        Assert.Equal(BindingId, row.BindingId);
        Assert.Equal("salesforce.accountchangeevent", row.TargetTable);
        Assert.Equal("sf_id", row.KeyMappingColumn);
        Assert.Equal((12, 40), (row.FieldMappingCount, row.FieldCount));
        Assert.Equal($"/bindings/{BindingId}", row.Href);
    }

    [Fact]
    public async Task AnUnboundEntity_LinksToBindingIt() {
        ArrangePrimaryChannel(("ContactChangeEvent", null));

        var row = Assert.Single(Page<BindingsView>(await NewController().Index(Ct)).Rows);

        Assert.Equal(BindingRowState.Unbound, row.State);
        Assert.Null(row.BindingId);
        Assert.Equal($"/bindings/new?member={MemberIdOf(0)}", row.Href);
    }

    [Theory]
    [InlineData("Active", false, BindingRowState.Active)]
    [InlineData("Inactive", false, BindingRowState.Inactive)]
    [InlineData("Incomplete", false, BindingRowState.Incomplete)]
    [InlineData("Incomplete", true, BindingRowState.NeedsAttention)]
    public async Task ABindingForcedBackToIncomplete_NeedsAttention_DistinctFromOneNeverFinished(
        string state, bool needsAttention, BindingRowState shown) {
        ArrangePrimaryChannel(("AccountChangeEvent", BindingId));
        _bindings.GetBindingsAsync(Arg.Any<CancellationToken>()).Returns([
            Binding(BindingId, "AccountChangeEvent", state, needsAttention)
        ]);

        var row = Assert.Single(Page<BindingsView>(await NewController().Index(Ct)).Rows);

        Assert.Equal(shown, row.State);
    }

    #endregion

    #region Choosing a Target Table

    [Fact]
    public async Task ANewBinding_ForAMemberThatDoesNotExist_IsNotFound() {
        ArrangePrimaryChannel(("AccountChangeEvent", null));

        var view = Page<NewBindingView>(await NewController().New(member: 999, Ct));

        Assert.Null(view.Member);
    }

    [Fact]
    public async Task ANewBinding_ForAMemberOfAnotherChannel_IsNotFound() {
        ArrangePrimaryChannel(("AccountChangeEvent", null));
        _channels.GetMemberByIdAsync(500, Arg.Any<CancellationToken>()).Returns(Member(500, "LeadChangeEvent", channelId: 2));

        var view = Page<NewBindingView>(await NewController().New(member: 500, Ct));

        Assert.Null(view.Member);
    }

    [Fact]
    public async Task ANewBinding_ForAnEntityAlreadyBound_GoesToItsEditorInstead() {
        ArrangePrimaryChannel(("AccountChangeEvent", null));
        _bindings.GetBindingsAsync(Arg.Any<CancellationToken>()).Returns([Binding(BindingId, "AccountChangeEvent")]);

        var result = await NewController().New(member: MemberIdOf(0), Ct);

        Assert.Equal($"/bindings/{BindingId}", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task ANewBinding_ListsTablesMatchingTheEntityFirst_AndTablesAlreadyBoundLast_WithTheirEntity() {
        ArrangePrimaryChannel(("AccountChangeEvent", null));
        _bindings.GetTargetTablesAsync(null, Arg.Any<CancellationToken>()).Returns([
            Table("audit_log", boundTo: "LeadChangeEvent"),
            Table("contacts"),
            Table("accounts"),
            Table("zebra")
        ]);

        var view = Page<NewBindingView>(await NewController().New(member: MemberIdOf(0), Ct));

        Assert.Equal("AccountChangeEvent", view.Member!.Entity);
        Assert.Equal(["salesforce.accounts", "salesforce.contacts", "salesforce.zebra", "salesforce.audit_log"],
            view.Tables.Select(t => t.FullName));
        Assert.True(view.Tables[0].NameMatches);
        Assert.Equal("LeadChangeEvent", view.Tables[^1].BoundEntity);
    }

    [Fact]
    public async Task ANewBinding_WhenTheTargetDatabaseIsDown_SaysSo_WithTheDriversWords() {
        ArrangePrimaryChannel(("AccountChangeEvent", null));
        _bindings.GetTargetTablesAsync(null, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("connection refused (127.0.0.1:5432)"));

        var view = Page<NewBindingView>(await NewController().New(member: MemberIdOf(0), Ct));

        Assert.NotNull(view.Member);
        Assert.Equal("connection refused (127.0.0.1:5432)", view.TargetError!.RawResponse);
    }

    #endregion

    #region The editor

    [Fact]
    public async Task TheEditor_ForABindingThatDoesNotExist_IsNotFound() {
        _bindings.GetBindingAsync(999, Arg.Any<CancellationToken>()).ThrowsAsync(new KeyNotFoundException("Binding 999 was not found."));

        var view = Page<BindingView>(await NewController().Show(999, Ct));

        Assert.Empty(view.Waiting);
        Assert.Null(view.Binding);
    }

    [Fact]
    public async Task TheEditor_WhenTheTargetDatabaseIsDown_StillShowsTheBinding_WithoutItsMappings() {
        ArrangeEditor();
        _bindings.GetBindingColumnsAsync(BindingId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("connection refused"));

        var editor = Page<BindingView>(await NewController().Show(BindingId, Ct)).Binding!;

        Assert.Equal("AccountChangeEvent", editor.Entity);
        Assert.Equal(MemberIdOf(0), editor.MemberId);
        Assert.Null(editor.Target);
        Assert.Equal("connection refused", editor.TargetError!.RawResponse);
    }

    [Fact]
    public async Task TheEditor_KnowsThePrimaryChannelsMemberForItsEntity_EvenWhenThatMemberIsNotLinked() {
        // Deleting and binding again starts from this member.
        ArrangeEditor();

        var editor = Page<BindingView>(await NewController().Show(BindingId, Ct)).Binding!;

        Assert.Equal(MemberIdOf(0), editor.MemberId);
    }

    [Fact]
    public async Task TheEditor_OfABindingWithNoFieldMappings_PrefillsItsDraftWithTheNameMatches() {
        ArrangeEditor(fields: [
            Field("Id", suggested: "id"),
            Field("Name", suggested: "name"),
            Field("Phone", suggested: "phone"),
            Field("Fax", suggested: "phone"),
            Field("Website", suggested: "website_that_is_gone"),
            Field("Industry")
        ]);

        var target = Page<BindingView>(await NewController().Show(BindingId, Ct)).Binding!.Target!;

        // The record ID belongs in the Key Mapping; a column is offered once; a column must exist.
        Assert.Equal([("Name", "name"), ("Phone", "phone")],
            target.Prefill.Select(m => (m.SalesforceFieldName, m.TargetColumnName)));
    }

    [Fact]
    public async Task TheEditor_OfABindingWithFieldMappings_PrefillsNothing() {
        ArrangeEditor(fieldMappings: 1, fields: [Field("Name", suggested: "name")]);

        var target = Page<BindingView>(await NewController().Show(BindingId, Ct)).Binding!.Target!;

        Assert.Empty(target.Prefill);
    }

    [Fact]
    public async Task TheEditor_WithoutAKeyMapping_SuggestsAUniqueRecordIdColumn_AndKeepsItOutOfThePrefill() {
        ArrangeEditor(fields: [Field("Name", suggested: "sf_id")]);

        var target = Page<BindingView>(await NewController().Show(BindingId, Ct)).Binding!.Target!;

        Assert.Equal("sf_id", target.SuggestedKeyColumn);
        Assert.Empty(target.Prefill);
    }

    [Fact]
    public async Task TheEditor_WithAKeyMapping_SuggestsNoKeyColumn() {
        ArrangeEditor(keyColumn: "sf_id");

        var target = Page<BindingView>(await NewController().Show(BindingId, Ct)).Binding!.Target!;

        Assert.Null(target.SuggestedKeyColumn);
    }

    #endregion

    #region Fixtures

    private static PipelineStatus Ready() => new(
        new OrgConnectionStage { Status = StageStatus.Ok },
        new PrimaryChannelStage { Status = StageStatus.Ok, ChannelFullName = "Sales__chn", MemberCount = 1 },
        new TargetConnectionStage { Status = StageStatus.Ok, Engine = TargetDatabaseEngine.Postgres },
        new BindingsStage { Status = StageStatus.ToDo, Reasons = [StageReason.NoActiveBindings] });

    private static int MemberIdOf(int index) => 100 + index;

    private void ArrangePrimaryChannel(params (string Entity, int? BindingId)[] members) {
        var channel = new PlatformEventChannelEntity {
            Id = PrimaryChannelId, SfId = "0YL000000000001", FullName = "Sales__chn", DeveloperName = "Sales",
            MasterLabel = "Sales", ChannelType = "data", IsPrimary = true,
            Members = members.Select((m, i) => Member(MemberIdOf(i), m.Entity, PrimaryChannelId, m.BindingId)).ToList()
        };
        _channels.GetPrimaryChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);
        foreach (var member in channel.Members) {
            _channels.GetMemberByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        }
    }

    private static PlatformEventChannelMemberEntity Member(int id, string entity, int channelId, int? bindingId = null) => new() {
        Id = id, ChannelId = channelId, SfId = $"0v8{id:D15}", FullName = $"Sales_chn_{entity}",
        SelectedEntity = entity, CdcSchemaId = bindingId
    };

    private static BindingDTO Binding(int id, string entity, string state = "Incomplete", bool needsAttention = false,
        string? keyColumn = null, int fieldMappings = 0, int? fields = null) => new() {
        Id = id, EntityName = entity, TargetTable = $"salesforce.{entity.ToLowerInvariant()}", State = state,
        NeedsAttention = needsAttention, KeyMappingColumn = keyColumn, FieldMappingCount = fieldMappings,
        FieldCount = fields
    };

    private static TargetTableDTO Table(string name, string? boundTo = null) => new() {
        SchemaName = "salesforce", TableName = name, FullName = $"salesforce.{name}", BoundEntityName = boundTo
    };

    private static BindableFieldDTO Field(string name, string? suggested = null) => new() {
        Name = name, FieldType = "String", AvroType = "string", SuggestedColumnName = suggested
    };

    private static TargetColumnDTO Column(string name, bool unique = false) => new() {
        ColumnName = name, DataType = "text", IsNullable = !unique, IsUnique = unique
    };

    /// <summary>A Binding for the Primary Channel's one Entity, linked through another Channel's member.</summary>
    private void ArrangeEditor(string? keyColumn = null, int fieldMappings = 0, BindableFieldDTO[]? fields = null) {
        ArrangePrimaryChannel(("AccountChangeEvent", null));
        _bindings.GetBindingAsync(BindingId, Arg.Any<CancellationToken>()).Returns(
            Binding(BindingId, "AccountChangeEvent", keyColumn: keyColumn, fieldMappings: fieldMappings));
        _bindings.GetBindableFieldsForBindingAsync(BindingId, Arg.Any<CancellationToken>()).Returns(fields ?? []);
        _bindings.GetBindingColumnsAsync(BindingId, Arg.Any<CancellationToken>()).Returns([
            Column("id", unique: true), Column("sf_id", unique: true), Column("name"), Column("phone"), Column("is_deleted")
        ]);
        _bindings.GetSoftDeleteColumnsAsync(BindingId, Arg.Any<CancellationToken>()).Returns(["is_deleted"]);
        _bindings.ValidateBindingAsync(BindingId, Arg.Any<CancellationToken>()).Returns(new BindingValidationDTO { BindingId = BindingId });
    }

    private static readonly JsonSerializerOptions PropsJson = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>The page half of the props a page action hands its Svelte component.</summary>
    private static TPage Page<TPage>(IActionResult result) {
        var page = Assert.IsType<SveltePage>(Assert.IsType<ViewResult>(result).Model);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(page.Props));
        return JsonSerializer.Deserialize<PageView<TPage>>(json, PropsJson)!.Page;
    }

    #endregion
}
