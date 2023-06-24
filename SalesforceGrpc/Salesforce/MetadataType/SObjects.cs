namespace SalesforceGrpc.Salesforce.MetadataType;

public class SObjectsa {
    public class PlatformEventChannelMember {
        public string? Id { get; set; }
        public string? DeveloperName { get; set; }
        public string? EventChannel { get; set; }
        public string? SelectedEntity { get; set; }
    }

    public class RestResponseRoot {
        public int size { get; set; }
        public int totalSize { get; set; }
        public bool done { get; set; }
        public object queryLocator { get; set; }
        public string entityTypeName { get; set; }
        public List<PlatformEventChannelMember> records { get; set; }
    }
}
