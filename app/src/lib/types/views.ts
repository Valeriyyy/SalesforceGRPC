// Hand-written mirror of the C# view classes in SalesforceGrpc/ViewModels (ADR 0006). Keep the two in step.
export type PipelineStage = "orgConnection" | "primaryChannel" | "targetConnection" | "bindings";
export type StageStatus = "waiting" | "toDo" | "needsAttention" | "ok";
export type NavKey = "overview" | "orgConnection" | "channels" | "targetConnection" | "bindings";

export interface PageView<TPage> {
    shell: ShellView;
    page: TPage;
}

export interface ShellView {
    appName: string;
    currentNav: NavKey;
    nav: NavItemView[];
}

export interface NavItemView {
    key: NavKey;
    label: string;
    href: string;
    stepNumber: number | null;
    status: StageStatus | null;
}

export interface OverviewView {
    stages: StageView[];
    nextStep: PipelineStage | null;
    allOk: boolean;
    workerStatus: WorkerStatusView | null;
}

export interface StageView {
    stage: PipelineStage;
    stepNumber: number;
    title: string;
    status: StageStatus;
    summary: string;
    problem: string | null;
    whatToDo: string | null;
    actionLabel: string | null;
    actionHref: string | null;
    waitingOn: string[];
}

/** Reserved: the worker does not report its runtime status yet, so this is always null. */
export type WorkerStatusView = Record<string, never>;

export interface PlaceholderView {
    title: string;
    description: string;
}

export type OrgConnectionPhase =
    | "notStarted" | "readyToApprove" | "needsConsumerSecret" | "approved" | "unverified" | "connected" | "failed";

export interface OrgConnectionView {
    phase: OrgConnectionPhase;
    details: OrgConnectionDetailsView | null;
    hasConsumerSecret: boolean;
    org: OrgView | null;
    callbackUrl: string;
    certificate: CertificateView | null;
    lastError: SalesforceErrorView | null;
    selfConfiguration: SelfConfigurationView | null;
    secretProtection: SecretProtectionView;
    notice: OrgConnectionNotice | null;
}

export interface OrgConnectionDetailsView {
    consumerKey: string;
    administeringUsername: string;
    runAsUsername: string;
    isSandbox: boolean;
}

export interface OrgView {
    orgId: string;
    orgUrl: string | null;
    lastConnectedAt: string | null;
}

export interface CertificateView {
    fingerprint: string;
    expiresAt: string | null;
    downloadHref: string;
}

export interface SalesforceErrorView {
    error: string;
    errorDescription: string;
    guidance: string | null;
    rawResponse: string;
    occurredAt: string | null;
}

export interface SelfConfigurationView {
    at: string;
    configured: boolean;
    summary: string;
    manualSteps: string[];
}

export type SecretProtectionStatus = "ready" | "notConfigured" | "unreadable";

export interface SecretProtectionView {
    status: SecretProtectionStatus;
    protectingKey: string;
    guidance: string | null;
}

export interface OrgConnectionNotice {
    kind: "success" | "error";
    message: string;
    error: SalesforceErrorView | null;
    orgMismatch: OrgMismatchView | null;
}

export interface OrgMismatchView {
    storedOrgId: string;
    discoveredOrgId: string;
}

// Channels pages (ChannelsViews.cs). Only Change Data Capture Channels ever appear.

export type StartingPoint = "latest" | "earliest";
export type BindingState = "incomplete" | "active" | "inactive";

export interface ChannelsView {
    orgConnectionReady: boolean;
    channels: ChannelRowView[];
    lastSyncedAt: string | null;
    notice: ChannelsNotice | null;
}

export interface ChannelRowView {
    id: number;
    label: string;
    fullName: string;
    isPrimary: boolean;
    memberCount: number;
    unboundCount: number;
    /** The Primary Channel's Checkpoint, or where it starts without one. Null for every other Channel. */
    streaming: string | null;
}

export interface ChannelView {
    orgConnectionReady: boolean;
    /** Null when there is no such Change Data Capture Channel. */
    channel: ChannelDetailView | null;
    notice: ChannelsNotice | null;
}

export interface ChannelDetailView {
    id: number;
    label: string;
    fullName: string;
    isPrimary: boolean;
    startingPoint: StartingPoint;
    checkpoint: CheckpointView | null;
    members: ChannelMemberView[];
}

export interface CheckpointView {
    savedAt: string;
    age: string;
    expiresIn: string;
    expired: boolean;
    canResume: boolean;
}

export interface ChannelMemberView {
    id: number;
    entity: string;
    filterExpression: string | null;
    enrichedFields: string[];
    binding: MemberBindingView | null;
}

export interface MemberBindingView {
    id: number;
    targetTable: string;
    state: BindingState;
}

export interface NewChannelView {
    orgConnectionReady: boolean;
    primaryChannelFullName: string | null;
    makePrimaryByDefault: boolean;
}

export interface ChannelsNotice {
    kind: "success" | "error";
    message: string;
    resync: ResyncReport | null;
}

/** Also the body of POST api/PlatformEventManagement/resync. */
export interface ResyncReport {
    channelsAdded: string[];
    channelsRemoved: string[];
    channelsRelabelled: string[];
    membersAdded: ResyncMemberChange[];
    membersRemoved: ResyncMemberChange[];
    primaryChannelRemoved: string | null;
    hasChanges: boolean;
}

export interface ResyncMemberChange {
    channel: string;
    selectedEntity: string;
    bindingSetInactive: string | null;
}

// Target Connection page (TargetConnectionView.cs).

