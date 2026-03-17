using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database;
using Database.Models;

namespace SalesforceGrpc.Strategies;

public interface IEventStrategy {
    ChangeType ChangeType { get; }
    Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken);
}