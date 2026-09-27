// PROTOTYPE — throwaway fixtures, one per Pipeline state the design must handle.
import type { OverviewView, PageView, ShellView, Stage, StageView } from "./views";

const href: Record<Stage, string> = {
    orgConnection: "/org-connection",
    primaryChannel: "/channels",
    targetConnection: "/target-connection",
    bindings: "/bindings"
};

function stage(stage: Stage, stepNumber: number, title: string, rest: Partial<StageView>): StageView {
    return {
        stage, stepNumber, title,
        status: "ok", summary: "", problem: null, whatToDo: null, actionLabel: null, actionHref: null, waitingOn: [],
        ...rest
    };
}

const waiting = (on: string[]): Partial<StageView> => ({
    status: "waiting",
    summary: `Waiting on ${on.join(" and ")}`,
    waitingOn: on
});

const org = (rest: Partial<StageView>) => stage("orgConnection", 1, "Org Connection", rest);
const channel = (rest: Partial<StageView>) => stage("primaryChannel", 2, "Primary Channel", rest);
const target = (rest: Partial<StageView>) => stage("targetConnection", 3, "Target Connection", rest);
const bindings = (rest: Partial<StageView>) => stage("bindings", 4, "Bindings", rest);

const orgOk = org({ summary: "Connected as integration@acme.com to acme.my.salesforce.com" });
const channelOk = channel({ summary: "SyncChannel__chn · 3 Channel Members · Checkpoint 12 min ago" });
const targetOk = target({ summary: "Postgres · db.internal:5432 / warehouse · proved 2 days ago" });
const bindingsOk = bindings({ summary: "5 Active · 0 Incomplete · 1 Inactive" });

function overview(stages: StageView[]): OverviewView {
    const next = stages.find(s => s.status !== "ok");
    return { stages, nextStep: next?.stage ?? null, allOk: !next, workerStatus: null };
}

export type FixtureKey = "fresh" | "half" | "allOk" | "degraded" | "degradedStrict";

export const fixtureLabels: Record<FixtureKey, string> = {
    fresh: "Fresh install",
    half: "Half set up",
    allOk: "All OK",
    degraded: "Degraded (set-up Stages keep their own status)",
    degradedStrict: "Degraded (strict Waiting propagation)"
};

const overviews: Record<FixtureKey, OverviewView> = {
    fresh: overview([
        org({
            status: "toDo",
            summary: "Not connected yet",
            problem: "There is no Org Connection yet.",
            whatToDo: "Create a Signing Keypair, install its certificate on a Connected App in Salesforce, then Connect.",
            actionLabel: "Set up Org Connection",
            actionHref: href.orgConnection
        }),
        channel(waiting(["Org Connection"])),
        target(waiting(["Org Connection"])),
        bindings(waiting(["Primary Channel", "Target Connection"]))
    ]),
    half: overview([
        orgOk,
        channelOk,
        target({
            status: "toDo",
            summary: "Not set up yet",
            problem: "There is no Target Connection yet, so events have nowhere to land.",
            whatToDo: "Enter the Target Database's engine and details. They are proved by reading schema metadata when you save.",
            actionLabel: "Set up Target Connection",
            actionHref: href.targetConnection
        }),
        bindings(waiting(["Target Connection"]))
    ]),
    allOk: overview([orgOk, channelOk, targetOk, bindingsOk]),
    degraded: overview([
        org({
            status: "needsAttention",
            summary: "Failed · last connected 3 days ago",
            problem: "The Org Connection has Failed: Salesforce stopped accepting its sign-in.",
            whatToDo: "Check that the Connected App is still installed, the Run-as User is active and the certificate has not expired, then Connect again.",
            actionLabel: "Repair Org Connection",
            actionHref: href.orgConnection
        }),
        channel({
            status: "needsAttention",
            summary: "SyncChannel__chn · 3 Channel Members · Checkpoint 61 h ago",
            problem: "The Checkpoint is 61 hours old and expires in 11 hours.",
            whatToDo: "The worker is not advancing it. Once it expires the worker restarts from Earliest. Repair the Org Connection first.",
            actionLabel: "Open Channels",
            actionHref: href.primaryChannel
        }),
        targetOk,
        bindings({
            status: "needsAttention",
            summary: "4 Active · 1 Incomplete · 1 Inactive",
            problem: "The worker forced Account back to Incomplete: its Key Mapping column has no unique constraint.",
            whatToDo: "Restore the unique constraint on the Key Mapping column, then reactivate the Binding.",
            actionLabel: "Open Bindings",
            actionHref: href.bindings
        })
    ]),
    degradedStrict: overview([
        org({
            status: "needsAttention",
            summary: "Failed · last connected 3 days ago",
            problem: "The Org Connection has Failed: Salesforce stopped accepting its sign-in.",
            whatToDo: "Check that the Connected App is still installed, the Run-as User is active and the certificate has not expired, then Connect again.",
            actionLabel: "Repair Org Connection",
            actionHref: href.orgConnection
        }),
        channel(waiting(["Org Connection"])),
        target(waiting(["Org Connection"])),
        bindings(waiting(["Primary Channel", "Target Connection"]))
    ])
};

function shell(page: OverviewView): ShellView {
    const status = (s: Stage) => page.stages.find(x => x.stage === s)!.status;
    return {
        appName: "SalesforceGRPC",
        currentNav: "overview",
        nav: [
            { key: "overview", label: "Overview", href: "/prototype/overview", stepNumber: null, status: null },
            { key: "orgConnection", label: "Org Connection", href: href.orgConnection, stepNumber: 1, status: status("orgConnection") },
            { key: "channels", label: "Channels", href: href.primaryChannel, stepNumber: 2, status: status("primaryChannel") },
            { key: "targetConnection", label: "Target Connection", href: href.targetConnection, stepNumber: 3, status: status("targetConnection") },
            { key: "bindings", label: "Bindings", href: href.bindings, stepNumber: 4, status: status("bindings") }
        ]
    };
}

export function fixture(key: FixtureKey): PageView<OverviewView> {
    const page = overviews[key];
    return { shell: shell(page), page };
}

export const fixtureKeys = Object.keys(overviews) as FixtureKey[];
