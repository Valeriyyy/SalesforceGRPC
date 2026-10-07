<!-- The Target Connection page. With nothing stored it is the engine picker and its form; once something is stored it
     is a summary with Verify and Edit, and, when Bindings exist, a danger zone to repoint. The server decides the phase
     and whether the identity is locked (TargetConnectionView); the page holds only the mode on top. Every action goes
     through api/targetconnection and reloads on success, so the page and the sidebar come from one read. -->
<script lang="ts">
    import { Info, ShieldAlert } from "@lucide/svelte";
    import { postJson, putJson, type ApiFailure } from "../../lib/api/client";
    import type { TargetConnectionView } from "../../lib/types/views";
    import EngineForm from "./EngineForm.svelte";
    import RepointDialog from "./RepointDialog.svelte";
    import Summary from "./Summary.svelte";
    import type { SaveTargetConnection, TargetConnectionResult } from "./engines";

    let { page }: { page: TargetConnectionView } = $props();

    type Mode = "viewing" | "editing" | "repointing";
    type Action = "save" | "verify";

    let mode = $state<Mode>("viewing");
    let pending = $state<Action | null>(null);
    let failed = $state<{ action: Action; failure: ApiFailure } | null>(null);
    let repoint: RepointDialog;

    const blocked = $derived(page.secretProtection.status !== "ready");
    const failureOf = (action: Action) => failed?.action === action ? failed.failure : null;

    /**
     * A save always stores, and a 200 whose proof failed is still a failure: the form stays open with the error and
     * everything typed. Only a proved connection reloads.
     */
    async function save(request: SaveTargetConnection) {
        pending = "save";
        failed = null;
        const result = await putJson<TargetConnectionResult>("/api/targetconnection", request);
        if (result.ok && result.value.connectionState === "Connected") {
            location.reload();
            return;
        }

        pending = null;
        failed = {
            action: "save",
            failure: !result.ok ? result.failure
                : result.value.lastError ? { kind: "database", message: result.value.lastError.message, rawResponse: result.value.lastError.rawResponse }
                : { kind: "message", message: "Saved, but the connection could not be proved." }
        };
    }

    async function verify() {
        pending = "verify";
        failed = null;
        // A Verify that fails is recorded too, so the reloaded summary shows it.
        const result = await postJson<TargetConnectionResult>("/api/targetconnection/verify");
        if (result.ok) {
            location.reload();
            return;
        }

        pending = null;
        failed = { action: "verify", failure: result.failure };
    }

    function setMode(next: Mode) {
        mode = next;
        failed = null;
    }
</script>

<header class="mb-6 max-w-5xl">
    <h1 class="h3">Target Connection</h1>
    <p class="mt-1 text-surface-600-400">The database change events are written into. Its tables must already exist; this application never creates them.</p>
</header>

<div class="max-w-5xl">
    {#if blocked}
        <div class="card mb-6 flex gap-3 border border-error-500 bg-error-50-950 p-4 text-sm" role="alert">
            <ShieldAlert class="size-6 shrink-0 text-error-600-400" />
            <div class="space-y-1">
                <p class="font-semibold">
                    {page.secretProtection.status === "unreadable" ? "The stored password can't be decrypted" : "This application can't store a database password yet"}
                </p>
                <p>{page.secretProtection.guidance}</p>
                <p class="text-xs text-surface-600-400">Protecting key: {page.secretProtection.protectingKey}</p>
            </div>
        </div>
    {/if}

    {#if !page.orgConnectionOk}
        <div class="card preset-tonal-surface mb-6 flex gap-3 p-4 text-sm">
            <Info class="mt-0.5 size-5 shrink-0" />
            <p>You can set this up now. No events will reach it until the <a class="anchor" href="/org-connection">Org Connection</a> is working.</p>
        </div>
    {/if}

    {#if !page.connection}
        {#if blocked}
            <p class="text-sm text-surface-600-400">The connection can't be saved until this application can store its password — see above.</p>
        {:else}
            <EngineForm {page} mode="new" pending={pending === "save"} error={failureOf("save")} onsubmit={save} />
        {/if}
    {:else if mode === "viewing" || blocked}
        <Summary {page} connection={page.connection} canEdit={!blocked} pending={pending === "verify"} error={failureOf("verify")}
            onverify={verify} onedit={() => setMode("editing")} />

        {#if page.identityLocked && !blocked}
            <section class="card mt-10 border border-error-500/50 p-5">
                <h2 class="h6 text-error-600-400">Point at a different database</h2>
                <p class="mb-3 mt-1 text-sm">
                    Switching engine, host or database destroys all {page.bindings} Binding{page.bindings === 1 ? "" : "s"} and their
                    {page.fieldMappings} Field Mapping{page.fieldMappings === 1 ? "" : "s"}. They were built against this database's
                    tables and type rules, so they can't be carried over.
                </p>
                <button type="button" class="btn btn-sm preset-outlined-error-500" onclick={() => setMode("repointing")}>
                    Point at a different database…
                </button>
            </section>
        {/if}
    {:else}
        <h2 class="h5 mb-4">{mode === "repointing" ? "Point at a different database" : "Edit the Target Connection"}</h2>
        {#key mode}
            <EngineForm {page} mode={mode === "repointing" ? "repoint" : "edit"} pending={pending === "save"} error={failureOf("save")}
                onsubmit={mode === "repointing" ? request => repoint.open(request) : save} oncancel={() => setMode("viewing")} />
        {/key}
    {/if}
</div>

<RepointDialog bind:this={repoint} {page} />
