// PROTOTYPE — throwaway. Hand-written mirror of the view classes in .scratch/ui-shell/spec.md.
export type Stage = "orgConnection" | "primaryChannel" | "targetConnection" | "bindings";
export type StageStatus = "waiting" | "toDo" | "needsAttention" | "ok";
export type NavKey = "overview" | "orgConnection" | "channels" | "targetConnection" | "bindings";

export interface NavItemView {
    key: NavKey;
    label: string;
    href: string;
    stepNumber: number | null;
    status: StageStatus | null;
}

export interface ShellView {
    appName: string;
    currentNav: NavKey;
    nav: NavItemView[];
}

export interface StageView {
    stage: Stage;
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

export interface OverviewView {
    stages: StageView[];
    nextStep: Stage | null;
    allOk: boolean;
    workerStatus: null;
}

export interface PageView<TPage> {
    shell: ShellView;
    page: TPage;
}
