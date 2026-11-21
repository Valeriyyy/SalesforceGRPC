namespace Salesforce.Writeback;

/// <summary>Reads a Source Object's field list as the REST describe reports it.</summary>
public interface ISObjectDescriber {
    /// <summary>The describe of an object, e.g. <c>Account</c>, or null when the object does not exist.</summary>
    Task<SObjectDescribe?> DescribeAsync(string sObject, CancellationToken cancellationToken = default);
}
