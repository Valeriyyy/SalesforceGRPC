<!-- One Channel: its Label, whether the worker follows it, where it starts, the Entities it carries, and deleting
     it. Every action goes through the api and reloads (ADR 0006), so the page and the sidebar agree. -->
<script lang="ts">
    import { ArrowLeft, Check, Pencil, Plus, Star, Trash2, X } from "@lucide/svelte";
    import { deleteJson, patchJson, putJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { ChannelMemberView, ChannelView, StartingPoint } from "../../lib/types/views";
    import { when } from "../../lib/utils/format";
    import AddEntitiesDialog from "./AddEntitiesDialog.svelte";
    import ConfirmDialog from "./ConfirmDialog.svelte";
    import EditMemberDialog from "./EditMemberDialog.svelte";
    import MakePrimaryDialog from "./MakePrimaryDialog.svelte";
    import Notice from "./Notice.svelte";
    import WaitingOnOrg from "./WaitingOnOrg.svelte";
    import { plural } from "./requests";

    let { page }: { page: ChannelView } = $props();

    let makePrimary = $state<MakePrimaryDialog>();
    let clearPrimary = $state<ConfirmDialog>();
    let addEntities = $state<AddEntitiesDialog>();
    let editMember = $state<EditMemberDialog>();
    let removeMember = $state<ConfirmDialog>();
    let deleteChannel = $state<ConfirmDialog>();

    let editingLabel = $state(false);
    let label = $state("");
    let pending = $state<"label" | "startingPoint" | null>(null);
    let failure = $state<ApiFailure | null>(null);
    let removing = $state<ChannelMemberView | null>(null);

    const stateLabels = { active: "Active", inactive: "Inactive", incomplete: "Incomplete" } as const;
    const stateTones = { active: "preset-filled-success-500", inactive: "preset-tonal", incomplete: "preset-filled-warning-500" } as const;

    function startEditingLabel(current: string) {
        label = current;
        editingLabel = true;
    }

    async function saveLabel(e: SubmitEvent, id: number) {
        e.preventDefault();
        pending = "label";
        failure = null;
        const result = await patchJson(`/api/PlatformEventManagement/channels/${id}`, { label: label.trim() });
        if (result.ok) {
            location.reload();
            return;
        }
        pending = null;
        failure = result.failure;
    }

    async function setStartingPoint(id: number, value: StartingPoint) {
        pending = "startingPoint";
        failure = null;
        const result = await putJson(`/api/Bindings/channels/${id}/starting-point`, { startingPoint: value });
        if (result.ok) {
            location.reload();
            return;
        }
        pending = null;
        failure = result.failure;
    }

    function askToRemove(member: ChannelMemberView) {
        removing = member;
        removeMember?.open();
    }
</script>

<a href="/channels" class="anchor mb-4 inline-flex items-center gap-1 text-sm"><ArrowLeft class="size-4" />Channels</a>

{#if !page.orgConnectionReady}
    <h1 class="h3 mb-6">Channel</h1>
    <WaitingOnOrg />
{:else if !page.channel}
    <h1 class="h3 mb-2">Channel not found</h1>
    <p class="text-surface-600-400">There is no Change Data Capture Channel with that id. It may have been deleted, or removed by a Resync.</p>
{:else}
    {@const channel = page.channel}
    <Notice notice={page.notice} />

    <!-- Header -->
    <div class="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div class="min-w-0">
            {#if editingLabel}
                <form class="flex items-center gap-2" onsubmit={e => saveLabel(e, channel.id)}>
                    <input class="input h3 max-w-md" required maxlength="80" bind:value={label} aria-label="Label" />
                    <button type="submit" class="btn-icon preset-filled-primary-500" disabled={pending === "label" || !label.trim()} aria-label="Save label"><Check class="size-4" /></button>
                    <button type="button" class="btn-icon preset-tonal" onclick={() => editingLabel = false} disabled={pending === "label"} aria-label="Cancel"><X class="size-4" /></button>
                </form>
            {:else}
                <h1 class="h3 flex items-center gap-2">
                    {channel.label}
                    <button type="button" class="btn-icon btn-icon-sm hover:preset-tonal" onclick={() => startEditingLabel(channel.label)} aria-label="Edit label"><Pencil class="size-4" /></button>
                </h1>
            {/if}
            <p class="font-mono text-sm text-surface-600-400">{channel.fullName}</p>
        </div>
        <div class="flex items-center gap-2">
            {#if channel.isPrimary}
                <span class="badge preset-filled-primary-500 gap-1"><Star class="size-3" />Primary Channel</span>
                <button type="button" class="btn preset-tonal" onclick={() => clearPrimary?.open()}>Clear Primary Channel</button>
            {:else}
                <button type="button" class="btn preset-filled-primary-500" onclick={() => makePrimary?.open()}><Star class="size-4" />Make Primary Channel</button>
            {/if}
        </div>
    </div>

    {#if failure}<SalesforceError {failure} class="mb-6" />{/if}

    <!-- Streaming -->
    <section class="card mb-6 space-y-4 border border-surface-200-800 bg-surface-50-950 p-6">
        <h2 class="h5">Streaming</h2>
        <p class="text-sm text-surface-600-400">
            {#if channel.isPrimary}The worker follows this Channel.{:else}The worker does not follow this Channel; it follows the Primary Channel only.{/if}
        </p>

        {#if channel.checkpoint}
            {@const cp = channel.checkpoint}
            <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
                <dt class="text-surface-600-400">Checkpoint</dt>
                <dd>
                    saved {cp.age} ago ({when(cp.savedAt)}) ·
                    {#if cp.expired}<span class="text-warning-700-300">expired — the worker restarts from Earliest</span>{:else}expires in {cp.expiresIn}{/if}
                </dd>
            </dl>
        {/if}

        <fieldset class="space-y-2" disabled={pending === "startingPoint"}>
            <legend class="label-text mb-1">Starting Point</legend>
            <div class="flex flex-wrap gap-x-6 gap-y-2">
                {#each [["latest", "Latest", "only changes from now on"], ["earliest", "Earliest", "up to 72 hours back"]] as [value, name, hint]}
                    <label class="flex items-center gap-2">
                        <input class="radio" type="radio" name="starting-point" checked={channel.startingPoint === value}
                            onchange={() => setStartingPoint(channel.id, value as StartingPoint)} />
                        <span><span class="font-semibold">{name}</span> <span class="text-sm text-surface-600-400">— {hint}</span></span>
                    </label>
                {/each}
            </div>
            <p class="text-xs text-surface-600-400">
                {#if channel.checkpoint}
                    Not used while a Checkpoint exists (saved {channel.checkpoint.age} ago). It applies again only if the Checkpoint is discarded.
                {:else}
                    Where the worker begins this Channel, until it saves its first Checkpoint.
                {/if}
            </p>
        </fieldset>
    </section>

    <!-- Channel Members -->
    <section class="mb-6 space-y-3">
        <div class="flex items-center justify-between gap-4">
            <h2 class="h5">Channel Members</h2>
            <button type="button" class="btn preset-filled-primary-500" onclick={() => addEntities?.open()}><Plus class="size-4" />Add Entities</button>
        </div>

        {#if channel.members.length === 0}
            <div class="card border border-dashed border-surface-300-700 p-6 text-sm text-surface-600-400">
                This Channel carries nothing yet{#if channel.isPrimary}, so no events arrive{/if}. Add the Entities whose changes you want to sync.
            </div>
        {:else}
            <div class="table-wrap card border border-surface-200-800">
                <table class="table">
                    <thead>
                        <tr><th>Entity</th><th>Binding</th><th>Filter Expression</th><th>Enriched Fields</th><th class="text-right">Actions</th></tr>
                    </thead>
                    <tbody>
                        {#each channel.members as member (member.id)}
                            <tr>
                                <td class="font-mono text-sm">{member.entity}</td>
                                <td>
                                    {#if member.binding}
                                        <a href="/bindings" class="font-mono text-sm hover:underline">{member.binding.targetTable}</a>
                                        <span class={["badge ml-1", stateTones[member.binding.state]]}>{stateLabels[member.binding.state]}</span>
                                    {:else}
                                        <span class="text-sm text-warning-700-300">Unbound</span>
                                    {/if}
                                </td>
                                <td class="max-w-xs truncate font-mono text-xs" title={member.filterExpression ?? ""}>{member.filterExpression ?? "—"}</td>
                                <td class="font-mono text-xs">{member.enrichedFields.length ? member.enrichedFields.join(", ") : "—"}</td>
                                <td class="text-right whitespace-nowrap">
                                    <button type="button" class="btn-icon btn-icon-sm hover:preset-tonal" onclick={() => editMember?.open(member)} aria-label="Edit {member.entity}"><Pencil class="size-4" /></button>
                                    <button type="button" class="btn-icon btn-icon-sm hover:preset-tonal-error" onclick={() => askToRemove(member)} aria-label="Remove {member.entity}"><Trash2 class="size-4" /></button>
                                </td>
                            </tr>
                        {/each}
                    </tbody>
                </table>
            </div>
        {/if}
    </section>

    <!-- Danger zone -->
    <section class="card space-y-3 border border-error-500 p-6">
        <h2 class="h5">Delete Channel</h2>
        <p class="text-sm text-surface-600-400">Deletes it in Salesforce, with its Channel Members and its Checkpoint.</p>
        <button type="button" class="btn preset-filled-error-500" onclick={() => deleteChannel?.open()}><Trash2 class="size-4" />Delete Channel</button>
    </section>

    <MakePrimaryDialog bind:this={makePrimary} {channel} />
    <AddEntitiesDialog bind:this={addEntities} {channel} />
    <EditMemberDialog bind:this={editMember} />

    <ConfirmDialog bind:this={clearPrimary} title="Clear the Primary Channel?" confirmLabel="Clear Primary Channel" pendingLabel="Clearing…"
        action={() => deleteJson("/api/Bindings/primary-channel")} onsuccess={() => location.reload()}>
        <p>The worker stops streaming. Nothing is deleted and no Binding changes.</p>
        {#if channel.checkpoint}
            <p>The Checkpoint is kept, so making this the Primary Channel again can Resume from it — for as long as Salesforce still has the events, at most 72 hours after it was saved.</p>
        {/if}
    </ConfirmDialog>

    <ConfirmDialog bind:this={removeMember} title={`Remove ${removing?.entity ?? ""}?`} confirmLabel="Remove" pendingLabel="Removing…" danger
        action={() => deleteJson(`/api/PlatformEventManagement/members/${removing?.id}`)} onsuccess={() => location.reload()}>
        <p>{channel.label} stops carrying {removing?.entity}.</p>
        {#if removing?.binding}
            <p>
                Its Binding to <span class="font-mono">{removing.binding.targetTable}</span>
                {#if channel.isPrimary && removing.binding.state === "active"}
                    stops syncing and is set Inactive. Its Field Mappings are kept, so adding the Entity back and switching the Binding on restores it.
                {:else}
                    is kept with its Field Mappings, and is linked again if the Entity is added back.
                {/if}
            </p>
        {/if}
    </ConfirmDialog>

    <ConfirmDialog bind:this={deleteChannel} title={`Delete ${channel.label}?`} confirmLabel="Delete Channel" pendingLabel="Deleting…" danger
        typeToConfirm={channel.isPrimary ? channel.fullName : null}
        action={() => deleteJson(`/api/PlatformEventManagement/channels/${channel.id}`)} onsuccess={() => location.href = "/channels"}>
        <p>This deletes <span class="font-mono">{channel.fullName}</span> in Salesforce. It cannot be undone.</p>
        {#if channel.members.length > 0}
            <div>
                <p class="font-semibold">Its {plural(channel.members.length, "Channel Member")} go with it</p>
                <ul class="list-disc pl-5">
                    {#each channel.members as member}
                        <li>
                            <span class="font-mono">{member.entity}</span>
                            {#if member.binding}— Binding to <span class="font-mono">{member.binding.targetTable}</span>{#if channel.isPrimary && member.binding.state === "active"} stops syncing{/if}{/if}
                        </li>
                    {/each}
                </ul>
            </div>
        {/if}
        {#if channel.checkpoint}<p>Its Checkpoint is lost.</p>{/if}
        {#if channel.isPrimary}
            <p class="font-semibold">This is the Primary Channel. Afterwards there is none, and nothing streams until you choose or create another. Bindings keep their state.</p>
        {/if}
    </ConfirmDialog>
{/if}
