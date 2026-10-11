<!-- The Field Mapping grid: one row per Salesforce field, flattened out of its compound parent, with a Target Column
     dropdown. Edits only the draft; saving and discarding it is the editor's. Compatibility comes from the server's
     validation of the draft, never from rules repeated here. -->
<script lang="ts">
    import { Check, LoaderCircle, Search, X } from "@lucide/svelte";
    import type { BindableField, BindingValidation, CompatibilityResult, TargetColumn } from "../../lib/types/views";
    import { levelTones } from "./state";

    let { fields, columns, keyColumn, draft = $bindable(), validation, checking }: {
        fields: BindableField[];
        columns: TargetColumn[];
        keyColumn: string | null;
        /** Salesforce field name → Target Column name. Unmapped fields are absent. */
        draft: Record<string, string>;
        /** Validation of the draft as it stands, or null while none has come back. */
        validation: BindingValidation | null;
        checking: boolean;
    } = $props();

    type Show = "all" | "mapped" | "unmapped";

    let query = $state("");
    let show = $state<Show>("all");

    const usedBy = $derived(new Map(Object.entries(draft).map(([field, column]) => [column.toLowerCase(), field])));
    const mappedCount = $derived(fields.filter(f => draft[f.name]).length);

    const visible = $derived.by(() => {
        const q = query.trim().toLowerCase();
        return fields.filter(f =>
            (show === "all" || (show === "mapped") === !!draft[f.name]) &&
            (!q || f.name.toLowerCase().includes(q) || (f.parentName ?? "").toLowerCase().includes(q)
                || (draft[f.name] ?? "").toLowerCase().includes(q)));
    });

    function setColumn(field: string, column: string) {
        if (column) {
            draft[field] = column;
        } else {
            delete draft[field];
        }
    }

    /** The result for this field's current column; a result for a column it has since moved off is stale. */
    function resultFor(field: string): CompatibilityResult | undefined {
        const column = draft[field];
        return column === undefined
            ? undefined
            : validation?.results.find(r => r.salesforceFieldName === field && r.targetColumnName.toLowerCase() === column.toLowerCase());
    }

    function columnLabel(c: TargetColumn) {
        return `${c.columnName} · ${c.dataType}${c.maxLength ? `(${c.maxLength})` : ""}`;
    }
</script>

<div class="mb-3 flex flex-wrap items-center gap-3">
    <div class="relative w-full max-w-sm">
        <Search class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-primary-600-400" />
        <input class="input h-10 rounded-base border-2 border-surface-300-700 bg-surface-50-950 pr-9 pl-9 shadow-sm
                      placeholder:text-surface-500 focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 [&::-webkit-search-cancel-button]:appearance-none"
            type="search" placeholder="Filter by field or column…" bind:value={query} aria-label="Filter fields by name" />
        {#if query}
            <button type="button" class="absolute top-1/2 right-2 -translate-y-1/2 rounded-full p-1 text-surface-600-400 hover:preset-tonal"
                aria-label="Clear filter" onclick={() => query = ""}><X class="size-4" /></button>
        {/if}
    </div>
    <div class="flex gap-1" role="group" aria-label="Show">
        {#each [["all", `All ${fields.length}`], ["mapped", `Mapped ${mappedCount}`], ["unmapped", `Unmapped ${fields.length - mappedCount}`]] as [value, label]}
            <button type="button" class={["btn btn-sm", show === value ? "preset-filled-primary-500" : "preset-tonal"]}
                aria-pressed={show === value} onclick={() => show = value as Show}>{label}</button>
        {/each}
    </div>
    {#if checking}<span class="flex items-center gap-1 text-xs text-surface-600-400"><LoaderCircle class="size-3 animate-spin" />Checking compatibility…</span>{/if}
</div>

<div class="table-wrap">
    <table class="table text-sm">
        <thead>
            <tr><th>Salesforce field</th><th>Field Type</th><th>Target Column</th><th>Compatibility</th></tr>
        </thead>
        <tbody>
            {#each visible as field (field.name)}
                {@const result = resultFor(field.name)}
                <tr>
                    <td class="font-mono">
                        {#if field.parentName}<span class="text-surface-500">{field.parentName} ›</span> {/if}{field.name}
                    </td>
                    <td><span class="badge preset-tonal text-xs">{field.fieldType}</span></td>
                    <td>
                        <select class="select px-3 py-1 text-sm" value={draft[field.name] ?? ""} aria-label="Target Column for {field.name}"
                            onchange={e => setColumn(field.name, e.currentTarget.value)}>
                            <option value="">— not mapped —</option>
                            {#each columns as column (column.columnName)}
                                {@const holder = usedBy.get(column.columnName.toLowerCase())}
                                {@const isKey = column.columnName === keyColumn}
                                <option value={column.columnName} disabled={isKey || (holder !== undefined && holder !== field.name)}>
                                    {columnLabel(column)}{isKey ? " — Key Mapping" : holder && holder !== field.name ? ` — used by ${holder}` : ""}
                                </option>
                            {/each}
                        </select>
                    </td>
                    <td class="text-xs">
                        {#if result}
                            {#if result.level === "Compatible"}
                                <span class={["flex items-center gap-1", levelTones.Compatible]}><Check class="size-3" />Compatible</span>
                            {:else}
                                <span class={levelTones[result.level]}><span class="font-semibold">{result.level}:</span> {result.message}</span>
                            {/if}
                        {:else if draft[field.name] && checking}
                            <span class="text-surface-500">…</span>
                        {/if}
                    </td>
                </tr>
            {:else}
                <tr><td colspan="4" class="text-center text-surface-600-400">No field matches.</td></tr>
            {/each}
        </tbody>
    </table>
</div>

<p class="mt-2 text-xs text-surface-600-400">{mappedCount} of {fields.length} fields mapped. Unmapped fields are ignored.</p>
