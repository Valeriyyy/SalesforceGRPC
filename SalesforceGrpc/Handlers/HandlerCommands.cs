using Avro.Specific;
using Google.Protobuf;
using MediatR;

namespace SalesforceGrpc.Handlers;

public record EventWithId : IRequest {
    public string SchemaId { get; set; }
    public byte[] Avropayload { get; set; }
    public ByteString? LatestReplayId { get; set; }
}


public abstract record CDCEvent : IRequest {
    public byte[]? AvroPayload { get; set; }
    public ByteString? LatestReplayId { get; set; }
    public string Name { get; set; }
}

public abstract record EventPayload : IRequest {
    public ISpecificRecord ChangeEvent { get; set; }
    public string Name { get; set; }
}

// Generic Event Commands

public record CreateCommand : IRequest {
    /// <summary>
    /// The avro decoded change event payload
    /// </summary>
    public ISpecificRecord ChangeEvent { get; set; }
    /// <summary>
    /// The concrete dotnet 
    /// </summary>
    public Type ChangeEventType { get; set; }
    /// <summary>
    /// The schema and the table name of the entity
    /// </summary>
    public string Table { get; set; }
}

public record UpdateCommand : IRequest {
    public ISpecificRecord ChangeEvent { get; set; }
    public Type ChangeEventType { get; set; }
    public string Table { get; set; }
}

public record DeleteCommand : IRequest {
    public List<string> RecordIds { get; set; }
    public string Table { get; set; }
}

public record UndeleteCommand : IRequest {
    public List<string> RecordIds { get; set; }
    public string Table { get; set; }
}


// Account Events
public record AccountCDCEventCommand() : CDCEvent;
public record AccountCreateCommand() : EventPayload;
public record AccountUpdateCommand() : EventPayload;
public record AccountDeleteCommand() : EventPayload;
public record AccountUndeleteCommand() : EventPayload;

// Contact Events
public record ContactCDCEventCommand() : CDCEvent;
public record ContactCreateCommand() : EventPayload;
public record ContactUpdateCommand() : EventPayload;
public record ContactDeleteCommand() : EventPayload;
public record ContactUndeleteCommand() : EventPayload;


