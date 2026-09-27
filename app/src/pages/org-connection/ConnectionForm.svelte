<!-- The connection details: plain Svelte state and the browser's own required/email checks; the server is the
     validator. Its values outlive a failed save, so nothing the user typed (the secret included) is lost. -->
<script lang="ts">
    import { untrack } from "svelte";
    import type { ApiFailure } from "../../lib/api";
    import SalesforceError from "../../lib/SalesforceError.svelte";
    import type { OrgConnectionDetailsView } from "../../lib/views";
    import type { SaveOrgConnection } from "./requests";

    /** new: nothing saved yet. edit: change saved details, secret optional. secretOnly: only the discarded secret. */
    type Mode = "new" | "edit" | "secretOnly";

    let { details, mode, pending, error, onsave, oncancel }: {
        details: OrgConnectionDetailsView | null;
        mode: Mode;
        pending: boolean;
        error: ApiFailure | null;
        onsave: (request: SaveOrgConnection) => void;
        oncancel?: () => void;
    } = $props();

    // Seeded once from the view model; the form owns them after that.
    const seed = untrack(() => details);
    let consumerKey = $state(seed?.consumerKey ?? "");
    let consumerSecret = $state("");
    let administeringUsername = $state(seed?.administeringUsername ?? "");
    let runAsUsername = $state(seed?.runAsUsername ?? "");
    let environment = $state(seed?.isSandbox ? "sandbox" : "production");

    function submit(e: SubmitEvent) {
        e.preventDefault();
        onsave({
            consumerKey,
            consumerSecret,
            administeringUsername,
            runAsUsername,
            isSandbox: environment === "sandbox"
        });
    }
</script>

<form class="space-y-4" onsubmit={submit}>
    {#if mode !== "secretOnly"}
        <fieldset class="space-y-2">
            <legend class="label-text mb-1">Salesforce environment</legend>
            <div class="flex flex-wrap gap-x-6 gap-y-2">
                <label class="flex items-center gap-2"><input class="radio" type="radio" bind:group={environment} value="production" /> Production or Developer Edition</label>
                <label class="flex items-center gap-2"><input class="radio" type="radio" bind:group={environment} value="sandbox" /> Sandbox</label>
            </div>
        </fieldset>

        <label class="label">
            <span class="label-text">Consumer Key</span>
            <input class="input font-mono text-xs" required bind:value={consumerKey} autocomplete="off" spellcheck="false" />
            <span class="text-xs text-surface-600-400">From the External Client App's OAuth settings in Salesforce. Not a secret.</span>
        </label>
    {/if}

    <label class="label">
        <span class="label-text">Consumer Secret{#if mode === "edit"} <span class="text-surface-600-400">(optional)</span>{/if}</span>
        <input class="input font-mono text-xs" type="password" required={mode !== "edit"} bind:value={consumerSecret} autocomplete="off" />
        <span class="text-xs text-surface-600-400">
            {#if mode === "edit"}Only needed to re-run the browser approval. Leave it blank to keep things as they are.
            {:else}From the same place as the Consumer Key. Used once, for the browser approval, then discarded.{/if}
        </span>
    </label>

    {#if mode !== "secretOnly"}
        <div class="grid gap-4 md:grid-cols-2">
            <label class="label">
                <span class="label-text">Administering User</span>
                <input class="input" type="email" required bind:value={administeringUsername} autocomplete="off" placeholder="admin@yourcompany.com" />
                <span class="text-xs text-surface-600-400">The Salesforce user who will approve in the next step. Used for setup work such as managing Channels.</span>
            </label>
            <label class="label">
                <span class="label-text">Run-as User</span>
                <input class="input" type="email" required bind:value={runAsUsername} autocomplete="off" placeholder="integration@yourcompany.com" />
                <span class="text-xs text-surface-600-400">Events are streamed as this user, so only what they can see ever reaches your database.</span>
            </label>
        </div>
    {/if}

    {#if error}<SalesforceError failure={error} />{/if}

    <div class="flex gap-2">
        <button type="submit" class="btn preset-filled-primary-500" disabled={pending}>
            {pending ? "Saving…" : mode === "new" ? "Save and continue" : "Save"}
        </button>
        {#if oncancel}<button type="button" class="btn preset-tonal" onclick={oncancel} disabled={pending}>Cancel</button>{/if}
    </div>
</form>
