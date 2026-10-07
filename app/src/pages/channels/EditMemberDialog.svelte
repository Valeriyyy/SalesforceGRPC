<!-- A Channel Member's two editable settings. Both replace what is there, as Salesforce treats them; Salesforce
     validates the filter itself, and its refusal is shown as it comes back. -->
<script lang="ts">
    import { X } from "@lucide/svelte";
    import { patchJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { ChannelMemberView } from "../../lib/types/views";
    import type { UpdateChannelMember } from "./requests";

    let dialog: HTMLDialogElement;
    let member = $state<ChannelMemberView | null>(null);
    let filterExpression = $state("");
    let enrichedFields = $state<string[]>([]);
    let field = $state("");
    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);

    export function open(target: ChannelMemberView) {
        member = target;
        filterExpression = target.filterExpression ?? "";
        enrichedFields = [...target.enrichedFields];
        field = "";
        failure = null;
        dialog.showModal();
    }

    function addField(e?: Event) {
        e?.preventDefault();
        const name = field.trim();
        if (name && !enrichedFields.some(f => f.toLowerCase() === name.toLowerCase())) {
            enrichedFields = [...enrichedFields, name];
        }
        field = "";
    }

    async function save() {
        if (!member) {
            return;
        }
        pending = true;
        failure = null;
        const result = await patchJson(`/api/PlatformEventManagement/members/${member.id}`, {
            filterExpression: filterExpression.trim() || null,
            enrichedFields
        } satisfies UpdateChannelMember);
        if (result.ok) {
            location.reload();
            return;
        }
        pending = false;
        failure = result.failure;
    }
</script>

<dialog bind:this={dialog} class="card m-auto w-full max-w-lg border border-surface-200-800 bg-surface-50-950 p-0 backdrop:bg-black/50"
    aria-label="Edit Channel Member">
    {#if member}
        <div class="space-y-4 p-6">
            <h2 class="h5">Edit <span class="font-mono">{member.entity}</span></h2>

            <label class="label">
                <span class="label-text">Filter Expression</span>
                <textarea class="textarea font-mono text-sm" rows="3" bind:value={filterExpression}
                    placeholder="Industry = 'Energy' AND BillingCountry = 'US'" spellcheck="false"></textarea>
                <span class="text-xs text-surface-600-400">Salesforce drops events that don't match before sending them. Leave empty to receive every change.</span>
            </label>

            <div class="space-y-2">
                <span class="label-text">Enriched Fields</span>
                <div class="flex flex-wrap gap-2">
                    {#each enrichedFields as name (name)}
                        <span class="chip preset-tonal font-mono text-xs">
                            {name}
                            <button type="button" onclick={() => enrichedFields = enrichedFields.filter(f => f !== name)} aria-label="Remove {name}"><X class="size-3" /></button>
                        </span>
                    {:else}
                        <span class="text-sm text-surface-600-400">None</span>
                    {/each}
                </div>
                <div class="input-group grid-cols-[1fr_auto]">
                    <input class="ig-input font-mono text-sm" bind:value={field} placeholder="Field API name, e.g. Industry"
                        onkeydown={e => e.key === "Enter" && addField(e)} spellcheck="false" />
                    <button type="button" class="ig-btn preset-tonal" onclick={() => addField()} disabled={!field.trim()}>Add</button>
                </div>
                <span class="text-xs text-surface-600-400">Fields Salesforce includes in every event for this Entity, even when unchanged.</span>
            </div>

            {#if failure}<SalesforceError {failure} />{/if}
        </div>
        <footer class="flex justify-end gap-2 border-t border-surface-200-800 px-6 py-4">
            <button type="button" class="btn preset-tonal" onclick={() => dialog.close()} disabled={pending}>Cancel</button>
            <button type="button" class="btn preset-filled-primary-500" onclick={save} disabled={pending}>{pending ? "Saving…" : "Save"}</button>
        </footer>
    {/if}
</dialog>
