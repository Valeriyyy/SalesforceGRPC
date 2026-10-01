<!-- The Org Connection page. While the connection is being set up it is a guided checklist of three steps; once it
     has worked it is a summary card with Verify and Edit. The server decides the phase (OrgConnectionView); every
     action goes through api/orgconnection and reloads on success, so the page and the sidebar come from one read. -->
<script lang="ts">
    import { Check, CircleCheck, CircleX, ExternalLink, Hourglass, Pencil } from "@lucide/svelte";
    import { postJson, putJson, type ApiFailure, type ApiResult } from "../../lib/api/client";
    import { when } from "../../lib/utils/format";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { OrgConnectionView } from "../../lib/types/views";
    import Alerts from "./Alerts.svelte";
    import ConnectionForm from "./ConnectionForm.svelte";
    import DisconnectDialog from "./DisconnectDialog.svelte";
    import ManualRegistration from "./ManualRegistration.svelte";
    import SalesforcePrep from "./SalesforcePrep.svelte";
    import type { SaveOrgConnection } from "./requests";

    let { page }: { page: OrgConnectionView } = $props();

    /** A plain link: the server redirects the browser to Salesforce, or back here with the reason it could not. */
    const connectHref = "/api/orgconnection/bootstrap/start";

    type Action = "save" | "verify";
    let pending = $state<Action | null>(null);
    let failed = $state<{ action: Action; failure: ApiFailure } | null>(null);
    let editing = $state(false);
    let askingForSecret = $state(false);
    let disconnect: DisconnectDialog;

    async function run(action: Action, request: () => Promise<ApiResult<unknown>>) {
        pending = action;
        failed = null;
        const result = await request();
        if (result.ok) {
            // Stays pending while the page reloads, so nothing can be pressed twice.
            location.reload();
            return;
        }
        pending = null;
        failed = { action, failure: result.failure };
    }

    const save = (request: SaveOrgConnection) => run("save", () => putJson("/api/orgconnection", request));
    const verify = () => run("verify", () => postJson("/api/orgconnection/verify"));
    const failureOf = (action: Action) => failed?.action === action ? failed.failure : null;

    const blocked = $derived(page.secretProtection.status !== "ready");
    const settingUp = $derived(page.phase === "notStarted" || page.phase === "readyToApprove"
        || page.phase === "needsConsumerSecret" || page.phase === "approved");
    const step2Active = $derived(page.phase === "notStarted" || page.phase === "needsConsumerSecret" || editing);
    const step3Active = $derived(!step2Active && (page.phase === "readyToApprove" || page.phase === "approved"));

    const summary = $derived({
        connected: { icon: CircleCheck, tone: "text-success-600-400", title: "Connected" },
        failed: { icon: CircleX, tone: "text-error-600-400", title: "Connection failed" },
        unverified: { icon: Hourglass, tone: "text-warning-600-400", title: "Changed, not verified yet" }
    }[page.phase as "connected" | "failed" | "unverified"]);
</script>

