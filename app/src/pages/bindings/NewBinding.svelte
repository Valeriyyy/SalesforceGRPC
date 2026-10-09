<!-- Choosing the Target Table for an unbound Entity. Tables whose name matches the Entity come first; a table another
     Entity already writes to is shown but cannot be chosen. Creating the Binding goes straight to its editor. -->
<script lang="ts">
    import { untrack } from "svelte";
    import { ArrowLeft, Link2, Sparkles, Table2 } from "@lucide/svelte";
    import { postJson, type ApiFailure } from "../../lib/api/client";
    import DatabaseError from "../../lib/components/DatabaseError.svelte";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { NewBindingView, TargetTableOptionView } from "../../lib/types/views";
    import WaitingForSetup from "./WaitingForSetup.svelte";

    let { page }: { page: NewBindingView } = $props();

    // Preselects the first table whose name matches, so the common case is one click.
    let chosen = $state(untrack(() => page.tables.find(t => t.nameMatches && !t.boundEntity)?.fullName ?? ""));
    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);

    async function create(memberId: number, table: TargetTableOptionView) {
        pending = true;
        failure = null;
        const result = await postJson<{ id: number }>(`/api/Bindings/members/${memberId}`,
            { targetSchema: table.schemaName, targetTable: table.tableName });
        if (result.ok) {
            location.href = `/bindings/${result.value.id}`;
            return;
        }
        pending = false;
        failure = result.failure;
    }
</script>

<a href="/bindings" class="anchor mb-4 inline-flex items-center gap-1 text-sm"><ArrowLeft class="size-4" />Bindings</a>

{#if page.waiting.length > 0}
    <h1 class="h3 mb-6">New Binding</h1>
    <WaitingForSetup steps={page.waiting} />
{:else if !page.member}
    <h1 class="h3 mb-2">Entity not found</h1>
    <p class="text-surface-600-400">There is no Channel Member with that id on the Primary Channel. It may have been removed, or belong to another Channel.</p>
{:else}
    {@const member = page.member}
    <div class="mb-6">
        <h1 class="h3 mb-2">Bind {member.entity}</h1>
        <p class="text-surface-600-400">Choose the table its changes land in. This application never creates tables — create one in the Target Database first if none fits.</p>
    </div>

    {#if page.targetError}
        <DatabaseError failure={page.targetError} title="The Target Database's tables could not be read" class="max-w-3xl" />
    {:else if page.tables.length === 0}
        <section class="card flex max-w-3xl items-start gap-4 border border-surface-200-800 bg-surface-50-950 p-6">
            <Table2 class="size-8 shrink-0 text-surface-600-400" />
            <div class="space-y-1">
                <h2 class="h5">The Target Database has no tables</h2>
                <p class="text-sm text-surface-600-400">Create the table {member.entity} should land in, then come back and reload.</p>
            </div>
        </section>
    {:else}
        <div class="max-w-3xl space-y-4">
            <fieldset class="table-wrap card border border-surface-200-800" disabled={pending}>
                <legend class="sr-only">Target Table</legend>
                <table class="table">
                    <tbody>
                        {#each page.tables as table (table.fullName)}
                            <tr class={[table.boundEntity ? "opacity-60" : "cursor-pointer hover:preset-tonal-primary"]}
                                onclick={() => { if (!table.boundEntity) chosen = table.fullName; }}>
                                <td class="w-8">
                                    <input class="radio" type="radio" name="target-table" value={table.fullName} bind:group={chosen}
                                        disabled={!!table.boundEntity} aria-label={table.fullName} />
                                </td>
                                <td class="font-mono text-sm">{table.fullName}</td>
                                <td class="text-right text-sm">
                                    {#if table.boundEntity}
                                        <span class="text-surface-600-400">Bound to <span class="font-mono">{table.boundEntity}</span></span>
                                    {:else if table.nameMatches}
                                        <span class="badge preset-tonal-primary gap-1"><Sparkles class="size-3" />Name matches</span>
                                    {/if}
                                </td>
                            </tr>
                        {/each}
                    </tbody>
                </table>
            </fieldset>

            {#if failure}<SalesforceError {failure} title="The Binding could not be created" />{/if}

            <div class="flex items-center justify-end gap-3">
                <span class="text-sm text-surface-600-400">It starts Incomplete: nothing syncs until you map it and activate it.</span>
                <button type="button" class="btn preset-filled-primary-500" disabled={!chosen || pending}
                    onclick={() => create(member.id, page.tables.find(t => t.fullName === chosen)!)}>
                    <Link2 class="size-4" />{pending ? "Creating…" : "Create Binding"}
                </button>
            </div>
        </div>
    {/if}
{/if}
