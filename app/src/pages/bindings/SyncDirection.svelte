<!-- Sync Direction: Inbound, or Two-way with Writeback. The choice, the Writeback Key, each field's direction and the
     Writeback Defaults are edited together and saved as one, because Two-way is checked as a whole: a refusal lists
     every reason at once, each database prerequisite with the SQL that fixes it. Saved, the page reloads (ADR 0006). -->
<script lang="ts">
    import { untrack } from "svelte";
    import { ArrowLeftRight, CircleAlert, Lock, TriangleAlert } from "@lucide/svelte";
    import { putJson, type ApiFailure } from "../../lib/api/client";
    import CopyField from "../../lib/components/CopyField.svelte";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { FieldDirection, SetSyncDirection, SyncDirection, WritebackIssue, WritebackSetup } from "../../lib/types/views";

    let { bindingId, entity, setup, disabled }: { bindingId: number; entity: string; setup: WritebackSetup; disabled: boolean } = $props();

    // Each page load is one Binding (ADR 0006), so the loaded setup is read once and edited as a draft.
    const loaded = untrack(() => setup);

    let direction = $state<SyncDirection>(loaded.syncDirection);
    let keyColumn = $state(loaded.writebackKeyColumn ?? loaded.keyColumnCandidates[0] ?? "");
    let keyField = $state(loaded.writebackKeySalesforceField ?? loaded.externalIdFields[0] ?? "");
    let chosen = $state<Record<string, FieldDirection>>(
        Object.fromEntries(loaded.fields.map(f => [f.salesforceFieldName, f.chosenDirection])));
    let defaults = $state<Record<string, string>>(
        Object.fromEntries(loaded.requiredFields.filter(r => r.defaultValue !== null).map(r => [r.salesforceFieldName, r.defaultValue!])));

    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);

    const directions: { value: FieldDirection; label: string }[] = [
        { value: "TwoWay", label: "Two-way" },
        { value: "InboundOnly", label: "Inbound only" },
        { value: "WritebackOnly", label: "Writeback only" }
    ];

    const changed = $derived(
        direction !== loaded.syncDirection
        || (direction === "TwoWay" && (
            keyColumn !== (loaded.writebackKeyColumn ?? "")
            || keyField !== (loaded.writebackKeySalesforceField ?? "")
            || loaded.fields.some(f => chosen[f.salesforceFieldName] !== f.chosenDirection)
            || loaded.requiredFields.some(r => (defaults[r.salesforceFieldName] ?? "") !== (r.defaultValue ?? "")))));

    /** What a field supplies once the user's choices are applied: a forced direction always wins. */
    const effective = (name: string, forced: boolean, direction: FieldDirection) => forced ? direction : chosen[name];

    const supplied = (restName: string) => loaded.fields.some(f =>
        f.restFieldName === restName && effective(f.salesforceFieldName, f.forced, f.direction) !== "InboundOnly");

    const refused = $derived<WritebackIssue[]>(failure?.kind === "writebackRefused" ? failure.issues : []);

    async function save() {
        pending = true;
        failure = null;
        const body: SetSyncDirection = {
            syncDirection: direction,
            writebackKeyColumn: keyColumn || null,
            writebackKeySalesforceField: keyField || null,
            fieldDirections: loaded.fields.filter(f => !f.forced).map(f => ({ salesforceFieldName: f.salesforceFieldName, direction: chosen[f.salesforceFieldName] })),
            writebackDefaults: Object.entries(defaults).filter(([, v]) => v.trim() !== "").map(([salesforceFieldName, value]) => ({ salesforceFieldName, value }))
        };
        const result = await putJson(`/api/Bindings/${bindingId}/sync-direction`, body);
        if (result.ok) {
            location.reload();
            return;
        }
        pending = false;
        failure = result.failure;
    }
</script>

