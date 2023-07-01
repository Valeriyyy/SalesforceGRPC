namespace SalesforceGrpc.Salesforce.MetadataType;
internal class RecordType {
    public string Id { get; set; }
    public string Name { get; set; }
    public string SObjectType { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

internal class RecordTypeQueryResponse {
    public int TotalSize { get; set; }
    public bool Done { get; set; }
    public List<RecordType> Records { get; set; }
}
