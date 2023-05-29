namespace SalesforceGrpc.Database;
public class VendorProfileFieldMapping {
    public static IDictionary<string, string> _mappings = new Dictionary<string, string> {
        { "Name", "name" },
        { "Account__c", "account_id" },
        { "Status__c", "status" },
        { "GUID__c", "guid" },
        { "CreatedDate", "created_date" },
        { "LastModifiedDate", "last_modified_date" },
        { "DOT_Number__c", "dot_number" },
        { "Is_Carrier__c", "is_carrier" }
    };
}
