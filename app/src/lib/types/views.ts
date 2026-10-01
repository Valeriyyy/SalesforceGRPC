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