{#snippet marker(state: "done" | "active" | "upcoming", n: number)}
    <span class={["grid size-8 shrink-0 place-items-center rounded-full border-2 text-sm font-semibold",
        state === "done" ? "border-success-500 bg-success-500 text-white" :
        state === "active" ? "border-primary-500 text-primary-600-400" : "border-surface-300-700 text-surface-500"]}>
        {#if state === "done"}<Check class="size-4" />{:else}{n}{/if}
    </span>
{/snippet}

{#snippet details()}
    {#if page.details}
        <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
            {#if page.org}<dt class="text-surface-600-400">Org ID</dt><dd class="font-mono">{page.org.orgId}</dd>{/if}
            <dt class="text-surface-600-400">Environment</dt><dd>{page.details.isSandbox ? "Sandbox" : "Production"}</dd>
            <dt class="text-surface-600-400">Run-as User</dt><dd>{page.details.runAsUsername}</dd>
            <dt class="text-surface-600-400">Administering User</dt><dd>{page.details.administeringUsername}</dd>
            <dt class="text-surface-600-400">Consumer Key</dt><dd class="truncate font-mono text-xs leading-5">{page.details.consumerKey}</dd>
            {#if page.certificate && !settingUp}
                <dt class="text-surface-600-400">Signing Certificate</dt>
                <dd class="break-all font-mono text-xs leading-5">{page.certificate.fingerprint} · expires {when(page.certificate.expiresAt)}</dd>
            {/if}
        </dl>
    {/if}
{/snippet}

<header class="mb-6 max-w-4xl">
    <h1 class="h3">Org Connection</h1>
    <p class="mt-1 text-surface-600-400">How this application signs in to your Salesforce org. Set up once; it reconnects on its own after restarts.</p>
</header>

<div class="max-w-4xl">
    <Alerts secretProtection={page.secretProtection} notice={page.notice} />

    {#if settingUp}
        <ol>
            <li class="flex gap-4">
                <div class="flex flex-col items-center">
                    {@render marker(page.phase === "notStarted" ? "active" : "done", 1)}
                    <div class="w-px flex-1 bg-surface-300-700"></div>
                </div>
                <div class="min-w-0 flex-1 pb-8">
                    <h2 class="h5 leading-8">Prepare Salesforce</h2>
                    {#if page.phase === "notStarted"}
                        <p class="mb-3 text-sm text-surface-600-400">Three things in Salesforce Setup, before this application can do the rest.</p>
                        <SalesforcePrep callbackUrl={page.callbackUrl} />
                    {:else}
                        <details class="text-sm">
                            <summary class="cursor-pointer text-surface-600-400 hover:underline">Show the Salesforce steps again</summary>
                            <div class="mt-3"><SalesforcePrep callbackUrl={page.callbackUrl} /></div>
                        </details>
                    {/if}
                </div>
            </li>

            <li class="flex gap-4">
                <div class="flex flex-col items-center">
                    {@render marker(step2Active ? "active" : "done", 2)}
                    <div class="w-px flex-1 bg-surface-300-700"></div>
                </div>
                <div class="min-w-0 flex-1 pb-8">
                    <div class="flex items-center justify-between">
                        <h2 class="h5 leading-8">Enter connection details</h2>
                        {#if !step2Active && !blocked}
                            <button type="button" class="btn btn-sm hover:preset-tonal" onclick={() => editing = true}><Pencil class="size-4" /> Edit</button>
                        {/if}
                    </div>
                    {#if step2Active && blocked}
                        <p class="text-sm text-surface-600-400">These can't be saved until this application can store credentials — see above.</p>
                    {:else if step2Active}
                        {#if page.phase === "needsConsumerSecret" && !editing}
                            <p class="mb-3 text-sm">The Consumer Secret was discarded, so the browser approval can't run. Paste it again from the External Client App.</p>
                        {/if}
                        <div class="card border border-surface-200-800 bg-surface-50-950 p-5">
                            <ConnectionForm details={page.details}
                                mode={editing ? "edit" : page.phase === "needsConsumerSecret" ? "secretOnly" : "new"}
                                pending={pending === "save"} error={failureOf("save")} onsave={save}
                                oncancel={editing ? () => editing = false : undefined} />
                        </div>
                    {:else}
                        {@render details()}
                    {/if}
                </div>
            </li>

            <li class="flex gap-4">
                <div class="flex flex-col items-center">{@render marker(step3Active ? "active" : "upcoming", 3)}</div>
                <div class="min-w-0 flex-1 pb-4">
                    <h2 class="h5 leading-8">Approve in Salesforce</h2>
                    {#if step3Active && page.phase === "readyToApprove"}
                        <p class="mb-3 text-sm">
                            You'll be sent to Salesforce to sign in as <strong>{page.details?.administeringUsername}</strong> and approve this
                            application. It then configures what it can and brings you back here.
                        </p>
                        {#if !blocked}
                            <a class="btn preset-filled-primary-500" href={connectHref}>Connect to Salesforce <ExternalLink class="size-4" /></a>
                        {/if}
                    {:else if step3Active && page.phase === "approved"}
                        <p class="mb-3 text-sm">
                            Approval received, but Salesforce isn't accepting this application's sign-in yet. Finish anything listed below,
                            give Salesforce a minute to catch up, then verify.
                        </p>
                        {#if page.lastError}
                            <SalesforceError failure={{ kind: "salesforce", error: page.lastError }} title="Not working yet" class="mb-4" />
                        {/if}
                        {#if page.selfConfiguration && !page.selfConfiguration.configured}
                            <div class="card mb-4 border border-warning-500 bg-warning-50-950 p-4">
                                <ManualRegistration outcome={page.selfConfiguration} certificate={page.certificate} />
                            </div>
                        {/if}
                        {#if failureOf("verify")}<SalesforceError failure={failureOf("verify")!} class="mb-4" />{/if}
                        <div class="flex flex-wrap gap-2">
                            <button type="button" class="btn preset-filled-primary-500" onclick={verify} disabled={pending !== null}>
                                {pending === "verify" ? "Verifying…" : "Verify connection"}
                            </button>
                            {#if !blocked}<a class="btn preset-tonal" href={connectHref}>Approve again</a>{/if}
                        </div>
                    {:else}
                        <p class="text-sm text-surface-600-400">After your details are saved.</p>
                    {/if}
                </div>
            </li>
        </ol>

        <!-- Collapsed unless the Approved phase is already showing the steps left to do by hand, above. -->
        {#if !(page.phase === "approved" && page.selfConfiguration && !page.selfConfiguration.configured)}
            <details class="card mt-6 border border-surface-200-800 p-4">
                <summary class="cursor-pointer font-semibold">Can't use the browser approval?</summary>
                <div class="mt-3"><ManualRegistration outcome={page.selfConfiguration} certificate={page.certificate} /></div>
            </details>
        {/if}
    {:else}
        <section class={["card border p-6", page.phase === "failed" ? "border-error-500" : "border-surface-200-800 bg-surface-50-950"]}>
            <div class="mb-4 flex flex-wrap items-start justify-between gap-4">
                <div class="flex items-center gap-3">
                    <summary.icon class={["size-8 shrink-0", summary.tone]} />
                    <div>
                        <h2 class="h5">{summary.title}</h2>
                        <p class="text-sm text-surface-600-400">
                            {#if page.phase === "unverified"}
                                The details were changed. Verify to check they still sign in.
                            {:else}
                                {page.org?.orgUrl ?? "Org not known yet"} · last worked {when(page.org?.lastConnectedAt)}
                            {/if}
                        </p>
                    </div>
                </div>
                <div class="flex flex-wrap gap-2">
                    <button type="button" class={["btn", page.phase === "connected" ? "preset-tonal" : "preset-filled-primary-500"]}
                        onclick={verify} disabled={pending !== null}>
                        {pending === "verify" ? "Verifying…" : "Verify"}
                    </button>
                    {#if page.phase === "failed" && !blocked && !askingForSecret}
                        {#if page.hasConsumerSecret}
                            <a class="btn preset-tonal" href={connectHref}>Approve again</a>
                        {:else}
                            <button type="button" class="btn preset-tonal" onclick={() => { askingForSecret = true; editing = false; }}>Approve again</button>
                        {/if}
                    {/if}
                    {#if !blocked && !editing && !askingForSecret}
                        <button type="button" class="btn hover:preset-tonal" onclick={() => editing = true}><Pencil class="size-4" /> Edit</button>
                    {/if}
                </div>
            </div>

            {#if page.phase === "failed" && page.lastError}
                <SalesforceError failure={{ kind: "salesforce", error: page.lastError }} title="Salesforce is not accepting this application's sign-in" class="mb-4" />
            {/if}
            {#if failureOf("verify")}<SalesforceError failure={failureOf("verify")!} class="mb-4" />{/if}

            {#if editing}
                <ConnectionForm details={page.details} mode="edit" pending={pending === "save"} error={failureOf("save")}
                    onsave={save} oncancel={() => editing = false} />
            {:else if askingForSecret}
                <p class="mb-3 text-sm">The browser approval needs the Consumer Secret, which was discarded once the connection first worked. Paste it again from the External Client App.</p>
                <ConnectionForm details={page.details} mode="secretOnly" pending={pending === "save"} error={failureOf("save")}
                    onsave={save} oncancel={() => askingForSecret = false} />
            {:else}
                {@render details()}
            {/if}
        </section>

        {#if page.phase !== "connected"}
            <details class="card mt-6 border border-surface-200-800 p-4">
                <summary class="cursor-pointer font-semibold">Check the Salesforce side by hand</summary>
                <div class="mt-3"><ManualRegistration outcome={page.selfConfiguration} certificate={page.certificate} /></div>
            </details>
        {/if}
    {/if}

    {#if page.phase !== "notStarted"}
        <section id="disconnect" class="card mt-10 border border-error-500/50 p-5">
            <h2 class="h6 text-error-600-400">Disconnect</h2>
            <p class="mb-3 mt-1 text-sm">
                Reset this application to a fresh install, to point it at a different org. Destroys the Bindings, Field Mappings and,
                if one is set up, the Target Connection. Nothing in Salesforce is touched.
            </p>
            <button type="button" class="btn btn-sm preset-outlined-error-500" onclick={() => disconnect.open()}>Disconnect…</button>
        </section>
    {/if}
</div>

<DisconnectDialog bind:this={disconnect} orgId={page.org?.orgId ?? null} />
