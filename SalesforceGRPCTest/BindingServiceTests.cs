using Application.Bindings;
using Application.Services;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.ComponentModel.DataAnnotations;

namespace SalesforceGRPCTest;

/// <summary>
/// The Binding lifecycle, driven through <see cref="IBindingService"/> — the one seam this feature is tested
/// at.
/// </summary>
/// <remarks>
/// The service is built over substituted repositories, and the Target Database substitute is asserted against
/// to prove that configuring a Binding never writes to the user's data. The Avro fixture is the real
/// AccountChangeEvent.avsc, so the field names under test are the ones Salesforce actually sends.
/// </remarks>
public class BindingServiceTests {

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IMetaRepository _meta = Substitute.For<IMetaRepository>();
    private readonly IAvroSchemaRepository _avro = Substitute.For<IAvroSchemaRepository>();
    private readonly IRepository _target = Substitute.For<IRepository>();
    private readonly IPlatformEventChannelRepository _channels = Substitute.For<IPlatformEventChannelRepository>();
    private readonly IEntitySchemaProvider _entitySchemas = Substitute.For<IEntitySchemaProvider>();
    private readonly IBindingChangeSignal _signal = Substitute.For<IBindingChangeSignal>();

    private const int MemberId = 5;
    private const int ChannelId = 1;
    private const int BindingId = 42;
    private const string Entity = "AccountChangeEvent";
    private const string TargetTable = "salesforce.account";

    private BindingService NewService() =>
        new(_meta, _avro, _target, _channels, _entitySchemas, _signal, NullLogger<BindingService>.Instance);

    #region Arrangement

