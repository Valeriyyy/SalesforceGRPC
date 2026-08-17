using Avro;
using Avro.Generic;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SalesforceGrpc.Strategies;

namespace SalesforceGRPCTest;

/// <summary>
/// Whether a Salesforce delete removes the row or only marks it is the Binding's decision, and an undelete
/// can only be honoured when it was the latter.
/// </summary>
/// <remarks>
/// The ChangeEventHeader schema is lifted out of the committed AccountChangeEvent.avsc rather than loaded from
/// a separate fixture, so these tests need nothing that is not already in the repository.
/// </remarks>
public class SoftDeleteStrategyTests {

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IMetaRepository _meta = Substitute.For<IMetaRepository>();
    private readonly IRepository _target = Substitute.For<IRepository>();

    private const int BindingId = 1;
    private const string Table = "salesforce.account";
    private static readonly List<string> RecordIds = ["001000000000001AAA"];

    private static RecordSchema AccountSchema() =>
        (RecordSchema)Schema.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "avro", "AccountChangeEvent.avsc")));

    /// <summary>Builds a change event carrying just the header, which is all a delete or undelete needs.</summary>
    private static GenericRecord DeleteEvent() {
        var schema = AccountSchema();
        var headerSchema = (RecordSchema)schema["ChangeEventHeader"].Schema;

        var header = new GenericRecord(headerSchema);
        header.Add("entityName", "Account");
        header.Add("recordIds", RecordIds.Cast<object>().ToArray());

        var record = new GenericRecord(schema);
        record.Add("ChangeEventHeader", header);
        return record;
    }

    private CDCSchema Binding(bool softDelete, string? column) {
        _meta.GetCachedMapping(BindingId, Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string> {
            { "MappedSFKey", "sf_id" },
            { "Phone", "phone" }
        });

        return new CDCSchema {
            Id = BindingId, EntityName = "AccountChangeEvent", DbSchemaFullName = Table,
            BindingState = BindingState.Active, SoftDeleteEnabled = softDelete, SoftDeleteColumnName = column
        };
    }

    private DeleteStrategy NewDeleteStrategy() =>
        new(NullLogger<DeleteStrategy>.Instance, _target, _meta);

    private UndeleteStrategy NewUndeleteStrategy() =>
        new(NullLogger<UndeleteStrategy>.Instance, _target, _meta);

    [Fact]
    public async Task WithSoftDeleteOff_ADeleteRemovesTheRow() {
        var binding = Binding(softDelete: false, column: null);

        await NewDeleteStrategy().ProcessEvent(DeleteEvent(), AccountSchema(), binding, Ct);

        await _target.Received(1).Delete(Table, "sf_id", Arg.Is<List<string>>(ids => ids.SequenceEqual(RecordIds)));
        await _target.DidNotReceive().SoftDelete(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>());
    }

    [Fact]
    public async Task WithSoftDeleteOn_ADeleteOnlyMarksTheRow() {
        var binding = Binding(softDelete: true, column: "is_deleted");

        await NewDeleteStrategy().ProcessEvent(DeleteEvent(), AccountSchema(), binding, Ct);

        await _target.Received(1).SoftDelete(Table, "sf_id", "is_deleted",
            Arg.Is<List<string>>(ids => ids.SequenceEqual(RecordIds)));
        await _target.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>());
    }

    [Fact]
    public async Task WithSoftDeleteEnabledButNoColumn_ADeleteFallsBackToRemovingTheRow() {
        // A Binding cannot be activated in this shape, but a row already stored this way must not silently
        // update a column named null.
        var binding = Binding(softDelete: true, column: null);

        await NewDeleteStrategy().ProcessEvent(DeleteEvent(), AccountSchema(), binding, Ct);

        await _target.Received(1).Delete(Table, "sf_id", Arg.Any<List<string>>());
    }

    [Fact]
    public async Task WithSoftDeleteOn_AnUndeleteClearsTheFlag() {
        var binding = Binding(softDelete: true, column: "is_deleted");

        await NewUndeleteStrategy().ProcessEvent(DeleteEvent(), AccountSchema(), binding, Ct);

        await _target.Received(1).UnDelete(Table, "sf_id", "is_deleted",
            Arg.Is<List<string>>(ids => ids.SequenceEqual(RecordIds)));
    }

    [Fact]
    public async Task WithSoftDeleteOff_AnUndeleteWritesNothingBecauseTheRowIsGone() {
        // The event carries no field values, so there is nothing to rebuild a hard-deleted row from.
        var binding = Binding(softDelete: false, column: null);

        await NewUndeleteStrategy().ProcessEvent(DeleteEvent(), AccountSchema(), binding, Ct);

        await _target.DidNotReceive().UnDelete(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>());
        await _target.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
    }
}