export type TargetConnectionPhase = "notStarted" | "connected" | "incomplete" | "failed";

export interface TargetConnectionView {
    phase: TargetConnectionPhase;
    /** Null when nothing is stored (notStarted). */
    connection: TargetConnectionDetailsView | null;
    engines: EngineView[];
    /** What a repoint would destroy, echoed back as its confirmation. */
    bindings: number;
    fieldMappings: number;
    /** Bindings exist: engine, host, database name and file path change only through a repoint. */
    identityLocked: boolean;
    orgConnectionOk: boolean;
    secretProtection: SecretProtectionView;
}

/** Every stored detail but the password. */
export interface TargetConnectionDetailsView {
    /** The API's engine name ("Postgres", "SqlServer", ...), sent back unchanged on save. */
    engine: string;
    host: string | null;
    port: number | null;
    databaseName: string | null;
    username: string | null;
    filePath: string | null;
    options: Record<string, string>;
    hasPassword: boolean;
    lastConnectedAt: string | null;
    lastError: TargetDatabaseErrorView | null;
}

export interface TargetDatabaseErrorView {
    message: string;
    rawResponse: string;
    occurredAt: string | null;
}

export interface EngineView {
    engine: string;
    isAvailable: boolean;
    unavailableReason: string | null;
    fields: FieldView[];
}

export type FieldKindView = "string" | "int" | "bool" | "secret" | "choice";

export interface FieldView {
    name: string;
    label: string;
    kind: FieldKindView;
    required: boolean;
    default: string | null;
    choices: string[] | null;
}

// Bindings pages (BindingsViews.cs). Every page waits, showing only `waiting`, until a Primary Channel and a usable
// Target Connection exist.

/** Where a Binding stands. needsAttention: was Active, forced back to Incomplete by the worker or an edit. */
export type BindingRowState = "unbound" | "incomplete" | "needsAttention" | "active" | "inactive";

export interface SetupStepView {
    title: string;
    problem: string;
    actionLabel: string;
    href: string;
}

export interface BindingsView {
    waiting: SetupStepView[];
    primaryChannelLabel: string | null;
    rows: BindingRowView[];
}

export interface BindingRowView {
    memberId: number;
    entity: string;
    /** Null, with the details below, when the Entity is not bound. */
    bindingId: number | null;
    targetTable: string | null;
    keyMappingColumn: string | null;
    fieldMappingCount: number;
    fieldCount: number | null;
    state: BindingRowState;
    href: string;
}

export interface NewBindingView {
    waiting: SetupStepView[];
    /** Null when there is no such member on the Primary Channel. */
    member: NewBindingMemberView | null;
    tables: TargetTableOptionView[];
    targetError: TargetDatabaseErrorView | null;
}

export interface NewBindingMemberView {
    id: number;
    entity: string;
}

export interface TargetTableOptionView {
    schemaName: string | null;
    tableName: string;
    fullName: string;
    /** The Entity already bound to this table, which makes it unavailable. */
    boundEntity: string | null;
    nameMatches: boolean;
}

export interface BindingView {
    waiting: SetupStepView[];
    /** Null when there is no such Binding. */
    binding: BindingEditorView | null;
}

export interface BindingEditorView {
    id: number;
    entity: string;
    targetTable: string;
    state: Exclude<BindingRowState, "unbound">;
    /** The Primary Channel's member for this Entity, where binding again starts. */
    memberId: number | null;
    fieldMappingCount: number;
    keyMappingColumn: string | null;
    softDeleteEnabled: boolean;
    softDeleteColumnName: string | null;
    /** Null, with targetError set, when the Target Database could not be read. */
    target: BindingTargetView | null;
    targetError: TargetDatabaseErrorView | null;
}

export interface BindingTargetView {
    fields: BindableField[];
    columns: TargetColumn[];
    /** Exactly the columns the soft delete setting accepts. */
    softDeleteColumns: string[];
    /** Offered, never applied, while there is no Key Mapping. */
    suggestedKeyColumn: string | null;
    /** Name matches a Binding with no Field Mappings starts its draft from. */
    prefill: FieldMapping[];
    /** The stored Binding's validation. */
    validation: BindingValidation;
}

// The api/Bindings DTOs the Bindings pages carry and send (DTO/BindingResponses.cs, DTO/BindingRequests.cs).

export interface BindableField {
    /** Flattened: a compound's parts appear as e.g. BillingAddressCity, never the compound itself. */
    name: string;
    fieldType: string;
    avroType: string;
    isNullable: boolean;
    parentName: string | null;
    mappedColumnName: string | null;
    suggestedColumnName: string | null;
}

export interface TargetColumn {
    columnName: string;
    dataType: string;
    isNullable: boolean;
    maxLength: number | null;
    isUnique: boolean;
    isPrimaryKey: boolean;
    mappedSalesforceFieldName: string | null;
}

export interface FieldMapping {
    salesforceFieldName: string;
    targetColumnName: string;
}

export type CompatibilityLevel = "Compatible" | "Warning" | "Error";

export interface CompatibilityResult {
    /** "MappedSFKey" for the Key Mapping, "SoftDelete" for the flag column, "" for an unmapped NOT NULL column. */
    salesforceFieldName: string;
    targetColumnName: string;
    fieldType: string;
    targetDataType: string;
    level: CompatibilityLevel;
    message: string;
}

/** Also the answer of POST api/Bindings/{id}/validate. */
export interface BindingValidation {
    bindingId: number;
    canActivate: boolean;
    blockers: string[];
    results: CompatibilityResult[];
    validatedAgainstSchemaId: string | null;
}
