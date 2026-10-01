<!-- Disconnect: reads the preview when opened, lists what is destroyed here and what stays in Salesforce, and only
     confirms once the user has typed the org id. The confirmation is built from the preview it showed, so a Binding
     created in the meantime makes the server refuse rather than destroy something the user never saw. -->
<script lang="ts">
    import { TriangleAlert } from "@lucide/svelte";
    import { getJson, postJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { ConfirmDisconnect, DisconnectPreview } from "./requests";

    let { orgId }: { orgId: string | null } = $props();

    let dialog: HTMLDialogElement;
    let preview = $state<DisconnectPreview | null>(null);
    let failure = $state<ApiFailure | null>(null);
    let typed = $state("");
    let pending = $state(false);

    const word = $derived(orgId ?? "disconnect");

    export async function open() {
        preview = null;
        failure = null;
        typed = "";
        dialog.showModal();

        const result = await getJson<DisconnectPreview>("/api/orgconnection/disconnect/preview");
        if (result.ok) {
            preview = result.value;
        } else {
            failure = result.failure;
        }
    }

    async function confirm() {
        if (!preview) {
            return;
        }

        pending = true;
        failure = null;
        const result = await postJson<DisconnectPreview>("/api/orgconnection/disconnect", {
            expectedBindings: preview.bindings,
            expectedFieldMappings: preview.fieldMappings,
            expectedTargetConnection: preview.targetConnection !== null
        } satisfies ConfirmDisconnect);

        if (result.ok) {
            location.reload();
            return;
        }

        pending = false;
        failure = result.failure;
    }

    const plural = (n: number, noun: string) => `${n} ${noun}${n === 1 ? "" : "s"}`;
</script>

<dialog bind:this={dialog} class="card m-auto w-full max-w-lg border border-surface-200-800 bg-surface-50-950 p-0 backdrop:bg-black/50"
    aria-labelledby="disconnect-title">
    <div class="space-y-4 p-6">
        <header class="flex items-center gap-3">
            <TriangleAlert class="size-6 text-error-600-400" />
            <h2 id="disconnect-title" class="h5">Disconnect from Salesforce?</h2>
        </header>
        <p class="text-sm">This returns the application to a fresh install so it can be pointed at another org. It cannot be undone.</p>

        {#if preview}
            <div class="text-sm">
                <p class="font-semibold">Destroyed here</p>
                <ul class="list-disc pl-5">
                    <li>The connection details and the Signing Keypair</li>
                    <li>{plural(preview.bindings, "Binding")} and their {plural(preview.fieldMappings, "Field Mapping")}</li>
                    <li>{plural(preview.avroSchemas, "cached Avro Schema")}</li>
                    <li>{plural(preview.channels, "Channel")} and {plural(preview.channelMembers, "Channel Member")} (this application's copy), with every Checkpoint</li>
                    {#if preview.targetConnection}
                        <li><span class="font-semibold">The Target Connection</span> ({preview.targetConnection})</li>
                    {/if}
                </ul>
            </div>
            <div class="text-sm">
                <p class="font-semibold">Left standing in Salesforce</p>
                <ul class="list-disc pl-5 text-surface-600-400">
                    {#each preview.leftInSalesforce as item}<li>{item}</li>{/each}
                </ul>
            </div>
            <label class="label">
                <span class="label-text">Type <code class="code">{word}</code> to confirm</span>
                <input class="input font-mono" bind:value={typed} autocomplete="off" spellcheck="false" />
            </label>
        {:else if !failure}
            <p class="text-sm text-surface-600-400">Counting what would be destroyed…</p>
        {/if}

        {#if failure}<SalesforceError {failure} />{/if}
    </div>
    <footer class="flex justify-end gap-2 border-t border-surface-200-800 px-6 py-4">
        <button type="button" class="btn preset-tonal" onclick={() => dialog.close()} disabled={pending}>Cancel</button>
        <button type="button" class="btn preset-filled-error-500" disabled={!preview || typed.trim() !== word || pending} onclick={confirm}>
            {pending ? "Disconnecting…" : "Disconnect"}
        </button>
    </footer>
</dialog>
