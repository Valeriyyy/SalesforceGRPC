<!-- The Channels list. Reads the Mirror only; Resync is the one thing here that asks Salesforce, and only when
     pressed. Each row opens its Channel's own page, where everything about it is managed. -->
<script lang="ts">
    import { Plus, RefreshCw, Radio, Star } from "@lucide/svelte";
    import { postJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { ChannelsView } from "../../lib/types/views";
    import { when } from "../../lib/utils/format";
    import Notice from "./Notice.svelte";
    import WaitingOnOrg from "./WaitingOnOrg.svelte";

    let { page }: { page: ChannelsView } = $props();

    let resyncing = $state(false);
    let failure = $state<ApiFailure | null>(null);

    async function resync() {
        resyncing = true;
        failure = null;
        const result = await postJson("/api/PlatformEventManagement/resync");
        if (result.ok) {
            location.reload();
            return;
        }
        resyncing = false;
        failure = result.failure;
    }
</script>

<div class="mb-6 flex flex-wrap items-start justify-between gap-4">
    <div>
        <h1 class="h3 mb-2">Channels</h1>
        <p class="text-surface-600-400">The Salesforce Channels that carry change events, and which one the worker follows.</p>
    </div>
    {#if page.orgConnectionReady}
        <div class="flex flex-col items-end gap-1">
            <div class="flex gap-2">
                <button type="button" class="btn preset-tonal" onclick={resync} disabled={resyncing}>
                    <RefreshCw class={["size-4", resyncing && "animate-spin"]} />{resyncing ? "Resyncing…" : "Resync from Salesforce"}
                </button>
                <a href="/channels/new" class="btn preset-filled-primary-500"><Plus class="size-4" />New Channel</a>
            </div>
            <span class="text-xs text-surface-600-400">Last synced {when(page.lastSyncedAt)}</span>
        </div>
    {/if}
</div>

{#if !page.orgConnectionReady}
    <WaitingOnOrg />
{:else}
    <Notice notice={page.notice} />
    {#if failure}<SalesforceError {failure} title="Resync failed" class="mb-6" />{/if}

    {#if page.channels.length === 0}
        <section class="card flex max-w-3xl items-start gap-4 border border-surface-200-800 bg-surface-50-950 p-6">
            <Radio class="size-8 shrink-0 text-surface-600-400" />
            <div class="space-y-2">
                <h2 class="h5">No Channels yet</h2>
                <p class="text-sm text-surface-600-400">
                    A Channel is a stream Salesforce publishes change events on, carrying the Entities you add to it —
                    AccountChangeEvent for Accounts, and so on. The worker follows one of them: the Primary Channel.
                </p>
                <p class="text-sm text-surface-600-400">If you created Channels in Salesforce Setup, Resync to bring them in.</p>
                <a href="/channels/new" class="btn preset-filled-primary-500"><Plus class="size-4" />Create a Channel</a>
            </div>
        </section>
    {:else}
        <div class="table-wrap card border border-surface-200-800">
            <table class="table">
                <thead>
                    <tr><th>Channel</th><th>Channel Members</th><th>Streaming</th></tr>
                </thead>
                <tbody class="[&>tr]:hover:preset-tonal-primary">
                    {#each page.channels as channel (channel.id)}
                        <tr class="cursor-pointer" onclick={() => location.href = `/channels/${channel.id}`}>
                            <td>
                                <a href="/channels/{channel.id}" class="font-semibold hover:underline">{channel.label}</a>
                                {#if channel.isPrimary}
                                    <span class="badge preset-filled-primary-500 ml-2 gap-1"><Star class="size-3" />Primary</span>
                                {/if}
                                <div class="font-mono text-xs text-surface-600-400">{channel.fullName}</div>
                            </td>
                            <td>
                                {channel.memberCount} {channel.memberCount === 1 ? "Entity" : "Entities"}
                                {#if channel.unboundCount > 0}
                                    <span class="block text-xs text-warning-700-300">{channel.unboundCount} without a Binding</span>
                                {/if}
                            </td>
                            <td class="text-sm">
                                {#if channel.streaming}{channel.streaming}{:else}<span class="text-surface-600-400">—</span>{/if}
                            </td>
                        </tr>
                    {/each}
                </tbody>
            </table>
        </div>
    {/if}
{/if}
