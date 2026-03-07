using Avro;
using Avro.Generic;
using Avro.Specific;
using com.sforce.eventbus;
using Google.Protobuf;
using MediatR;
using System.Collections;

namespace SalesforceGrpc.Handlers;

public abstract record CDCEvent : IRequest
{
    public byte[]? AvroPayload { get; set; }
    public ByteString? LatestReplayId { get; set; }
    public string Name { get; set; }
    public Schema AvroSchema { get; set; }
}

public abstract record EventPayload : IRequest
{
    public ISpecificRecord ChangeEvent { get; set; }
    public string Name { get; set; }
}

// Generic Event Commands

public record CreateCommand : IRequest
{
    /// <summary>
    /// The avro GenericRecord
    /// </summary>
    public GenericRecord ChangeEvent { get; set; }
    /// <summary>
    /// The schema and the table name of the entity
    /// </summary>
    public string EntityName { get; set; }
}

public record UpdateCommand : IRequest
{
    public GenericRecord ChangeEvent { get; set; }
    public ChangeEventHeader ChangeEventHeader { get; set; }
    public string EntityName { get; set; }
}

public record DeleteCommand : IRequest
{
    public IList<string> RecordIds { get; set; }
    public GenericRecord ChangeEvent { get; set; }
    public string EntityName { get; set; }
}

public record UndeleteCommand : IRequest
{
    public IList<string> RecordIds { get; set; }
    public GenericRecord ChangeEvent { get; set; }
    public string EntityName { get; set; }
}

public record GenericCDCEventCommand() : CDCEvent;
