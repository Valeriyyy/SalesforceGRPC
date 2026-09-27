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
