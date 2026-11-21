namespace StarPush;

/// <summary>
/// An event's Avro Schema, or the Binding it routes to, could not be looked up — Salesforce or the App Database did
/// not answer. A failure that may pass, not a problem with the event: the batch is replayed, never skipped.
/// </summary>
public sealed class SchemaUnavailableException : Exception {
    public SchemaUnavailableException(string schemaId, Exception inner)
        : base($"Could not look up Avro Schema {schemaId} for an incoming event: {inner.Message}.", inner) {
        SchemaId = schemaId;
    }

    public string SchemaId { get; }
}
