using Application.Bindings;
using Application.Connections;
using Application.Services;
using Application.Targets;
using Application.Writeback;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using Database.Writeback;
using DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Salesforce.Writeback;
using StarPush.Tests.Writeback;
using System.ComponentModel.DataAnnotations;

namespace StarPush.Tests;

/// <summary>
/// Choosing Two-way, driven through <see cref="IBindingService"/>: what the editor is offered, what saving
/// refuses and stores, and the plan the Writeback worker runs.
/// </summary>
/// <remarks>
/// The Avro fixture is the real AccountChangeEvent.avsc, so compound fields flatten the way Salesforce sends
/// them (BillingAddress + Street → BillingAddressStreet). The describe is hand-written to the REST API's shape.
/// </remarks>
public class BindingServiceWritebackTests {

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IMetaRepository _meta = Substitute.For<IMetaRepository>();
    private readonly IRepository _target = Substitute.For<IRepository>();
    private readonly ITargetConnectionProvider _targetConnections;
    private readonly ITargetEngineCatalog _engines = Substitute.For<ITargetEngineCatalog>();
    private readonly IPlatformEventChannelRepository _channels = Substitute.For<IPlatformEventChannelRepository>();
    private readonly IEntitySchemaProvider _entitySchemas = Substitute.For<IEntitySchemaProvider>();
    private readonly IConfigurationChangeSignal _signal = Substitute.For<IConfigurationChangeSignal>();
    private readonly ISObjectDescriber _describer = Substitute.For<ISObjectDescriber>();
    private readonly IWritebackFailureRepository _failures = Substitute.For<IWritebackFailureRepository>();
    private readonly ISalesforceRecordWriter _salesforce = Substitute.For<ISalesforceRecordWriter>();
    private readonly FakeWritebackCapability _capability = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

    private const int BindingId = 42;
    private const string Entity = "AccountChangeEvent";

    private static readonly WritebackEndpoints Endpoints = new("Host=db;Username=app", "Host=db;Username=repl");

    private List<MappedField> _mappings = [];
    private TableMetadata _table = AccountTable();
    private CDCSchema _binding = Binding();

    public BindingServiceWritebackTests() {
        _targetConnections = TargetProviders.Of(_target);
        WithEngine(TargetDatabaseEngine.Postgres, _capability);
        WithReplicationCredential(ConnectionState.Connected);

        _target.Engine.Returns(TargetDatabaseEngine.Postgres);
        _target.GetTableMetadata("account", "salesforce", Arg.Any<CancellationToken>()).Returns(_ => _table);
        _meta.GetSchemaById(BindingId).Returns(_ => _binding);
        _meta.GetEntityMappedFieldsBySchemaId(BindingId).Returns(_ => _mappings);
        _meta.GetWritebackDefaults(BindingId).Returns([]);
        _entitySchemas.GetSchemaForEntityAsync(Entity, Arg.Any<CancellationToken>()).Returns(AvroSchema());
        _channels.GetMembersByBindingIdAsync(BindingId, Arg.Any<CancellationToken>()).Returns([]);
        _describer.DescribeAsync("Account", Arg.Any<CancellationToken>()).Returns(AccountDescribe());
        _failures.ListAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns([]);

        _mappings = [
            Map("MappedSFKey", "sf_id"),
            Map("Phone", "phone"),
            Map("AnnualRevenue", "annual_revenue"),
            Map("BillingAddressStreet", "billing_street"),
            Map("LastModifiedDate", "last_modified"),
            Map("Fax", "fax_digits")
        ];
    }

    private BindingService NewService() =>
        new(_meta, Substitute.For<IAvroSchemaRepository>(), _targetConnections, _engines, _channels, _entitySchemas,
            _signal, Substitute.For<IOrgConnectionProvider>(), Substitute.For<ICheckpointRepository>(), _describer,
            _failures, new WritebackProcessor(_salesforce, _failures, _time, NullLogger<WritebackProcessor>.Instance),
            _time, NullLogger<BindingService>.Instance);

