<!-- The Bindings list: every Entity on the Primary Channel and where it lands. Read-only — each row opens the page
     where it is changed: its editor, or choosing a Target Table when it is not bound yet. -->
<script lang="ts">
    import { ArrowRight, Link2, Star, Unlink } from "@lucide/svelte";
    import type { BindingsView } from "../../lib/types/views";
    import WaitingForSetup from "./WaitingForSetup.svelte";
    import { bindingStates, plural } from "./state";

    let { page }: { page: BindingsView } = $props();

    const counts = $derived({
        active: page.rows.filter(r => r.state === "active").length,
        unbound: page.rows.filter(r => r.state === "unbound").length,
        needsAttention: page.rows.filter(r => r.state === "needsAttention").length
    });
</script>

<div class="mb-6">
    <h1 class="h3 mb-2">Bindings</h1>
    <p class="text-surface-600-400">
        Where each Entity on the Primary Channel
        {#if page.primaryChannelLabel}<span class="badge preset-filled-primary-500 gap-1"><Star class="size-3" />{page.primaryChannelLabel}</span>{/if}
        lands in the Target Database.
    </p>
</div>

{#if page.waiting.length > 0}
    <WaitingForSetup steps={page.waiting} />
{:else if page.rows.length === 0}
    <section class="card flex max-w-3xl items-start gap-4 border border-surface-200-800 bg-surface-50-950 p-6">
        <Unlink class="size-8 shrink-0 text-surface-600-400" />
        <div class="space-y-2">
            <h2 class="h5">The Primary Channel carries no Entities</h2>
            <p class="text-sm text-surface-600-400">Add a Channel Member for each Entity whose changes you want to sync, then bind it here.</p>
            <a href="/channels" class="anchor text-sm">Open Channels</a>
        </div>
    </section>
{:else}
    <div class="mb-4 flex flex-wrap gap-6 text-sm">
        <span><strong>{counts.active}</strong> of {page.rows.length} {page.rows.length === 1 ? "Entity" : "Entities"} syncing</span>
        {#if counts.needsAttention}<span class="text-error-700-300"><strong>{counts.needsAttention}</strong> need{counts.needsAttention === 1 ? "s" : ""} attention</span>{/if}
        {#if counts.unbound}<span class="text-warning-700-300"><strong>{counts.unbound}</strong> without a Binding</span>{/if}
    </div>

    <div class="table-wrap card border border-surface-200-800">
        <table class="table">
            <thead>
                <tr><th>Entity</th><th></th><th>Target Table</th><th>Key Mapping</th><th>Field Mappings</th><th>State</th></tr>
            </thead>
            <tbody class="[&>tr]:hover:preset-tonal-primary">
                {#each page.rows as row (row.memberId)}
                    <tr class="cursor-pointer" onclick={() => location.href = row.href}>
                        <td><a href={row.href} class="font-semibold hover:underline">{row.entity}</a></td>
                        <td><ArrowRight class="size-4 text-surface-500" /></td>
                        {#if row.bindingId !== null}
                            <td class="font-mono text-sm">{row.targetTable}</td>
                            <td class="font-mono text-sm">{row.keyMappingColumn ?? "—"}</td>
                            <td class="text-sm">
                                {#if row.fieldCount !== null}{row.fieldMappingCount} of {row.fieldCount} fields{:else}{plural(row.fieldMappingCount, "field")}{/if}
                            </td>
                            <td>
                                <span class={["badge", bindingStates[row.state].badge]}>{bindingStates[row.state].label}</span>
                                {#if row.state === "needsAttention"}
                                    <span class="block text-xs text-error-700-300">Forced back to Incomplete — not syncing</span>
                                {/if}
                            </td>
                        {:else}
                            <td colspan="3" class="text-sm text-surface-600-400">Not bound — events for this Entity are skipped</td>
                            <td><a href={row.href} class="btn btn-sm preset-filled-primary-500"><Link2 class="size-3" />Bind</a></td>
                        {/if}
                    </tr>
                {/each}
            </tbody>
        </table>
    </div>
{/if}
