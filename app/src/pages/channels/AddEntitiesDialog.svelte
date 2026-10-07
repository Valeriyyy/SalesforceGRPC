<!-- Adding Entities: a searchable list of what Salesforce lets this Channel carry, minus what it already carries.
     Everything picked goes in one all-or-nothing submission; when Salesforce refuses one, nothing is added, the
     refused Entity is named with Salesforce's reason, and the selection stays so it can be adjusted and retried. -->
<script lang="ts">
    import { CircleX, Search } from "@lucide/svelte";
    import { getJson, postJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { ChannelDetailView } from "../../lib/types/views";
    import type { AddChannelMembers, AddChannelMembersResult, SelectableEntity } from "./requests";

    let { channel }: { channel: ChannelDetailView } = $props();

    let dialog: HTMLDialogElement;
    let entities = $state<SelectableEntity[] | null>(null);
    let selected = $state<string[]>([]);
    let search = $state("");
    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);
    let refused = $state<AddChannelMembersResult | null>(null);

    const carried = $derived(new Set(channel.members.map(m => m.entity.toLowerCase())));
    const offered = $derived((entities ?? [])
        .filter(e => !carried.has(e.value.toLowerCase()))
        .filter(e => search.trim() === "" || e.value.toLowerCase().includes(search.trim().toLowerCase())
            || e.label.toLowerCase().includes(search.trim().toLowerCase())));

    export async function open() {
        selected = [];
        search = "";
        failure = null;
        refused = null;
        dialog.showModal();

        if (entities === null) {
            const result = await getJson<SelectableEntity[]>("/api/PlatformEventManagement/selectable-entities?channelType=data");
            if (result.ok) {
                entities = result.value;
            } else {
                failure = result.failure;
            }
        }
    }

    async function add() {
        pending = true;
        failure = null;
        refused = null;
        const result = await postJson<AddChannelMembersResult>(`/api/PlatformEventManagement/channels/${channel.id}/members/batch`, {
            members: selected.map(selectedEntity => ({ selectedEntity }))
        } satisfies AddChannelMembers);

        if (result.ok && result.value.added) {
            location.reload();
            return;
        }
        pending = false;
        if (result.ok) {
            refused = result.value;
        } else {
            failure = result.failure;
        }
    }
</script>

<dialog bind:this={dialog} class="card m-auto w-full max-w-xl border border-surface-200-800 bg-surface-50-950 p-0 backdrop:bg-black/50"
    aria-label="Add Entities">
    <div class="space-y-4 p-6">
        <h2 class="h5">Add Entities to {channel.label}</h2>
        <p class="text-sm text-surface-600-400">Each Entity is one Salesforce object's change events. All of those you pick are added together, or none are.</p>

        <div class="input-group grid-cols-[auto_1fr]">
            <div class="ig-cell preset-tonal"><Search class="size-4" /></div>
            <input class="ig-input" type="search" placeholder="Search Entities" bind:value={search} disabled={entities === null} />
        </div>

        <div class="max-h-72 overflow-y-auto rounded-container border border-surface-200-800">
            {#if entities === null && !failure}
                <p class="p-4 text-sm text-surface-600-400">Asking Salesforce which Entities can be added…</p>
            {:else if offered.length === 0}
                <p class="p-4 text-sm text-surface-600-400">{search ? "Nothing matches." : "This Channel already carries every Entity Salesforce offers."}</p>
            {:else}
                {#each offered as entity (entity.value)}
                    <label class="flex items-center gap-3 border-b border-surface-200-800 px-4 py-2 last:border-b-0 hover:preset-tonal">
                        <input class="checkbox" type="checkbox" value={entity.value} bind:group={selected} />
                        <span class="font-mono text-sm">{entity.value}</span>
                        {#if entity.label !== entity.value}<span class="text-xs text-surface-600-400">{entity.label}</span>{/if}
                    </label>
                {/each}
            {/if}
        </div>

        {#if refused}
            <div class="card space-y-2 border border-error-500 bg-error-50-950 p-4 text-sm" role="alert">
                <p class="flex items-center gap-2 font-semibold"><CircleX class="size-5 text-error-600-400" />Salesforce refused part of this, so nothing was added</p>
                <ul class="space-y-1">
                    {#each refused.outcomes.filter(o => o.status === "Failed") as outcome}
                        <li><span class="font-mono font-semibold">{outcome.selectedEntity}</span>: {outcome.message}</li>
                    {/each}
                </ul>
                <p class="text-surface-600-400">Remove it from your selection, or fix what Salesforce objects to, and add again.</p>
                {#if refused.leftInSalesforce.length > 0}
                    <p class="text-warning-700-300">
                        Salesforce kept {refused.leftInSalesforce.join(", ")} even so, and it could not be removed. Resync on the Channels list to bring it in, or remove it there.
                    </p>
                {/if}
            </div>
        {/if}
        {#if failure}<SalesforceError {failure} />{/if}
    </div>
    <footer class="flex items-center justify-between gap-2 border-t border-surface-200-800 px-6 py-4">
        <span class="text-sm text-surface-600-400">{selected.length} selected</span>
        <div class="flex gap-2">
            <button type="button" class="btn preset-tonal" onclick={() => dialog.close()} disabled={pending}>Cancel</button>
            <button type="button" class="btn preset-filled-primary-500" onclick={add} disabled={pending || selected.length === 0}>
                {pending ? "Adding…" : `Add ${selected.length || ""} ${selected.length === 1 ? "Entity" : "Entities"}`}
            </button>
        </div>
    </footer>
</dialog>