    #region Arrangement

    private void WithEngine(TargetDatabaseEngine engine, IWritebackCapability? writeback) {
        var profile = Substitute.For<ITargetEngineProfile>();
        profile.Engine.Returns(engine);
        profile.IsAvailable.Returns(true);
        profile.Writeback.Returns(writeback);
        _engines.For(engine).Returns(profile);
        _targetConnections.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => new TargetConnection {
            Engine = engine, ConnectionState = ConnectionState.Connected,
            ReplicationUsername = "repl", ReplicationPasswordEncrypted = "cipher", ReplicationState = ConnectionState.Connected
        });
    }

    private void WithReplicationCredential(ConnectionState? state) {
        _targetConnections.GetAsync(Arg.Any<CancellationToken>()).Returns(new TargetConnection {
            Engine = TargetDatabaseEngine.Postgres, ConnectionState = ConnectionState.Connected,
            ReplicationUsername = state is null ? null : "repl",
            ReplicationPasswordEncrypted = state is null ? null : "cipher",
            ReplicationState = state ?? ConnectionState.Incomplete
        });
        _targetConnections.GetWritebackEndpointsAsync(Arg.Any<CancellationToken>())
            .Returns(state is null ? null : Endpoints);
    }

    private static MappedField Map(string field, string column, FieldDirection direction = FieldDirection.TwoWay) =>
        new() { SchemaId = BindingId, SalesforceFieldName = field, TargetFieldName = column, FieldDirection = direction };

    private static DbAvroSchema AvroSchema() => new() {
        Id = 9, SchemaId = "SCHEMA_V1", RecordName = Entity, DateCreated = DateTime.UtcNow,
        SchemaJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "avro", "AccountChangeEvent.avsc"))
    };

    private static ColumnMetadata Col(string name, string dataType, bool unique = false, int? maxLength = null,
        bool generated = false, int? precision = null, int? scale = null) {
        var column = new ColumnMetadata {
            ColumnName = name, DataType = dataType, IsNullable = true, MaxLength = maxLength, IsGenerated = generated,
            NumericPrecision = precision, NumericScale = scale
        };
        if (unique) {
            column.ColumnConstraints.Add(new ColumnConstraint { ConstraintType = "UNIQUE", ConstraintName = $"{name}_key" });
        }
        return column;
    }

    private static TableMetadata AccountTable() => new() {
        SchemaName = "salesforce", TableName = "account", Constraints = [],
        Columns = [
            Col("id", "integer", unique: true),
            Col("sf_id", "character varying", unique: true, maxLength: 18),
            Col("phone", "character varying", maxLength: 40),
            Col("annual_revenue", "numeric", precision: 18, scale: 2),
            Col("billing_street", "text"),
            Col("last_modified", "timestamp with time zone"),
            Col("fax_digits", "text", generated: true),
            Col("region", "text")
        ]
    };

    private static CDCSchema Binding(SyncDirection direction = SyncDirection.Inbound,
        BindingState state = BindingState.Active, string? keyColumn = null, string? keyField = null) => new() {
        Id = BindingId, EntityName = Entity, DbSchemaFullName = "salesforce.account", BindingState = state,
        SyncDirection = direction, WritebackKeyColumn = keyColumn, WritebackKeySalesforceField = keyField,
        AvroSchema = AvroSchema()
    };

    private static SObjectField Field(string name, string type, bool writable = true, bool nillable = true,
        bool externalId = false, bool unique = false, int length = 0, int precision = 0, int scale = 0,
        string? compound = null, bool defaulted = false) => new() {
        Name = name, Label = name, Type = type, Updateable = writable, Createable = writable, Nillable = nillable,
        ExternalId = externalId, Unique = unique, Length = length, Precision = precision, Scale = scale,
        CompoundFieldName = compound, DefaultedOnCreate = defaulted
    };

    private static SObjectDescribe AccountDescribe() => new() {
        Name = "Account",
        Fields = [
            Field("Id", "id", writable: false, nillable: false, defaulted: true),
            Field("Name", "string", nillable: false, length: 255),
            Field("Phone", "phone", length: 40),
            Field("Fax", "phone", length: 40),
            Field("AnnualRevenue", "currency", precision: 18, scale: 2),
            Field("BillingStreet", "textarea", length: 255, compound: "BillingAddress"),
            Field("LastModifiedDate", "datetime", writable: false, nillable: false, defaulted: true),
            Field("Row_Key__c", "string", externalId: true, unique: true, length: 50),
            Field("Legacy_Key__c", "string", externalId: true, unique: false, length: 50)
        ]
    };

    private static SetSyncDirectionDTO TwoWay(string? keyColumn = "id", string? keyField = "Row_Key__c",
        List<WritebackDefaultDTO>? defaults = null, List<FieldDirectionDTO>? directions = null) => new() {
        SyncDirection = "TwoWay", WritebackKeyColumn = keyColumn, WritebackKeySalesforceField = keyField,
        WritebackDefaults = defaults ?? [new WritebackDefaultDTO { SalesforceFieldName = "Name", Value = "Unnamed" }],
        FieldDirections = directions ?? []
    };

    #endregion

    #region What the editor is offered

    [Fact]
    public async Task TheSetup_ForcesEachFieldsDirectionByWhatEachSideCanAccept() {
        var setup = await NewService().GetWritebackSetupAsync(BindingId, Ct);

        var fields = setup.Fields.ToDictionary(f => f.SalesforceFieldName);

        Assert.Equal("TwoWay", fields["Phone"].Direction);
        Assert.False(fields["Phone"].Forced);

        // Salesforce writes LastModifiedDate itself.
        Assert.Equal("InboundOnly", fields["LastModifiedDate"].Direction);
        Assert.True(fields["LastModifiedDate"].Forced);
        Assert.NotNull(fields["LastModifiedDate"].Reason);

        // The database generates fax_digits itself.
        Assert.Equal("WritebackOnly", fields["Fax"].Direction);
        Assert.True(fields["Fax"].Forced);
        Assert.Contains("generate", fields["Fax"].Reason, StringComparison.OrdinalIgnoreCase);

        // A compound child is written under the name the REST API uses.
        Assert.Equal("BillingStreet", fields["BillingAddressStreet"].RestFieldName);
        Assert.Equal("TwoWay", fields["BillingAddressStreet"].Direction);
    }

    [Fact]
    public async Task TheSetup_OffersOnlyUniqueColumnsAndUniqueExternalIdFields_AsTheWritebackKey() {
        var setup = await NewService().GetWritebackSetupAsync(BindingId, Ct);

        Assert.True(setup.CanBeTwoWay);
        // sf_id is unique too, but it is the Key Mapping.
        Assert.Equal(["id"], setup.KeyColumnCandidates);
        // Legacy_Key__c is an External ID but not Unique, so an upsert on it could match two records.
        Assert.Equal(["Row_Key__c"], setup.ExternalIdFields);
    }

    [Fact]
    public async Task TheSetup_ListsTheFieldsARecordCannotBeCreatedWithout() {
        var setup = await NewService().GetWritebackSetupAsync(BindingId, Ct);

        var required = Assert.Single(setup.RequiredFields);
        Assert.Equal("Name", required.SalesforceFieldName);
        Assert.False(required.SuppliedByMapping);
        Assert.Null(required.DefaultValue);
    }

    [Fact]
    public async Task OnAnEngineWithoutWriteback_TwoWayIsShownButCannotBeChosen() {
        WithEngine(TargetDatabaseEngine.SqlServer, writeback: null);

        var setup = await NewService().GetWritebackSetupAsync(BindingId, Ct);

        Assert.False(setup.CanBeTwoWay);
        Assert.Equal("Writeback is not yet supported for SqlServer.", setup.UnavailableReason);
    }

    [Fact]
    public async Task OnAServerTooOldForWriteback_TwoWayCannotBeChosen_AndSaysInboundStillWorks() {
        var tooOld = Substitute.For<IWritebackCapability>();
        tooOld.GetUnavailableReasonAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Postgres 15 is too old for Two-way; Inbound still works.");
        WithEngine(TargetDatabaseEngine.Postgres, tooOld);
        WithReplicationCredential(ConnectionState.Connected);

        var setup = await NewService().GetWritebackSetupAsync(BindingId, Ct);

        Assert.False(setup.CanBeTwoWay);
        Assert.Contains("too old", setup.UnavailableReason);
    }

    #endregion

    #region Saving Two-way

    [Fact]
    public async Task SwitchingToTwoWay_IsRefusedWithEveryUnmetPrerequisiteAtOnce_EachWithItsSql() {
        _capability.Prerequisites.Add(new WritebackPrerequisite("wal_level is replica; Writeback needs logical.",
            "ALTER SYSTEM SET wal_level = logical; -- then restart Postgres"));

        var refused = await Assert.ThrowsAsync<WritebackRefusedException>(() =>
            NewService().SetSyncDirectionAsync(BindingId, TwoWay(keyColumn: null), Ct));

        Assert.Contains(refused.Issues, i => i.Message.Contains("wal_level") && i.Sql!.Contains("ALTER SYSTEM"));
        Assert.Contains(refused.Issues, i => i.Message.Contains("Writeback Key column"));
        await _meta.DidNotReceiveWithAnyArgs().SetWriteback(default, default, default, default);
    }

    [Fact]
    public async Task SwitchingToTwoWay_WithoutAProvedReplicationCredential_IsRefused() {
        WithReplicationCredential(state: null);

        var refused = await Assert.ThrowsAsync<WritebackRefusedException>(() =>
            NewService().SetSyncDirectionAsync(BindingId, TwoWay(), Ct));

        Assert.Contains(refused.Issues, i => i.Message.Contains("Replication Credential"));
    }

    [Theory]
    [InlineData("region", "Row_Key__c", "unique")]          // the column is not unique
    [InlineData("sf_id", "Row_Key__c", "Key Mapping")]       // the column already holds the Salesforce ID
    [InlineData("id", "Legacy_Key__c", "Unique")]            // an upsert could match two records
    [InlineData("id", "Phone", "External ID")]               // not an External ID at all
    public async Task AWritebackKeyThatCannotIdentifyARowExactlyOnce_IsRefused(string column, string field, string expected) {
        var refused = await Assert.ThrowsAsync<WritebackRefusedException>(() =>
            NewService().SetSyncDirectionAsync(BindingId, TwoWay(column, field), Ct));

        Assert.Contains(refused.Issues, i => i.Message.Contains(expected));
    }

    [Fact]
    public async Task AWritebackKeyWhoseExternalIdFieldIsAlsoMapped_IsRefused_SoTheTwoDirectionsNeverFightOverIt() {
        _mappings.Add(Map("JigsawCompanyId", "region"));
        var describe = AccountDescribe();
        describe.Fields.Add(Field("JigsawCompanyId", "string", externalId: true, unique: true, length: 20));
        _describer.DescribeAsync("Account", Arg.Any<CancellationToken>()).Returns(describe);

        var refused = await Assert.ThrowsAsync<WritebackRefusedException>(() =>
            NewService().SetSyncDirectionAsync(BindingId, TwoWay("id", "JigsawCompanyId"), Ct));

        Assert.Contains(refused.Issues, i => i.Message.Contains("JigsawCompanyId") && i.Message.Contains("Field Mapping"));
    }

    [Fact]
    public async Task AWritebackKeyColumnThatAlsoReceivesAFieldInbound_IsRefused() {
        _mappings.Add(Map("AccountNumber", "id"));

        var refused = await Assert.ThrowsAsync<WritebackRefusedException>(() =>
            NewService().SetSyncDirectionAsync(BindingId, TwoWay(), Ct));

        Assert.Contains(refused.Issues, i => i.Message.Contains("'id'") && i.Message.Contains("inbound"));
    }

    [Fact]
    public async Task AWritebackDefaultThatIsNotAValueOfItsFieldsType_IsRefused() {
        var refused = await Assert.ThrowsAsync<WritebackRefusedException>(() =>
            NewService().SetSyncDirectionAsync(BindingId, TwoWay(defaults: [
                new WritebackDefaultDTO { SalesforceFieldName = "AnnualRevenue", Value = "lots" }
            ]), Ct));

        Assert.Contains(refused.Issues, i => i.Message.Contains("AnnualRevenue") && i.Message.Contains("lots"));
    }

    [Fact]
    public async Task AValidTwoWaySetup_IsStored_WithForcedDirectionsAndTheDefaults() {
        _binding = Binding(state: BindingState.Inactive);

        var saved = await NewService().SetSyncDirectionAsync(BindingId, TwoWay(directions: [
            new FieldDirectionDTO { SalesforceFieldName = "AnnualRevenue", Direction = "InboundOnly" }
        ]), Ct);

        await _meta.Received(1).SetWriteback(BindingId, SyncDirection.TwoWay, "id", "Row_Key__c");
        await _meta.Received(1).ReplaceFieldMappings(BindingId, Arg.Is<IEnumerable<MappedField>>(m =>
            m.Single(x => x.SalesforceFieldName == "AnnualRevenue").FieldDirection == FieldDirection.InboundOnly &&
            m.Single(x => x.SalesforceFieldName == "Fax").FieldDirection == FieldDirection.WritebackOnly &&
            m.Single(x => x.SalesforceFieldName == "Phone").FieldDirection == FieldDirection.TwoWay &&
            // Forced Inbound only by Salesforce, but the user's own choice is what is kept, so it comes back once
            // the field can be written.
            m.Single(x => x.SalesforceFieldName == "LastModifiedDate").FieldDirection == FieldDirection.TwoWay &&
            m.Any(x => x.SalesforceFieldName == "MappedSFKey")));
        await _meta.Received(1).ReplaceWritebackDefaults(BindingId, Arg.Is<IEnumerable<WritebackDefault>>(d =>
            d.Single().SalesforceFieldName == "Name" && d.Single().Value == "Unnamed"));
        _signal.Received().Signal();
        Assert.Equal("TwoWay", saved.SyncDirection);
    }

    [Fact]
    public async Task ARequiredFieldNothingSupplies_IsAWarningNotARefusal_BecauseAFlowMayFillIt() {
        _binding = Binding(state: BindingState.Inactive);

        await NewService().SetSyncDirectionAsync(BindingId, TwoWay(defaults: []), Ct);

        await _meta.Received(1).SetWriteback(BindingId, SyncDirection.TwoWay, "id", "Row_Key__c");
    }

    [Fact]
    public async Task SwitchingBackToInbound_KeepsTheFieldDirectionsAndTheWritebackKey() {
        _binding = Binding(SyncDirection.TwoWay, BindingState.Active, "id", "Row_Key__c");
        _mappings[2] = Map("AnnualRevenue", "annual_revenue", FieldDirection.InboundOnly);

        var saved = await NewService().SetSyncDirectionAsync(BindingId, new SetSyncDirectionDTO { SyncDirection = "Inbound" }, Ct);

        await _meta.Received(1).SetWriteback(BindingId, SyncDirection.Inbound, "id", "Row_Key__c");
        await _meta.DidNotReceiveWithAnyArgs().ReplaceFieldMappings(default, default!);
        Assert.Equal("Inbound", saved.SyncDirection);
    }

    [Fact]
    public async Task AnUnknownSyncDirection_IsRefused() {
        await Assert.ThrowsAsync<ValidationException>(() =>
            NewService().SetSyncDirectionAsync(BindingId, new SetSyncDirectionDTO { SyncDirection = "Sideways" }, Ct));
    }

    [Fact]
    public async Task EditingTheFieldMappings_KeepsEachFieldsDirection() {
        _binding = Binding(SyncDirection.TwoWay, BindingState.Inactive, "id", "Row_Key__c");
        _mappings[2] = Map("AnnualRevenue", "annual_revenue", FieldDirection.InboundOnly);
        _mappings[5] = Map("Fax", "fax_digits", FieldDirection.WritebackOnly);

        await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [
                new FieldMappingDTO { SalesforceFieldName = "Phone", TargetColumnName = "phone" },
                new FieldMappingDTO { SalesforceFieldName = "AnnualRevenue", TargetColumnName = "annual_revenue" },
                new FieldMappingDTO { SalesforceFieldName = "Fax", TargetColumnName = "fax_digits" }
            ]
        }, Ct);

        await _meta.Received(1).ReplaceFieldMappings(BindingId, Arg.Is<IEnumerable<MappedField>>(m =>
            m.Single(x => x.SalesforceFieldName == "AnnualRevenue").FieldDirection == FieldDirection.InboundOnly &&
            m.Single(x => x.SalesforceFieldName == "Fax").FieldDirection == FieldDirection.WritebackOnly &&
            m.Single(x => x.SalesforceFieldName == "Phone").FieldDirection == FieldDirection.TwoWay));
    }

    [Fact]
    public async Task ANewMappingOntoAGeneratedColumnOfATwoWayBinding_IsWritebackOnly_SoTheInboundFlowNeverWritesIt() {
        _binding = Binding(SyncDirection.TwoWay, BindingState.Inactive, "id", "Row_Key__c");
        _mappings = [Map("MappedSFKey", "sf_id")];

        await NewService().SetFieldMappingsAsync(BindingId, new SetFieldMappingsDTO {
            Mappings = [new FieldMappingDTO { SalesforceFieldName = "Fax", TargetColumnName = "fax_digits" }]
        }, Ct);

        await _meta.Received(1).ReplaceFieldMappings(BindingId, Arg.Is<IEnumerable<MappedField>>(m =>
            m.Single(x => x.SalesforceFieldName == "Fax").FieldDirection == FieldDirection.WritebackOnly));
    }

    #endregion

    #region The plan

    private void ArrangeTwoWayBinding() {
        _binding = Binding(SyncDirection.TwoWay, BindingState.Active, "id", "Row_Key__c");
        _meta.GetCachedSchemas(Arg.Any<CancellationToken>()).Returns(_ => [_binding]);
        _meta.GetWritebackDefaults(BindingId).Returns([
            new WritebackDefault { BindingId = BindingId, SalesforceFieldName = "Name", Value = "Unnamed" }
        ]);
        _mappings[2] = Map("AnnualRevenue", "annual_revenue", FieldDirection.InboundOnly);
    }

    [Fact]
    public async Task ThePlan_CarriesOnlyFieldsWhoseDirectionCarriesWriteback_UnderTheirRestNames() {
        ArrangeTwoWayBinding();

        var plan = await NewService().GetWritebackPlanAsync(Ct);

        Assert.True(plan.CanRun);
        Assert.Same(_capability, plan.Capability);
        Assert.Equal(Endpoints, plan.Endpoints);

        var binding = Assert.Single(plan.Bindings);
        Assert.Equal("Account", binding.SourceObject);
        Assert.Equal(new WritebackTable("salesforce", "account"), binding.Table);
        Assert.Equal("sf_id", binding.KeyMappingColumn);
        Assert.Equal(new WritebackField("id", "Row_Key__c", "string"), binding.WritebackKey);
        Assert.Equal([
            new WritebackField("phone", "Phone", "phone"),
            new WritebackField("billing_street", "BillingStreet", "textarea"),
            new WritebackField("fax_digits", "Fax", "phone")
        ], binding.Fields);
        Assert.Equal([new WritebackDefaultValue("Name", "Unnamed")], binding.Defaults);
    }

    [Fact]
    public async Task ThePlan_LeavesOutInboundAndInactiveBindings() {
        _binding = Binding(SyncDirection.Inbound, BindingState.Active, "id", "Row_Key__c");
        var inactive = Binding(SyncDirection.TwoWay, BindingState.Inactive, "id", "Row_Key__c");
        _meta.GetCachedSchemas(Arg.Any<CancellationToken>()).Returns([_binding, inactive]);

        var plan = await NewService().GetWritebackPlanAsync(Ct);

        Assert.False(plan.HasWork);
    }

    [Fact]
    public async Task ATwoWayBindingThatNoLongerMeetsWritebacksRequirements_IsForcedBackToIncomplete() {
        ArrangeTwoWayBinding();
        // A DBA dropped the unique constraint the Writeback Key relies on.
        _table.Columns[0] = Col("id", "integer");

        var plan = await NewService().GetWritebackPlanAsync(Ct);

        Assert.Empty(plan.Bindings);
        await _meta.Received(1).ForceBindingIncomplete(BindingId, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task ATwoWayBindingThatCannotBeCheckedRightNow_IsKept_ButNotRunThisRound() {
        ArrangeTwoWayBinding();
        _describer.DescribeAsync("Account", Arg.Any<CancellationToken>())
            .Returns<SObjectDescribe?>(_ => throw new SalesforceTransientException("Salesforce answered 503"));

        var plan = await NewService().GetWritebackPlanAsync(Ct);

        Assert.Empty(plan.Bindings);
        await _meta.DidNotReceiveWithAnyArgs().ForceBindingIncomplete(default, default);
    }

    [Fact]
    public async Task ThePlan_SaysWhyItCannotRun_WhenTheReplicationCredentialIsGone() {
        ArrangeTwoWayBinding();
        WithReplicationCredential(state: null);

        var plan = await NewService().GetWritebackPlanAsync(Ct);

        Assert.True(plan.HasWork);
        Assert.False(plan.CanRun);
        Assert.Contains("Replication Credential", plan.BlockedReason);
    }

    #endregion

    #region Inbound Bindings and generated columns

    [Fact]
    public async Task AnInboundBindingMappingAFieldOntoAGeneratedColumn_CannotBeActivated() {
        _binding = Binding(state: BindingState.Incomplete);

        var validation = await NewService().ValidateBindingAsync(BindingId, Ct);

        Assert.False(validation.CanActivate);
        Assert.Contains(validation.Results, r => r.TargetColumnName == "fax_digits" && r.Level == "Error");
    }

    #endregion

    #region Retry and Dismiss

    private const int FailureId = 5;

    private void ArrangeFailure(string? keyValue = "7", string? salesforceId = null) {
        ArrangeTwoWayBinding();
        _failures.GetAsync(FailureId, Arg.Any<CancellationToken>()).Returns(new WritebackFailure {
            Id = FailureId, BindingId = BindingId, RowKey = WritebackFailure.RowKeyFor(keyValue, salesforceId),
            WritebackKeyValue = keyValue, SalesforceId = salesforceId, Operation = WritebackOperation.Create,
            StatusCodes = "FIELD_CUSTOM_VALIDATION_EXCEPTION", Message = "Phone must be E.164"
        });
    }

    [Fact]
    public async Task Retry_SendsTheRowsCurrentValues_AndClearsTheFailureWhenSalesforceAcceptsThem() {
        ArrangeFailure();
        _target.GetRowByKey("salesforce.account", "id", "7", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, object?> { ["id"] = 7, ["sf_id"] = null, ["phone"] = "+15550100" });
        _salesforce.UpsertAsync("Account", "Row_Key__c", Arg.Any<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(),
            Arg.Any<CancellationToken>()).Returns([new RecordSaveResult { Success = true, Created = true, Id = "001000000000009AAA" }]);

        var outcome = await NewService().RetryWritebackFailureAsync(FailureId, Ct);

        Assert.Equal("Succeeded", outcome.Outcome);
        await _salesforce.Received(1).UpsertAsync("Account", "Row_Key__c",
            Arg.Is<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(r => (string?)r.Single()["Phone"] == "+15550100"),
            Arg.Any<CancellationToken>());
        await _failures.Received(1).DeleteAsync(FailureId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_OfARowThatHasSinceGone_DiscardsTheFailureAndSendsNothing() {
        ArrangeFailure();
        _target.GetRowByKey("salesforce.account", "id", "7", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<string, object?>?)null);

        var outcome = await NewService().RetryWritebackFailureAsync(FailureId, Ct);

        Assert.Equal("Discarded", outcome.Outcome);
        Assert.Empty(_salesforce.ReceivedCalls());
        await _failures.Received(1).DeleteAsync(FailureId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_OfARowKnownOnlyByItsSalesforceId_ReadsItByTheKeyMappingColumn() {
        ArrangeFailure(keyValue: null, salesforceId: "001000000000001AAA");
        _target.GetRowByKey("salesforce.account", "sf_id", "001000000000001AAA", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, object?> { ["id"] = null, ["sf_id"] = "001000000000001AAA", ["phone"] = "1" });
        _salesforce.UpdateAsync("Account", Arg.Any<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(), Arg.Any<CancellationToken>())
            .Returns([new RecordSaveResult { Success = true, Id = "001000000000001AAA" }]);

        var outcome = await NewService().RetryWritebackFailureAsync(FailureId, Ct);

        Assert.Equal("Succeeded", outcome.Outcome);
    }

    [Fact]
    public async Task Retry_ThatSalesforceStillRefuses_ReportsWhyAndKeepsTheFailure() {
        ArrangeFailure();
        _target.GetRowByKey("salesforce.account", "id", "7", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, object?> { ["id"] = 7, ["sf_id"] = null, ["phone"] = "still bad" });
        _salesforce.UpsertAsync(default!, default!, default!, default).ReturnsForAnyArgs([new RecordSaveResult {
            Success = false, Errors = [new RecordError { StatusCode = "FIELD_CUSTOM_VALIDATION_EXCEPTION", Message = "Phone must be E.164" }]
        }]);

        var outcome = await NewService().RetryWritebackFailureAsync(FailureId, Ct);

        Assert.Equal("Failed", outcome.Outcome);
        Assert.Contains("E.164", outcome.Message);
        await _failures.DidNotReceive().DeleteAsync(FailureId, Arg.Any<CancellationToken>());
        await _failures.Received(1).RecordAsync(Arg.Any<WritebackFailure>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_WhenSalesforceCannotBeReached_SaysSo_AndLeavesTheFailureAsItWas() {
        ArrangeFailure();
        _target.GetRowByKey("salesforce.account", "id", "7", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, object?> { ["id"] = 7, ["sf_id"] = null, ["phone"] = "1" });
        _salesforce.UpsertAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs<IReadOnlyList<RecordSaveResult>>(_ => throw new SalesforceTransientException("Salesforce answered 503"));

        var outcome = await NewService().RetryWritebackFailureAsync(FailureId, Ct);

        Assert.Equal("Failed", outcome.Outcome);
        Assert.Contains("503", outcome.Message);
        await _failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        await _failures.DidNotReceive().DeleteAsync(FailureId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dismiss_RemovesTheFailure() {
        ArrangeFailure();

        await NewService().DismissWritebackFailureAsync(FailureId, Ct);

        await _failures.Received(1).DeleteAsync(FailureId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dismiss_OfAFailureThatDoesNotExist_IsNotFound() {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => NewService().DismissWritebackFailureAsync(99, Ct));
    }

    #endregion
}