<fieldset class="space-y-2 text-sm" disabled={disabled || pending}>
    <legend class="sr-only">Sync Direction</legend>
    <label class="flex items-center gap-2">
        <input class="radio" type="radio" name="sync-direction" checked={direction === "Inbound"} onchange={() => direction = "Inbound"} />
        Inbound <span class="text-surface-600-400">(Salesforce → Target Database)</span>
    </label>
    <label class="flex items-center gap-2">
        <input class="radio" type="radio" name="sync-direction" checked={direction === "TwoWay"} disabled={!loaded.canBeTwoWay}
            onchange={() => direction = "TwoWay"} />
        Two-way <span class="text-surface-600-400">(also writes the table's own changes back to Salesforce)</span>
    </label>
    {#if !loaded.canBeTwoWay}
        <p class="ml-6 text-xs text-surface-600-400">{loaded.unavailableReason}</p>
    {/if}
</fieldset>

{#if direction === "TwoWay"}
    <div class="space-y-5 border-l-2 border-primary-500 pl-4">
        <p class="text-sm text-surface-600-400">
            Rows inserted or updated in the table by anything other than this application are written to {entity}'s records:
            a row with a Salesforce ID updates that record with only the fields that changed, and a row without one is
            matched on the Writeback Key, so it is created once. Deletes are not written back.
        </p>

        <!-- The Writeback Key -->
        <div class="space-y-2">
            <h3 class="font-semibold">Writeback Key</h3>
            <p class="text-sm text-surface-600-400">
                A column that identifies each row, stored in a Salesforce External ID field marked Unique, so a row sent twice still makes one record.
            </p>
            <div class="flex flex-wrap items-center gap-2 text-sm">
                <select class="select max-w-xs px-3" bind:value={keyColumn} disabled={disabled || pending} aria-label="Writeback Key column">
                    <option value="" disabled>Choose a column…</option>
                    {#each loaded.keyColumnCandidates as column}<option value={column}>{column}</option>{/each}
                </select>
                <ArrowLeftRight class="size-4 text-surface-500" />
                <select class="select max-w-xs px-3" bind:value={keyField} disabled={disabled || pending} aria-label="External ID field">
                    <option value="" disabled>Choose an External ID field…</option>
                    {#each loaded.externalIdFields as field}<option value={field}>{field}</option>{/each}
                </select>
            </div>
            {#if loaded.keyColumnCandidates.length === 0}
                <p class="text-xs text-warning-700-300">No column of the table other than the Key Mapping is unique. Add a primary key or unique constraint.</p>
            {/if}
            {#if loaded.externalIdFields.length === 0}
                <p class="text-xs text-warning-700-300">No field of the object is an External ID marked Unique. Create one in Salesforce Setup, then reload.</p>
            {/if}
        </div>

        <!-- Field Directions -->
        {#if loaded.fields.length > 0}
            <div class="space-y-2">
                <h3 class="font-semibold">Field Directions</h3>
                <div class="table-wrap">
                    <table class="table text-sm">
                        <thead><tr><th>Salesforce field</th><th>Column</th><th>Direction</th></tr></thead>
                        <tbody>
                            {#each loaded.fields as field (field.salesforceFieldName)}
                                <tr>
                                    <td class="font-mono">{field.restFieldName ?? field.salesforceFieldName}</td>
                                    <td class="font-mono">{field.targetColumnName}</td>
                                    <td>
                                        {#if field.forced}
                                            <span class="inline-flex items-center gap-1" title={field.reason ?? ""}>
                                                <Lock class="size-3" />{directions.find(d => d.value === field.direction)?.label}
                                            </span>
                                            <span class="block text-xs text-surface-600-400">{field.reason}</span>
                                        {:else}
                                            <select class="select px-2 py-1" bind:value={chosen[field.salesforceFieldName]}
                                                disabled={disabled || pending} aria-label={`Direction of ${field.salesforceFieldName}`}>
                                                {#each directions as d}<option value={d.value}>{d.label}</option>{/each}
                                            </select>
                                        {/if}
                                    </td>
                                </tr>
                            {/each}
                        </tbody>
                    </table>
                </div>
            </div>
        {/if}

        <!-- Required fields and Writeback Defaults -->
        {#if loaded.requiredFields.length > 0}
            <div class="space-y-2">
                <h3 class="font-semibold">Required to create a record</h3>
                <p class="text-sm text-surface-600-400">
                    Salesforce won't create a record without these. A default you set is sent whenever a row doesn't supply one, so you can
                    find and fix those records in Salesforce. Leave it blank if Salesforce automation fills the field.
                </p>
                <ul class="space-y-2 text-sm">
                    {#each loaded.requiredFields as field (field.salesforceFieldName)}
                        <li class="flex flex-wrap items-center gap-2">
                            <span class="w-48 font-mono">{field.salesforceFieldName}</span>
                            {#if supplied(field.salesforceFieldName)}
                                <span class="text-success-700-300">Supplied by a Field Mapping</span>
                            {:else if field.allowedValues.length > 0}
                                <select class="select max-w-xs px-3" bind:value={defaults[field.salesforceFieldName]} disabled={disabled || pending}
                                    aria-label={`Writeback Default for ${field.salesforceFieldName}`}>
                                    <option value="">No default</option>
                                    {#each field.allowedValues as value}<option {value}>{value}</option>{/each}
                                </select>
                            {:else}
                                <input class="input max-w-xs" placeholder="No default" bind:value={defaults[field.salesforceFieldName]}
                                    disabled={disabled || pending} aria-label={`Writeback Default for ${field.salesforceFieldName}`} />
                            {/if}
                            {#if !supplied(field.salesforceFieldName) && !(defaults[field.salesforceFieldName] ?? "").trim()}
                                <span class="flex items-center gap-1 text-xs text-warning-700-300">
                                    <TriangleAlert class="size-3" />Creates fail unless automation fills it
                                </span>
                            {/if}
                        </li>
                    {/each}
                </ul>
            </div>
        {/if}

        {#if loaded.warnings.length > 0}
            <ul class="space-y-1 text-sm">
                {#each loaded.warnings as warning}
                    <li class="flex gap-2 text-warning-700-300"><TriangleAlert class="size-4 shrink-0" />{warning}</li>
                {/each}
            </ul>
        {/if}

        {#if loaded.issues.length > 0 && refused.length === 0}
            <div class="space-y-2">
                <p class="text-sm font-semibold">As stored, Two-way would be refused until:</p>
                <ul class="space-y-3 text-sm">
                    {#each loaded.issues as issue}
                        <li class="space-y-1">
                            <span class="flex gap-2 text-error-700-300"><CircleAlert class="size-4 shrink-0" />{issue.message}</span>
                            {#if issue.sql}<CopyField label="SQL to run" value={issue.sql} />{/if}
                        </li>
                    {/each}
                </ul>
            </div>
        {/if}
    </div>
{/if}

{#if refused.length > 0}
    <div class="card space-y-3 border border-error-500 bg-error-50-950 p-4 text-sm" role="alert">
        <p class="font-semibold">Two-way wasn't switched on. Fix these, then save again:</p>
        <ul class="space-y-3">
            {#each refused as issue}
                <li class="space-y-1">
                    <span class="flex gap-2"><CircleAlert class="size-4 shrink-0 text-error-600-400" />{issue.message}</span>
                    {#if issue.sql}<CopyField label="SQL to run" value={issue.sql} />{/if}
                </li>
            {/each}
        </ul>
    </div>
{:else if failure}
    <SalesforceError {failure} title="The Sync Direction couldn't be saved" />
{/if}

<div class="flex items-center justify-end gap-3">
    {#if changed}<span class="mr-auto text-sm text-warning-700-300">Unsaved changes</span>{/if}
    <button type="button" class="btn preset-filled-primary-500" disabled={disabled || pending || !changed} onclick={save}>
        {pending ? "Saving…" : "Save Sync Direction"}
    </button>
</div>
