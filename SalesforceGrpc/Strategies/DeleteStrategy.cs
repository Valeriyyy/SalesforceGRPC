using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database;
using Database.Models;

namespace SalesforceGrpc.Strategies;

public class DeleteStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.DELETE;
    public Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        throw new NotImplementedException();
    }

    private readonly ILogger<DeleteStrategy> _logger;

    public DeleteStrategy(ILogger<DeleteStrategy> logger) {
        _logger = logger;
    }

    public Task ProcessEvent(byte[] payload, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        _logger.LogInformation("This is where the delete process event goes");
        return Task.CompletedTask;
    }
}