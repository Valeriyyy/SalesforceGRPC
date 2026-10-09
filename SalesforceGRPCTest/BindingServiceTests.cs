using Application.Bindings;
using Application.Connections;
using Application.Services;
using Application.Targets;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Database.Targets;
using DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
    private readonly ITargetConnectionProvider _targetConnections;
    private readonly ITargetEngineCatalog _engines = Substitute.For<ITargetEngineCatalog>();
    private readonly IPlatformEventChannelRepository _channels = Substitute.For<IPlatformEventChannelRepository>();
    private readonly IEntitySchemaProvider _entitySchemas = Substitute.For<IEntitySchemaProvider>();
    private readonly IConfigurationChangeSignal _signal = Substitute.For<IConfigurationChangeSignal>();
    private readonly IOrgConnectionProvider _connections = Substitute.For<IOrgConnectionProvider>();
    private readonly ICheckpointRepository _checkpoints = Substitute.For<ICheckpointRepository>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));

    private const int MemberId = 5;
    private const int ChannelId = 1;
    private const int BindingId = 42;
    private const string Entity = "AccountChangeEvent";
    private const string TargetTable = "salesforce.account";

    public BindingServiceTests() {
        _targetConnections = TargetProviders.Of(_target);
        WithStoredEngine(TargetDatabaseEngine.Postgres, available: true);
    }

    /// <summary>A stored Target Connection on the given engine, and a profile that says whether it can be used.</summary>
    private void WithStoredEngine(TargetDatabaseEngine engine, bool available) {
        _targetConnections.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TargetConnection { Engine = engine, ConnectionState = ConnectionState.Connected });
        var profile = Substitute.For<ITargetEngineProfile>();
        profile.Engine.Returns(engine);
        profile.IsAvailable.Returns(available);
        profile.UnavailableReason.Returns(available ? null : $"Support for {engine} is not implemented yet.");
        _engines.For(engine).Returns(profile);
    }

    private BindingService NewService() =>
        new(_meta, _avro, _targetConnections, _engines, _channels, _entitySchemas, _signal, _connections, _checkpoints,
            _time, NullLogger<BindingService>.Instance);

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

    private static TableMetadata AccountTable(string? schemaName = "salesforce") => new() {
        SchemaName = schemaName,
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
        _target.Engine.Returns(TargetDatabaseEngine.Postgres);
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(AccountTable());
        _entitySchemas.GetSchemaForEntityAsync(Entity, Arg.Any<CancellationToken>()).Returns(AvroSchema());
        _channels.GetMembersByBindingIdAsync(BindingId, Arg.Any<CancellationToken>()).Returns([Member(BindingId)]);
    }

    private void ArrangeMemberWithoutBinding(string channelType = "data",
        TargetDatabaseEngine engine = TargetDatabaseEngine.Postgres, string? schemaName = "salesforce") {
        _target.Engine.Returns(engine);
        _channels.GetMemberByIdAsync(MemberId, Arg.Any<CancellationToken>()).Returns(Member());
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel(channelType));
        _target.GetTableMetadata("account", schemaName, Arg.Any<CancellationToken>()).Returns(AccountTable(schemaName));
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
    public async Task GetBindableFieldsForBinding_CarriesEachFieldsMappedColumn() {
        // Keyed by Binding rather than member, so it works for a Primary Channel member not linked to it yet.
        ArrangeValidBinding();

        var fields = await NewService().GetBindableFieldsForBindingAsync(BindingId, Ct);

        Assert.Equal("phone", Assert.Single(fields, f => f.Name == "Phone").MappedColumnName);
        Assert.Null(Assert.Single(fields, f => f.Name == "Name").MappedColumnName);
    }

    [Fact]
    public async Task GetBinding_CountsTheFieldsItsEntityCouldMap() {
        // The "12 of 40 fields" on the Bindings list: the 40 is every bindable field, flattened.
        ArrangeValidBinding();
        var bindable = await NewService().GetBindableFieldsForBindingAsync(BindingId, Ct);

        var binding = await NewService().GetBindingAsync(BindingId, Ct);

        Assert.Equal(bindable.Count, binding.FieldCount);
        Assert.Equal(2, binding.FieldMappingCount);
    }

    [Fact]
    public async Task GetBindingColumns_ReadsTheBindingsTargetTable_MarkingWhatIsMappedWhere() {
        ArrangeValidBinding();

        var columns = await NewService().GetBindingColumnsAsync(BindingId, Ct);

        Assert.Equal("Phone", Assert.Single(columns, c => c.ColumnName == "phone").MappedSalesforceFieldName);
        Assert.True(Assert.Single(columns, c => c.ColumnName == "sf_id").IsUnique);
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
    public async Task CreateBinding_ForAnEntityThatAlreadyHasOne_LinksTheMemberToItInsteadOfCreatingASecond() {
        // The Entity was bound through another Channel's member; this member can only ever share that Binding.
        ArrangeMemberWithoutBinding();
        _meta.GetSchemaByEntityName(Entity).Returns(Binding(BindingState.Active));

        var binding = await NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct);

        Assert.Equal(BindingId, binding.Id);
        Assert.Equal(nameof(BindingState.Active), binding.State);
        await _channels.Received(1).SetMemberBindingAsync(MemberId, BindingId, Arg.Any<CancellationToken>());
        await _meta.DidNotReceive().CreateNewSchemaWithAvroLink(Arg.Any<CDCSchema>(), Arg.Any<int>());
    }

    [Fact]
    public async Task CreateBinding_ForAMemberThatAlreadyHasOne_IsRejected() {
        ArrangeMemberWithoutBinding();
        _channels.GetMemberByIdAsync(MemberId, Arg.Any<CancellationToken>()).Returns(Member(BindingId));

        await Assert.ThrowsAsync<ValidationException>(() => NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct));

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
    [InlineData(TargetDatabaseEngine.SqlServer)]
    [InlineData(TargetDatabaseEngine.MySql)]
    public async Task CreateBinding_AgainstAnEngineThatIsNotSupported_ReportsThatClearly(TargetDatabaseEngine engine) {
        ArrangeMemberWithoutBinding();
        WithStoredEngine(engine, available: false);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct));

        Assert.Contains(engine.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBinding_NeverWritesToTheTargetDatabase() {
        ArrangeMemberWithoutBinding();

        await NewService().CreateBindingAsync(MemberId,
            new CreateBindingDTO { TargetSchema = "salesforce", TargetTable = "account" }, Ct);

        await _target.DidNotReceive().Upsert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        await _target.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>(), Arg.Any<Dictionary<string, object>>());
        await _target.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>());
    }

    /// <summary>
    /// SQLite has no schema concept. Leaving TargetSchema unset — the natural thing to do for it — must
    /// produce a dot-free stored name, not "public.account": SQLite would read "public" as an attached
    /// database that does not exist and fail every write.
    /// </summary>
    [Fact]
    public async Task CreateBinding_AgainstASqliteTarget_WithNoSchemaSpecified_ProducesADotFreeFullName() {
        ArrangeMemberWithoutBinding(engine: TargetDatabaseEngine.Sqlite, schemaName: null);
        WithStoredEngine(TargetDatabaseEngine.Sqlite, available: true);

        await NewService().CreateBindingAsync(MemberId, new CreateBindingDTO { TargetTable = "account" }, Ct);

        await _meta.Received(1).CreateNewSchemaWithAvroLink(
            Arg.Is<CDCSchema>(s => s.DbSchemaFullName == "account"), Arg.Any<int>());
    }

    /// <summary>
    /// BindingService must never inject "public" itself — that is Postgres's own convention and belongs to
    /// PostgresRepository. Leaving TargetSchema unset against a Postgres target should still ask the
    /// repository for schema null, trusting the repository to resolve its own default.
    /// </summary>
    [Fact]
    public async Task CreateBinding_AgainstAPostgresTarget_WithNoSchemaSpecified_PassesNullThrough_NeverPublic() {
        ArrangeMemberWithoutBinding(schemaName: null);

        await NewService().CreateBindingAsync(MemberId, new CreateBindingDTO { TargetTable = "account" }, Ct);

        await _target.Received(1).GetTableMetadata("account", null, Arg.Any<CancellationToken>());
        await _target.DidNotReceive().GetTableMetadata("account", "public", Arg.Any<CancellationToken>());
    }

    #endregion

    #region Target table discovery

    /// <summary>
    /// A SQLite Binding is correctly stored with a dot-free full name (e.g. "account"). Listing tables must
    /// build each candidate's full name the exact same way a Binding stores it, or a bound table looks free.
    /// </summary>
    [Fact]
    public async Task GetTargetTables_AgainstASqliteTarget_ReportsADotFreeBoundTableAsBound() {
        WithStoredEngine(TargetDatabaseEngine.Sqlite, available: true);
        _target.Engine.Returns(TargetDatabaseEngine.Sqlite);
        _target.GetSchemaMetadata(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new TableMetadata { SchemaName = null, TableName = "account", Columns = [], Constraints = [] }]);

        var boundBinding = Binding();
        boundBinding.DbSchemaFullName = "account";
        _meta.GetCachedSchemas(Arg.Any<CancellationToken>()).Returns([boundBinding]);

        var tables = await NewService().GetTargetTablesAsync(null, Ct);

        var account = Assert.Single(tables, t => t.TableName == "account");
        Assert.Equal("account", account.FullName);
        Assert.Equal(Entity, account.BoundEntityName);
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
    public async Task SetKeyMapping_ToAColumnWithoutAUniqueConstraint_IsRejectedAndNothingIsSaved() {
        ArrangeValidBinding();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetKeyMappingAsync(BindingId,
            new SetKeyMappingDTO { TargetColumnName = "phone" }, Ct));

        Assert.Contains("unique constraint or primary key", ex.Message, StringComparison.OrdinalIgnoreCase);
        await _meta.DidNotReceive().ReplaceFieldMappings(Arg.Any<int>(), Arg.Any<IEnumerable<MappedField>>());
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

    [Fact]
    public async Task ValidateProposed_ChecksTheProposedSetInPlaceOfTheStoredOne() {
        ArrangeValidBinding();

        var result = await NewService().ValidateProposedFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [new FieldMappingDTO { SalesforceFieldName = "Some_Date_Time__c", TargetColumnName = "employee_count" }]
        }, Ct);

        Assert.False(result.CanActivate);
        Assert.Equal("Error", Assert.Single(result.Results, r => r.SalesforceFieldName == "Some_Date_Time__c").Level);
        // The stored Phone mapping is not part of the proposal, so it is not checked.
        Assert.DoesNotContain(result.Results, r => r.SalesforceFieldName == "Phone");
    }

    [Fact]
    public async Task ValidateProposed_KeepsTheStoredKeyMapping() {
        ArrangeValidBinding();

        var result = await NewService().ValidateProposedFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "phone" }]
        }, Ct);

        Assert.True(result.CanActivate);
        Assert.Contains(result.Results, r => r.TargetColumnName == "sf_id");
    }

    [Fact]
    public async Task ValidateProposed_ThatWouldBreakAnActiveBinding_WritesNothing() {
        ArrangeValidBinding(BindingState.Active);

        await NewService().ValidateProposedFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [new FieldMappingDTO { SalesforceFieldName = "Some_Date_Time__c", TargetColumnName = "employee_count" }]
        }, Ct);

        await _meta.DidNotReceive().ReplaceFieldMappings(Arg.Any<int>(), Arg.Any<IEnumerable<MappedField>>());
        await _meta.DidNotReceive().ForceBindingIncomplete(Arg.Any<int>(), Arg.Any<DateTime>());
        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
        _signal.DidNotReceive().Signal();
    }

    [Fact]
    public async Task ValidateProposed_WithTwoFieldsOnOneColumn_CannotActivateAndNamesTheColumn() {
        // Saving this set would be refused, so validating it must not say it is ready.
        ArrangeValidBinding();

        var result = await NewService().ValidateProposedFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [
                new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "phone" },
                new FieldMappingDTO { SalesforceFieldName = "Fax", TargetColumnName = "phone" }
            ]
        }, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Blockers, b => b.Contains("'phone'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateProposed_MappingAFieldToTheKeyMappingColumn_CannotActivate() {
        ArrangeValidBinding();

        var result = await NewService().ValidateProposedFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [
                new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "phone" },
                new FieldMappingDTO { SalesforceFieldName = "Name", TargetColumnName = "sf_id" }
            ]
        }, Ct);

        Assert.False(result.CanActivate);
        Assert.Contains(result.Blockers, b => b.Contains("'sf_id'", StringComparison.Ordinal));
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
    public async Task SetFieldMappings_OnAnActiveBindingThatNowFailsValidation_ForcesItIncomplete() {
        // Saving an incompatible mapping is allowed — the user may be mid-edit — but an Active Binding must
        // not go on claiming to work. It is forced back to Incomplete, not Inactive: Inactive records only the
        // user's own choice, and a forced Binding has to show as needing attention.
        ArrangeValidBinding(BindingState.Active);

        var binding = await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [
                // A DateTime into an integer column cannot succeed.
                new FieldMappingDTO { SalesforceFieldName = "Some_Date_Time__c", TargetColumnName = "employee_count" }
            ]
        }, Ct);

        Assert.Equal(nameof(BindingState.Incomplete), binding.State);
        Assert.True(binding.NeedsAttention);
        await _meta.Received(1).ForceBindingIncomplete(BindingId, _time.GetUtcNow().UtcDateTime);
        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
    }

    [Fact]
    public async Task SetKeyMapping_OnAnActiveBindingWhoseTableChangedUnderIt_ForcesItIncomplete() {
        ArrangeValidBinding(BindingState.Active);
        ArrangeColumnDropped("phone");

        var binding = await NewService().SetKeyMappingAsync(BindingId,
            new SetKeyMappingDTO { TargetColumnName = "sf_id" }, Ct);

        Assert.Equal(nameof(BindingState.Incomplete), binding.State);
        await _meta.Received(1).ForceBindingIncomplete(BindingId, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task SetSoftDelete_OnAnActiveBindingWhoseTableChangedUnderIt_ForcesItIncomplete() {
        ArrangeValidBinding(BindingState.Active);
        ArrangeColumnDropped("phone");

        var binding = await NewService().SetSoftDeleteAsync(BindingId,
            new SetSoftDeleteDTO { Enabled = true, ColumnName = "is_deleted" }, Ct);

        Assert.Equal(nameof(BindingState.Incomplete), binding.State);
        await _meta.Received(1).ForceBindingIncomplete(BindingId, Arg.Any<DateTime>());
    }

    [Theory]
    [InlineData(BindingState.Inactive)]
    [InlineData(BindingState.Incomplete)]
    public async Task SetFieldMappings_ThatBreakABindingThatIsNotActive_LeavesItsStateAlone(BindingState state) {
        ArrangeValidBinding(state);

        var binding = await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [
                new FieldMappingDTO { SalesforceFieldName = "Some_Date_Time__c", TargetColumnName = "employee_count" }
            ]
        }, Ct);

        Assert.Equal(state.ToString(), binding.State);
        await _meta.DidNotReceive().ForceBindingIncomplete(Arg.Any<int>(), Arg.Any<DateTime>());
        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
    }

    [Fact]
    public async Task GetBinding_ForcedBackToIncomplete_NeedsAttention() {
        ArrangeValidBinding();
        var forced = Binding();
        forced.ForcedIncompleteAt = _time.GetUtcNow().UtcDateTime;
        _meta.GetSchemaById(BindingId).Returns(forced);

        var binding = await NewService().GetBindingAsync(BindingId, Ct);

        Assert.True(binding.NeedsAttention);
    }

    /// <summary>The Target Table as it is after a DBA dropped one of its mapped columns.</summary>
    private void ArrangeColumnDropped(string columnName) {
        var table = AccountTable();
        table.Columns = table.Columns.Where(c => c.ColumnName != columnName).ToList();
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(table);
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
    public async Task SoftDeleteColumns_AreExactlyTheColumnsSetSoftDeleteAccepts() {
        ArrangeValidBinding();
        var service = NewService();

        var offered = (await service.GetSoftDeleteColumnsAsync(BindingId, Ct)).ToHashSet();

        var accepted = new HashSet<string>();
        foreach (var column in AccountTable().Columns) {
            try {
                await service.SetSoftDeleteAsync(BindingId,
                    new SetSoftDeleteDTO { Enabled = true, ColumnName = column.ColumnName }, Ct);
                accepted.Add(column.ColumnName);
            } catch (ValidationException) {
                // Not a column that can carry the flag.
            }
        }

        Assert.Contains("is_deleted", offered);
        Assert.DoesNotContain("name", offered);
        Assert.Equal(accepted.Order(), offered.Order());
    }

    [Fact]
    public async Task SoftDeleteColumns_WhenNoColumnCanCarryTheFlag_IsEmpty() {
        ArrangeValidBinding();
        var table = AccountTable();
        table.Columns = table.Columns.Where(c => c.DataType != "boolean" && c.DataType != "integer").ToList();
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(table);

        var offered = await NewService().GetSoftDeleteColumnsAsync(BindingId, Ct);

        Assert.Empty(offered);
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

    [Theory]
    [InlineData(null, false)]
    [InlineData(ConnectionState.Incomplete, false)]
    [InlineData(ConnectionState.Failed, false)]
    [InlineData(ConnectionState.Connected, true)]
    public async Task GetSubscriptionPlan_CarriesTheTargetConnectionState_AndStreamsOnlyWhenConnected(
        ConnectionState? state, bool expectedHasTargetDatabase) {
        ArrangePrimaryChannel(Binding(BindingState.Active));
        _targetConnections.GetAsync(Arg.Any<CancellationToken>()).Returns(state is null
            ? null
            : new TargetConnection { Engine = TargetDatabaseEngine.Postgres, ConnectionState = state.Value });

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Equal(state, plan.TargetConnectionState);
        Assert.Equal(expectedHasTargetDatabase, plan.HasTargetDatabase);
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
    public async Task GetSubscriptionPlan_DemotesAnActiveBindingWhoseKeyMappingColumnIsNotUnique() {
        ArrangeValidBinding(BindingState.Active);
        ArrangePrimaryChannel(Binding(BindingState.Active));
        var table = AccountTable();
        // A DBA dropped the constraint after the Binding was activated.
        table.Columns.Single(c => c.ColumnName == "sf_id").ColumnConstraints.Clear();
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(table);

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Empty(plan.ActiveBindingsBySchemaId);
        await _meta.Received(1).ForceBindingIncomplete(BindingId, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetSubscriptionPlan_KeepsAnActiveBindingWhoseKeyMappingColumnIsUnique() {
        ArrangeValidBinding(BindingState.Active);
        ArrangePrimaryChannel(Binding(BindingState.Active));

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Contains("SCHEMA_V1", plan.ActiveBindingsBySchemaId.Keys);
        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
        await _meta.DidNotReceive().ForceBindingIncomplete(Arg.Any<int>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetSubscriptionPlan_KeepsActiveBindings_WhenTheTargetDatabaseCannotBeRead() {
        ArrangeValidBinding(BindingState.Active);
        ArrangePrimaryChannel(Binding(BindingState.Active));
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>())
            .Returns<TableMetadata?>(_ => throw new TimeoutException("unreachable"));

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        // Nothing is known to be wrong with the Binding, so it is not demoted; the worker's own failure
        // handling deals with a database that cannot be reached.
        Assert.Contains("SCHEMA_V1", plan.ActiveBindingsBySchemaId.Keys);
        await _meta.DidNotReceive().SetBindingState(Arg.Any<int>(), Arg.Any<BindingState>());
        await _meta.DidNotReceive().ForceBindingIncomplete(Arg.Any<int>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetSubscriptionPlan_CarriesThePrimaryChannelsId_SoTheWorkerKnowsWhereToSaveCheckpoints() {
        ArrangePrimaryChannel(Binding(BindingState.Active));

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Equal(ChannelId, plan.ChannelId);
    }

    [Fact]
    public async Task GetSubscriptionPlan_ResumesAfterTheChannelsCheckpoint_WhenItHasOne() {
        var checkpoint = new Checkpoint { ChannelId = ChannelId, ReplayId = [1, 2, 3], SavedAt = DateTime.UtcNow };
        ArrangePrimaryChannel(Binding(BindingState.Active), StartingPoint.Earliest);
        _checkpoints.GetAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(checkpoint);

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        // The Starting Point plays no part once a Checkpoint exists.
        Assert.Equal(StartFrom.Resume, plan.StartPosition.From);
        Assert.Same(checkpoint, plan.StartPosition.Checkpoint);
    }

    [Theory]
    [InlineData(StartingPoint.Latest, StartFrom.Latest)]
    [InlineData(StartingPoint.Earliest, StartFrom.Earliest)]
    public async Task GetSubscriptionPlan_WithoutACheckpoint_StartsAtTheChannelsStartingPoint(
        StartingPoint startingPoint, StartFrom expected) {
        ArrangePrimaryChannel(Binding(BindingState.Active), startingPoint);
        _checkpoints.GetAsync(ChannelId, Arg.Any<CancellationToken>()).Returns((Checkpoint?)null);

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Equal(expected, plan.StartPosition.From);
        Assert.Null(plan.StartPosition.Checkpoint);
    }

    [Fact]
    public async Task SetPrimaryChannel_OnAPlatformEventChannel_IsRejected() {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel("event"));

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SetPrimaryChannelAsync(new SetPrimaryChannelDTO { ChannelId = ChannelId }, Ct));

        await _channels.DidNotReceive().SetPrimaryChannelAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPrimaryChannel_OnAChannelThatDoesNotExist_IsNotFound() {
        _channels.GetChannelByIdAsync(77, Arg.Any<CancellationToken>()).Returns((PlatformEventChannelEntity?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => NewService().SetPrimaryChannelAsync(new SetPrimaryChannelDTO { ChannelId = 77 }, Ct));
    }

    [Fact]
    public async Task ClearPrimaryChannel_StopsStreaming_KeepsTheCheckpoint_AndTellsTheWorker() {
        await NewService().ClearPrimaryChannelAsync(Ct);

        await _channels.Received(1).ClearPrimaryChannelAsync(Arg.Any<CancellationToken>());
        await _checkpoints.DidNotReceiveWithAnyArgs().DiscardAsync(default, default, default);
        await _meta.DidNotReceiveWithAnyArgs().SetBindingState(default, default);
        _signal.Received().Signal();
    }

    [Fact]
    public async Task SetPrimaryChannel_TellsTheWorkerToRePlan() {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel());

        await NewService().SetPrimaryChannelAsync(new SetPrimaryChannelDTO { ChannelId = ChannelId }, Ct);

        _signal.Received().Signal();
    }

    [Fact]
    public async Task GetSubscriptionPlan_WithoutACheckpoint_StartsFromAOneTimeRestartChoice_OverTheStartingPoint() {
        ArrangePrimaryChannel(Binding(BindingState.Active), StartingPoint.Latest);
        var channel = await _channels.GetPrimaryChannelAsync(Ct);
        channel!.RestartFrom = StartingPoint.Earliest;

        var plan = await NewService().GetSubscriptionPlanAsync(Ct);

        Assert.Equal(StartFrom.Earliest, plan.StartPosition.From);
    }

    #region Switching the Primary Channel with a Checkpoint

    private Checkpoint ArrangeCheckpoint(TimeSpan age) {
        var checkpoint = new Checkpoint {
            ChannelId = ChannelId, ReplayId = [1, 2, 3], SavedAt = _time.GetUtcNow().UtcDateTime - age
        };
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel());
        _checkpoints.GetAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(checkpoint);
        return checkpoint;
    }

    [Fact]
    public async Task SetPrimaryChannel_WithACheckpoint_ResumesFromItByDefault() {
        ArrangeCheckpoint(TimeSpan.FromHours(5));

        await NewService().SetPrimaryChannelAsync(new SetPrimaryChannelDTO { ChannelId = ChannelId }, Ct);

        await _channels.Received(1).SetPrimaryChannelAsync(ChannelId, Arg.Any<CancellationToken>());
        await _checkpoints.DidNotReceiveWithAnyArgs().DiscardAsync(default, default, default);
    }

    [Fact]
    public async Task SetPrimaryChannel_Resume_WithACheckpointOlderThan72Hours_IsRefusedAndSaysWhy() {
        ArrangeCheckpoint(TimeSpan.FromHours(73));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SetPrimaryChannelAsync(
            new SetPrimaryChannelDTO { ChannelId = ChannelId, Start = "Resume" }, Ct));

        Assert.Contains("72 hours", ex.Message, StringComparison.Ordinal);
        await _channels.DidNotReceive().SetPrimaryChannelAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Earliest", StartingPoint.Earliest)]
    [InlineData("latest", StartingPoint.Latest)]
    public async Task SetPrimaryChannel_EarliestOrLatest_DiscardsTheCheckpointAndStartsThereOnce(
        string start, StartingPoint expected) {
        // Expired, so choosing somewhere else is exactly what the user has to do.
        ArrangeCheckpoint(TimeSpan.FromHours(100));

        await NewService().SetPrimaryChannelAsync(new SetPrimaryChannelDTO { ChannelId = ChannelId, Start = start }, Ct);

        await _checkpoints.Received(1).DiscardAsync(ChannelId, expected, Arg.Any<CancellationToken>());
        await _channels.Received(1).SetPrimaryChannelAsync(ChannelId, Arg.Any<CancellationToken>());
        // The one-time choice leaves the Channel's own Starting Point alone.
        await _channels.DidNotReceiveWithAnyArgs().SetStartingPointAsync(default, default, default);
    }

    [Fact]
    public async Task SetPrimaryChannel_WithoutACheckpoint_IgnoresTheChoiceAndUsesTheStartingPoint() {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel());
        _checkpoints.GetAsync(ChannelId, Arg.Any<CancellationToken>()).Returns((Checkpoint?)null);

        await NewService().SetPrimaryChannelAsync(new SetPrimaryChannelDTO { ChannelId = ChannelId, Start = "Earliest" }, Ct);

        await _checkpoints.DidNotReceiveWithAnyArgs().DiscardAsync(default, default, default);
        await _channels.Received(1).SetPrimaryChannelAsync(ChannelId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPrimaryChannel_WithAStartThatIsNotAChoice_IsRejected() {
        ArrangeCheckpoint(TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SetPrimaryChannelAsync(
            new SetPrimaryChannelDTO { ChannelId = ChannelId, Start = "Yesterday" }, Ct));

        await _channels.DidNotReceive().SetPrimaryChannelAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(73, false)]
    public async Task GetChannelStart_ReportsTheCheckpointsAge_AndWhetherItCanStillBeResumedFrom(int ageHours, bool canResume) {
        var checkpoint = ArrangeCheckpoint(TimeSpan.FromHours(ageHours));

        var start = await NewService().GetChannelStartAsync(ChannelId, Ct);

        Assert.True(start.HasCheckpoint);
        Assert.Equal(checkpoint.SavedAt, start.CheckpointSavedAt);
        Assert.Equal(canResume, start.CanResume);
        Assert.Equal(nameof(StartingPoint.Latest), start.StartingPoint);
    }

    [Fact]
    public async Task GetChannelStart_WithoutACheckpoint_SaysSo() {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel());

        var start = await NewService().GetChannelStartAsync(ChannelId, Ct);

        Assert.False(start.HasCheckpoint);
        Assert.False(start.CanResume);
        Assert.Null(start.CheckpointSavedAt);
    }

    #endregion

    #region Starting Point

    [Fact]
    public async Task SetStartingPoint_StoresItOnTheChannel() {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel());

        await NewService().SetStartingPointAsync(ChannelId, new SetStartingPointDTO { StartingPoint = "Earliest" }, Ct);

        await _channels.Received(1).SetStartingPointAsync(ChannelId, StartingPoint.Earliest, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Resume")]
    [InlineData("Soon")]
    [InlineData("")]
    public async Task SetStartingPoint_ToAnythingButEarliestOrLatest_IsRejected(string startingPoint) {
        _channels.GetChannelByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(Channel());

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SetStartingPointAsync(
            ChannelId, new SetStartingPointDTO { StartingPoint = startingPoint }, Ct));

        await _channels.DidNotReceiveWithAnyArgs().SetStartingPointAsync(default, default, default);
    }

    [Fact]
    public async Task SetStartingPoint_OnAChannelThatDoesNotExist_IsNotFound() {
        _channels.GetChannelByIdAsync(77, Arg.Any<CancellationToken>()).Returns((PlatformEventChannelEntity?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => NewService().SetStartingPointAsync(
            77, new SetStartingPointDTO { StartingPoint = "Earliest" }, Ct));
    }

    #endregion

    private void ArrangePrimaryChannel(CDCSchema binding, StartingPoint startingPoint = StartingPoint.Latest) {
        var channel = Channel(isPrimary: true);
        channel.StartingPoint = startingPoint;
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
