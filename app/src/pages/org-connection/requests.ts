// The bodies api/orgconnection takes and returns, beyond the page's own view model.

/** PUT api/orgconnection. */
export interface SaveOrgConnection {
    consumerKey: string;
    consumerSecret: string;
    administeringUsername: string;
    runAsUsername: string;
    isSandbox: boolean;
}

/** GET api/orgconnection/disconnect/preview. */
export interface DisconnectPreview {
    bindings: number;
    fieldMappings: number;
    avroSchemas: number;
    channels: number;
    channelMembers: number;
    targetConnection: string | null;
    leftInSalesforce: string[];
}

/** POST api/orgconnection/disconnect: the preview's counts, so a stale preview aborts instead of destroying more. */
export interface ConfirmDisconnect {
    expectedBindings: number;
    expectedFieldMappings: number;
    expectedTargetConnection: boolean;
}