    private static string AccountSchemaJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "avro", "AccountChangeEvent.avsc"));

    private static PlatformEventChannelEntity Channel(string channelType = "data", bool isPrimary = false) => new() {
        Id = ChannelId, SfId = "0YL000000000001", FullName = "Sales__chn",
        DeveloperName = "Sales", ChannelType = channelType, IsPrimary = isPrimary
    };

    private static PlatformEventChannelMemberEntity Member(int? bindingId = null) => new() {
        Id = MemberId, ChannelId = ChannelId, SfId = "0v8000000000001",
        FullName = "Sales_chn_AccountChangeEvent", SelectedEntity = Entity, CdcSchemaId = bindingId
    };

    private static DbAvroSchema AvroSchema(string schemaId = "SCHEMA_V1") => new() {
        Id = 9, SchemaId = schemaId, RecordName = Entity, SchemaJson = AccountSchemaJson(),
        DateCreated = DateTime.UtcNow
    };

    private static ColumnMetadata Col(string name, string dataType, bool nullable = true,
        int? maxLength = null, bool unique = false) {
        var column = new ColumnMetadata { ColumnName = name, DataType = dataType, IsNullable = nullable, MaxLength = maxLength };
        if (unique) {
            column.ColumnConstraints.Add(new ColumnConstraint { ConstraintType = "UNIQUE", ConstraintName = $"{name}_key" });
        }
        return column;
    }

    private static TableMetadata AccountTable() => new() {
        SchemaName = "salesforce",
        TableName = "account",
        Columns = [
            Col("sf_id", "character varying", nullable: false, maxLength: 18, unique: true),
            Col("name", "text"),
            Col("phone", "text"),
            Col("annual_revenue", "numeric"),
            Col("created_at", "timestamp without time zone"),
            Col("is_deleted", "boolean"),
            Col("employee_count", "integer")
        ],
        Constraints = []
    };

    private static CDCSchema Binding(BindingState state = BindingState.Incomplete,
        string schemaId = "SCHEMA_V1", bool softDelete = false, string? softDeleteColumn = null) => new() {
        Id = BindingId, EntityName = Entity, DbSchemaFullName = TargetTable, BindingState = state,
        SoftDeleteEnabled = softDelete, SoftDeleteColumnName = softDeleteColumn,
        AvroSchema = AvroSchema(schemaId)
    };

    /// <summary>Wires up a Binding that is complete and would pass validation.</summary>
    private void ArrangeValidBinding(BindingState state = BindingState.Incomplete) {
        _meta.GetSchemaById(BindingId).Returns(Binding(state));
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "MappedSFKey", TargetFieldName = "sf_id" },
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "Phone", TargetFieldName = "phone" },
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "AnnualRevenue", TargetFieldName = "annual_revenue" }
        ]);
        _target.DatabaseType.Returns(DbType.Postgres);
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(AccountTable());
        _entitySchemas.GetSchemaForEntityAsync(Entity, Arg.Any<CancellationToken>()).Returns(AvroSchema());
        _channels.GetMembersByBindingIdAsync(BindingId, Arg.Any<CancellationToken>()).Returns([Member(BindingId)]);
    }

    private void ArrangeMemberWithoutBinding(string channelType = "data") {
        _target.DatabaseType.Returns(DbType.Postgres);
        _channels.GetMemberByIdAsync(MemberId, Arg.Any<CancellationToken>()).Returns(Member());
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel(channelType));
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(AccountTable());
        _entitySchemas.GetSchemaForEntityAsync(Entity, Arg.Any<CancellationToken>()).Returns(AvroSchema());
        _meta.CreateNewSchemaWithAvroLink(Arg.Any<CDCSchema>(), Arg.Any<int>())
            .Returns(call => Binding(((CDCSchema)call[0]).BindingState));
        _avro.GetSchemaBySchemaIdAsync("SCHEMA_V1", Arg.Any<CancellationToken>()).Returns(AvroSchema());
    }

    #endregion

    #region Bindable fields

    [Fact]
    public async Task GetBindableFields_ReturnsFlattenedCompoundFields() {
        ArrangeMemberWithoutBinding();

        var fields = await NewService().GetBindableFieldsAsync(MemberId, Ct);

        Assert.Contains(fields, f => f.Name == "BillingAddressCity");
        Assert.DoesNotContain(fields, f => f.Name == "BillingAddress");
    }

    [Fact]
    public async Task GetBindableFields_CarriesTheSalesforceFieldTypeNotJustTheAvroType() {
        ArrangeMemberWithoutBinding();

        var fields = await NewService().GetBindableFieldsAsync(MemberId, Ct);

        // Both arrive as an Avro long; only the doc annotation tells them apart.
        Assert.Equal("DateTime", Assert.Single(fields, f => f.Name == "Some_Date_Time__c").FieldType);
        Assert.Equal("DateOnly", Assert.Single(fields, f => f.Name == "Some_Date__c").FieldType);
    }

    [Fact]
    public async Task GetBindableFields_SuggestsATargetColumnWhoseNameMatchesOnceConventionsAreNormalised() {
        ArrangeMemberWithoutBinding();
        _channels.GetMemberByIdAsync(MemberId, Arg.Any<CancellationToken>()).Returns(Member(BindingId));
        _meta.GetSchemaById(BindingId).Returns(Binding());
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([]);

        var fields = await NewService().GetBindableFieldsAsync(MemberId, Ct);

        // AnnualRevenue -> annual_revenue, NumberOfEmployees has no match here.
        Assert.Equal("annual_revenue", Assert.Single(fields, f => f.Name == "AnnualRevenue").SuggestedColumnName);
        Assert.Equal("phone", Assert.Single(fields, f => f.Name == "Phone").SuggestedColumnName);
    }

    [Fact]
    public async Task GetBindableFields_ForAMemberThatDoesNotExist_IsNotFound() {
        _channels.GetMemberByIdAsync(99, Arg.Any<CancellationToken>()).Returns((PlatformEventChannelMemberEntity?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => NewService().GetBindableFieldsAsync(99, Ct));
    }

    #endregion

    #region Creating a Binding

    [Fact]
    public async Task CreateBinding_StartsOutIncompleteSoAHalfBuiltBindingNeverWrites() {
        ArrangeMemberWithoutBinding();

        var binding = await NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct);

        Assert.Equal(nameof(BindingState.Incomplete), binding.State);
        await _meta.Received(1).CreateNewSchemaWithAvroLink(
            Arg.Is<CDCSchema>(s => s.BindingState == BindingState.Incomplete), Arg.Any<int>());
    }

    [Fact]
    public async Task CreateBinding_LinksTheChannelMemberToIt() {
        ArrangeMemberWithoutBinding();

        await NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct);

        await _channels.Received(1).SetMemberBindingAsync(MemberId, BindingId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBinding_ForAnEntityThatAlreadyHasOne_IsRejected() {
        ArrangeMemberWithoutBinding();
        _meta.GetSchemaByEntityName(Entity).Returns(Binding(BindingState.Active));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct));

        Assert.Contains(Entity, ex.Message, StringComparison.Ordinal);
        await _meta.DidNotReceive().CreateNewSchemaWithAvroLink(Arg.Any<CDCSchema>(), Arg.Any<int>());
    }

    [Fact]
    public async Task CreateBinding_ToATableAnotherEntityAlreadyWritesTo_IsRejected() {
        ArrangeMemberWithoutBinding();
        _meta.GetSchemaByTargetTable(TargetTable).Returns(new CDCSchema {
            Id = 7, EntityName = "ContactChangeEvent", DbSchemaFullName = TargetTable
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct));

        Assert.Contains("ContactChangeEvent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBinding_ToATableThatDoesNotExist_IsRejected() {
        ArrangeMemberWithoutBinding();
        _target.GetTableMetadata("nope", "salesforce", Arg.Any<CancellationToken>()).Returns((TableMetadata?)null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "nope" }, Ct));

        Assert.Contains("salesforce.nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBinding_OnAPlatformEventChannelMember_IsRejected() {
        // Bindings are Change Data Capture only; a platform event has no record to update or delete.
        ArrangeMemberWithoutBinding(channelType: "event");

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct));

        Assert.Contains("Change Data Capture", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DbType.SqlServer)]
    [InlineData(DbType.MySql)]
    public async Task CreateBinding_AgainstADriverThatIsNotImplemented_ReportsThatClearly(DbType dbType) {
        ArrangeMemberWithoutBinding();
        _target.DatabaseType.Returns(dbType);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct));

        Assert.Contains(dbType.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBinding_NeverWritesToTheTargetDatabase() {
        ArrangeMemberWithoutBinding();

        await NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct);

        await _target.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        await _target.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>(), Arg.Any<Dictionary<string, object>>());
        await _target.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>());
    }

    #endregion

    #region Field Mappings

    [Fact]
    public async Task SetFieldMappings_ReplacesTheSetAndKeepsTheKeyMapping() {
        ArrangeValidBinding();

        await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "phone" }]
        }, Ct);

        await _meta.Received(1).ReplaceFieldMappings(BindingId, Arg.Is<IEnumerable<MappedField>>(m =>
            m.Any(f => f.SalesforceFieldName == "MappedSFKey" && f.TargetFieldName == "sf_id") &&
            m.Any(f => f.SalesforceFieldName == "Phone")));
    }

    [Fact]
    public async Task SetFieldMappings_MappingASalesforceFieldTheEntityDoesNotCarry_IsRejected() {
        ArrangeValidBinding();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetFieldMappingsAsync(BindingId,
            new SetFieldMappingsDTO {
                Mappings = [new FieldMappingDTO { SalesforceFieldName = "NotAField", TargetColumnName = "phone" }]
            }, Ct));

        Assert.Contains("NotAField", ex.Message, StringComparison.Ordinal);
        await _meta.DidNotReceive().ReplaceFieldMappings(Arg.Any<int>(), Arg.Any<IEnumerable<MappedField>>());
    }

    [Fact]
    public async Task SetFieldMappings_MappingAnUnflattenedCompoundName_IsRejected() {
        // "BillingAddress" is not a field the events carry; only its flattened parts are.
        ArrangeValidBinding();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetFieldMappingsAsync(BindingId,
            new SetFieldMappingsDTO {
                Mappings = [new FieldMappingDTO { SalesforceFieldName = "BillingAddress", TargetColumnName = "name" }]
            }, Ct));

        Assert.Contains("BillingAddress", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetFieldMappings_MappingToAColumnThatDoesNotExist_IsRejected() {
        ArrangeValidBinding();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetFieldMappingsAsync(BindingId,
            new SetFieldMappingsDTO {
                Mappings = [new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "no_such_column" }]
            }, Ct));

        Assert.Contains("no_such_column", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetFieldMappings_MappingTwoSalesforceFieldsToOneColumn_IsRejected() {
        ArrangeValidBinding();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetFieldMappingsAsync(BindingId,
            new SetFieldMappingsDTO {
                Mappings = [
                    new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "name" },
                    new FieldMappingDTO { SalesforceFieldName = "Fax", TargetColumnName = "name" }
                ]
            }, Ct));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetFieldMappings_MappingToTheKeyMappingColumn_IsRejected() {
        // The Key Mapping owns that column; a field writing to it too would fight the WHERE clause.
        ArrangeValidBinding();

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SetFieldMappingsAsync(BindingId,
            new SetFieldMappingsDTO {
                Mappings = [new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "sf_id" }]
            }, Ct));
    }

    [Fact]
    public async Task SetFieldMappings_LeavingEveryFieldUnmapped_IsAllowedWhileIncomplete() {
        ArrangeValidBinding();

        await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO { Mappings = [] }, Ct);

        await _meta.Received(1).ReplaceFieldMappings(BindingId, Arg.Any<IEnumerable<MappedField>>());
    }

    #endregion

    #region Key Mapping

    [Fact]
    public async Task SetKeyMapping_StoresItUnderTheSentinelFieldName() {
        ArrangeValidBinding();

        await NewService().SetKeyMappingAsync(BindingId, new SetKeyMappingDTO { TargetColumnName = "sf_id" }, Ct);

        await _meta.Received(1).ReplaceFieldMappings(BindingId, Arg.Is<IEnumerable<MappedField>>(m =>
            m.Count(f => f.SalesforceFieldName == "MappedSFKey" && f.TargetFieldName == "sf_id") == 1));
    }

    [Fact]
    public async Task SetKeyMapping_ToAColumnThatCannotHoldARecordId_IsRejected() {
        ArrangeValidBinding();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetKeyMappingAsync(BindingId,
            new SetKeyMappingDTO { TargetColumnName = "employee_count" }, Ct));

        Assert.Contains("employee_count", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetKeyMapping_ToAColumnThatDoesNotExist_IsRejected() {
        ArrangeValidBinding();

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SetKeyMappingAsync(BindingId,
            new SetKeyMappingDTO { TargetColumnName = "nope" }, Ct));
    }

    [Fact]
    public async Task SetKeyMapping_OnAnInactiveBinding_IsAllowedSoAMistakeIsCorrectable() {
        ArrangeValidBinding(BindingState.Inactive);

        await NewService().SetKeyMappingAsync(BindingId, new SetKeyMappingDTO { TargetColumnName = "sf_id" }, Ct);

        await _meta.Received(1).ReplaceFieldMappings(BindingId, Arg.Any<IEnumerable<MappedField>>());
    }

    #endregion

    #region Validation

    [Fact]
    public async Task Validate_OnACompleteBinding_CanActivate() {
        ArrangeValidBinding();

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.True(result.CanActivate);
        Assert.Empty(result.Blockers);
    }

    [Fact]
    public async Task Validate_NamesTheAvroSchemaRevisionItRanAgainst() {
        ArrangeValidBinding();

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.Equal("SCHEMA_V1", result.ValidatedAgainstSchemaId);
    }

    [Fact]
    public async Task Validate_WithoutAKeyMapping_CannotActivateAndSaysSo() {
        ArrangeValidBinding();
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "Phone", TargetFieldName = "phone" }
        ]);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Blockers, b => b.Contains("Key Mapping", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_WithNoFieldMappingsAtAll_CannotActivate() {
        ArrangeValidBinding();
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "MappedSFKey", TargetFieldName = "sf_id" }
        ]);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Blockers, b => b.Contains("Field Mapping", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_WithAMappingToAColumnSinceDropped_CannotActivate() {
        ArrangeValidBinding();
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "MappedSFKey", TargetFieldName = "sf_id" },
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "Phone", TargetFieldName = "dropped_column" }
        ]);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Blockers, b => b.Contains("dropped_column", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_WithAMappedFieldTheNewAvroSchemaNoLongerCarries_CannotActivateAndNamesIt() {
        ArrangeValidBinding();
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "MappedSFKey", TargetFieldName = "sf_id" },
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "RemovedInSalesforce__c", TargetFieldName = "phone" }
        ]);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Blockers, b => b.Contains("RemovedInSalesforce__c", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_WithAnErrorLevelTypeMismatch_CannotActivate() {
        ArrangeValidBinding();
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "MappedSFKey", TargetFieldName = "sf_id" },
            // A DateTime into an integer column cannot succeed.
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "Some_Date_Time__c", TargetFieldName = "employee_count" }
        ]);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Results, r => r.Level == nameof(CompatibilityLevel.Error)
            && r.SalesforceFieldName == "Some_Date_Time__c");
    }

    [Fact]
    public async Task Validate_WithOnlyAWarning_CanStillActivate() {
        ArrangeValidBinding();
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "MappedSFKey", TargetFieldName = "sf_id" },
            // Currency into an integer column truncates but works.
            new MappedField { SchemaId = BindingId, SalesforceFieldName = "AnnualRevenue", TargetFieldName = "employee_count" }
        ]);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.True(result.CanActivate);
        Assert.Contains(result.Results, r => r.Level == nameof(CompatibilityLevel.Warning));
    }

    [Fact]
    public async Task Validate_WarnsAboutANotNullColumnWithNoFieldMapping() {
        ArrangeValidBinding();
        var table = AccountTable();
        table.Columns.Add(Col("mandatory_note", "text", nullable: false));
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(table);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.True(result.CanActivate);
        Assert.Contains(result.Results, r => r.TargetColumnName == "mandatory_note"
            && r.Level == nameof(CompatibilityLevel.Warning));
    }

    [Fact]
    public async Task Validate_WhenTheTargetTableHasBeenDropped_CannotActivate() {
        ArrangeValidBinding();
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns((TableMetadata?)null);

        var result = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Blockers, b => b.Contains(TargetTable, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_DoesNotChangeTheBindingsState() {
        ArrangeValidBinding();

        await NewService().ValidateBindingAsync(BindingId, Ct);

        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
    }

    #endregion

    #region Binding State

    [Fact]
    public async Task Activate_OnAValidIncompleteBinding_MakesItActive() {
        ArrangeValidBinding();

        var binding = await NewService().ActivateAsync(BindingId, Ct);

        Assert.Equal(nameof(BindingState.Active), binding.State);
        await _meta.Received(1).SetBindingState(BindingId, BindingState.Active);
    }

    [Fact]
    public async Task Activate_OnAnInvalidBinding_ThrowsAndChangesNothing() {
        ArrangeValidBinding();
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns([]);

        await Assert.ThrowsAsync<ValidationException>(() => NewService().ActivateAsync(BindingId, Ct));

        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
    }

    [Fact]
    public async Task Activate_RevalidatesRatherThanTrustingAStoredResult() {
        // The Binding was left Inactive when everything was fine; the column has since been dropped.
        ArrangeValidBinding(BindingState.Inactive);
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns((TableMetadata?)null);

        await Assert.ThrowsAsync<ValidationException>(() => NewService().ActivateAsync(BindingId, Ct));
    }

    [Fact]
    public async Task Deactivate_KeepsEveryFieldMapping() {
        ArrangeValidBinding(BindingState.Active);

        var binding = await NewService().DeactivateAsync(BindingId, Ct);

        Assert.Equal(nameof(BindingState.Inactive), binding.State);
        await _meta.Received(1).SetBindingState(BindingId, BindingState.Inactive);
        await _meta.DidNotReceive().ReplaceFieldMappings(Arg.Any<int>(), Arg.Any<IEnumerable<MappedField>>());
    }

    [Fact]
    public async Task Deactivate_OnAnIncompleteBinding_IsRejectedBecauseItWasNeverOn() {
        ArrangeValidBinding();

        await Assert.ThrowsAsync<ValidationException>(() => NewService().DeactivateAsync(BindingId, Ct));
    }

    [Fact]
    public async Task Deactivate_DoesNotRunValidationSoABrokenBindingCanAlwaysBeSwitchedOff() {
        ArrangeValidBinding(BindingState.Active);
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns((TableMetadata?)null);

        var binding = await NewService().DeactivateAsync(BindingId, Ct);

        Assert.Equal(nameof(BindingState.Inactive), binding.State);
    }

    [Fact]
    public async Task SetFieldMappings_OnAnActiveBindingThatNowFailsValidation_MovesItToInactive() {
        // Saving an incompatible mapping is allowed — the user may be mid-edit — but an Active Binding must
        // not go on claiming to work, so it is switched off rather than left lying.
        ArrangeValidBinding(BindingState.Active);

        var binding = await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [
                // A DateTime into an integer column cannot succeed.
                new FieldMappingDTO { SalesforceFieldName = "Some_Date_Time__c", TargetColumnName = "employee_count" }
            ]
        }, Ct);

        Assert.Equal(nameof(BindingState.Inactive), binding.State);
        await _meta.Received(1).SetBindingState(BindingId, BindingState.Inactive);
    }

    [Fact]
    public async Task SetFieldMappings_OnAnActiveBindingThatStillValidates_LeavesItActive() {
        ArrangeValidBinding(BindingState.Active);

        var binding = await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "phone" }]
        }, Ct);

        Assert.Equal(nameof(BindingState.Active), binding.State);
        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
    }

    [Fact]
    public async Task Delete_RemovesTheBindingAndUnlinksTheChannelMember() {
        ArrangeValidBinding(BindingState.Inactive);

        await NewService().DeleteBindingAsync(BindingId, Ct);

        await _channels.Received(1).SetMemberBindingAsync(MemberId, null, Arg.Any<CancellationToken>());
        await _meta.Received(1).DeleteBinding(BindingId);
    }

    #endregion

    #region Soft delete

    [Fact]
    public async Task SetSoftDelete_OnABooleanColumn_IsAccepted() {
        ArrangeValidBinding();

        var binding = await NewService().SetSoftDeleteAsync(BindingId,
            new SetSoftDeleteDTO { Enabled = true, ColumnName = "is_deleted" }, Ct);

        Assert.True(binding.SoftDeleteEnabled);
        Assert.Equal("is_deleted", binding.SoftDeleteColumnName);
    }

    [Fact]
    public async Task SetSoftDelete_OnAColumnThatCannotHoldAFlag_IsRejected() {
        ArrangeValidBinding();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetSoftDeleteAsync(BindingId,
            new SetSoftDeleteDTO { Enabled = true, ColumnName = "name" }, Ct));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetSoftDelete_EnabledWithoutNamingAColumn_IsRejected() {
        ArrangeValidBinding();

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SetSoftDeleteAsync(BindingId,
            new SetSoftDeleteDTO { Enabled = true, ColumnName = null }, Ct));
    }

    [Fact]
    public async Task SetSoftDelete_TurnedOff_ClearsTheColumnName() {
        ArrangeValidBinding(BindingState.Active);

        var binding = await NewService().SetSoftDeleteAsync(BindingId,
            new SetSoftDeleteDTO { Enabled = false }, Ct);

        Assert.False(binding.SoftDeleteEnabled);
        Assert.Null(binding.SoftDeleteColumnName);
    }

    #endregion

    #region Primary channel and the subscription plan

    [Fact]
    public async Task GetSubscriptionPlan_WithNoPrimaryChannel_IsEmptyRatherThanThrowing() {
        _channels.GetPrimaryChannelAsync(Arg.Any<CancellationToken>()).Returns((PlatformEventChannelEntity?)null);

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.False(plan.HasChannel);
        Assert.Null(plan.TopicName);
        Assert.Empty(plan.ActiveBindingsBySchemaId);
    }

    [Fact]
    public async Task GetSubscriptionPlan_BuildsTheTopicFromTheChannelFullName() {
        ArrangePrimaryChannel(Binding(BindingState.Active));

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Equal("/data/Sales__chn", plan.TopicName);
    }

    [Fact]
    public async Task GetSubscriptionPlan_KeysActiveBindingsByAvroSchemaId() {
        ArrangePrimaryChannel(Binding(BindingState.Active));

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.True(plan.ActiveBindingsBySchemaId.ContainsKey("SCHEMA_V1"));
    }

    [Theory]
    [InlineData(BindingState.Incomplete)]
    [InlineData(BindingState.Inactive)]
    public async Task GetSubscriptionPlan_ExcludesBindingsThatAreNotActive(BindingState state) {
        ArrangePrimaryChannel(Binding(state));

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Empty(plan.ActiveBindingsBySchemaId);
        // The Entity is still reported as carried, so the worker can log a skip rather than a mystery.
        Assert.Contains(Entity, plan.ChannelEntityNames);
    }

    [Fact]
    public async Task GetSubscriptionPlan_ExcludesActiveBindingsForEntitiesTheChannelDoesNotCarry() {
        ArrangePrimaryChannel(Binding(BindingState.Active));
        _meta.GetCachedSchemas(Arg.Any<CancellationToken>()).Returns([
            Binding(BindingState.Active),
            new CDCSchema {
                Id = 99, EntityName = "ContactChangeEvent", DbSchemaFullName = "salesforce.contact",
                BindingState = BindingState.Active, AvroSchema = new DbAvroSchema {
                    Id = 11, SchemaId = "CONTACT_V1", RecordName = "ContactChangeEvent", SchemaJson = "{}"
                }
            }
        ]);

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.DoesNotContain("CONTACT_V1", plan.ActiveBindingsBySchemaId.Keys);
    }

    [Fact]
    public async Task SetPrimaryChannel_OnAPlatformEventChannel_IsRejected() {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel("event"));

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SetPrimaryChannelAsync(ChannelId, Ct));

        await _channels.DidNotReceive().SetPrimaryChannelAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPrimaryChannel_OnAChannelThatDoesNotExist_IsNotFound() {
        _channels.GetChannelByIdAsync(77, Arg.Any<CancellationToken>()).Returns((PlatformEventChannelEntity?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => NewService().SetPrimaryChannelAsync(77, Ct));
    }

    [Fact]
    public async Task SetPrimaryChannel_TellsTheWorkerToRePlan() {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel());

        await NewService().SetPrimaryChannelAsync(ChannelId, Ct);

        _signal.Received().Signal();
    }

    private void ArrangePrimaryChannel(CDCSchema binding) {
        var channel = Channel(isPrimary: true);
        channel.Members = [Member(binding.Id)];
        _channels.GetPrimaryChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);
        _meta.GetCachedSchemas(Arg.Any<CancellationToken>()).Returns([binding]);
    }

    #endregion

    #region Cache invalidation

    [Fact]
    public async Task ActivatingABinding_TellsTheWorkerToRePlanRatherThanWaitingOutTheCache() {
        ArrangeValidBinding();

        await NewService().ActivateAsync(BindingId, Ct);

        _signal.Received().Signal();
    }

    [Fact]
    public async Task ChangingFieldMappings_TellsTheWorkerToRePlan() {
        ArrangeValidBinding();

        await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "phone" }]
        }, Ct);

        _signal.Received().Signal();
    }

    #endregion
}
