using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database;
using Database.Models;

namespace SalesforceGrpc.Strategies;

public class UndeleteStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.UNDELETE;
    public Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        throw new NotImplementedException();
    }
}