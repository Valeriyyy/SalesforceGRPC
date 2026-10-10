<!-- One Binding's editor — the one place a Binding is changed. Field Mappings are a draft, validated by the server as
     they change and saved or discarded as a whole set; everything else saves on click and reloads (ADR 0006), and
     waits while the draft is unsaved so a reload never loses it. -->
<script lang="ts">
    import { untrack } from "svelte";
    import { ArrowLeft, ArrowRight, Check, CircleAlert, KeyRound, Power, Sparkles, Trash2, TriangleAlert } from "@lucide/svelte";
    import { deleteJson, postJson, putJson, type ApiFailure, type ApiResult } from "../../lib/api/client";
    import ConfirmDialog from "../../lib/components/ConfirmDialog.svelte";
    import DatabaseError from "../../lib/components/DatabaseError.svelte";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { BindingEditorView, BindingValidation, BindingView, CompatibilityResult, FieldMapping } from "../../lib/types/views";
    import FieldMappings from "./FieldMappings.svelte";
    import WaitingForSetup from "./WaitingForSetup.svelte";
    import { bindingStates, levelTones, plural } from "./state";

    let { page }: { page: BindingView } = $props();

    // Each page load is one Binding (ADR 0006), so the loaded values are read once.
    const editor = untrack(() => page.binding);
    const target = editor?.target ?? null;
    const saved: Record<string, string> = Object.fromEntries(
        (target?.fields ?? []).filter(f => f.mappedColumnName).map(f => [f.name, f.mappedColumnName!]));
    // Field Mappings are set up once the Key Mapping is chosen: the record ID's column is the one a field cannot take,
    // and the Key Mapping cannot change while a draft is unsaved. Only the editor asks for this order; the API does not.
    const keyChosen = !!editor?.keyMappingColumn;
    const prefilled = keyChosen && (target?.prefill.length ?? 0) > 0;

    // A Binding with no Field Mappings starts from the name matches, unsaved, so the first visit is a review.
    let draft = $state<Record<string, string>>(prefilled
        ? Object.fromEntries(target!.prefill.map(m => [m.salesforceFieldName, m.targetColumnName]))
        : { ...saved });

    let draftValidation = $state<BindingValidation | null>(null);
    let checking = $state(false);
    let pending = $state(false);
    let leaving = false;
    let failure = $state<ApiFailure | null>(null);

    let saveWarning = $state<ConfirmDialog>();
    let deleteBinding = $state<ConfirmDialog>();

    const keyOf = (mappings: Record<string, string>) => JSON.stringify(Object.entries(mappings).sort());
    const dirty = $derived(keyOf(draft) !== keyOf(saved));

    /** What the summary panel, the row indicators and the save warning go by: the draft while there is one. */
    const validation = $derived(dirty ? draftValidation : target?.validation ?? null);

    const mappings = (): FieldMapping[] =>
        Object.entries(draft).map(([salesforceFieldName, targetColumnName]) => ({ salesforceFieldName, targetColumnName }));

    let sequence = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;

    /** Asks the server to validate the draft. A reply overtaken by a later edit is dropped. */
    async function validateDraft(): Promise<BindingValidation | null> {
        const mine = ++sequence;
        checking = true;
        const result = await postJson<BindingValidation>(`/api/Bindings/${editor!.id}/validate`, { mappings: mappings() });
        if (mine !== sequence) {
            return null;
        }
        checking = false;
        if (!result.ok) {
            failure = result.failure;
            return null;
        }
        draftValidation = result.value;
        return result.value;
    }

    $effect(() => {
        keyOf(draft);
        clearTimeout(timer);
        if (!dirty) {
            sequence++;
            checking = false;
            draftValidation = null;
            return;
        }
        checking = true;
        timer = setTimeout(validateDraft, 400);
        return () => clearTimeout(timer);
    });

    $effect(() => {
        const warn = (e: BeforeUnloadEvent) => {
            if (dirty && !leaving) {
                e.preventDefault();
            }
        };
        addEventListener("beforeunload", warn);
        return () => removeEventListener("beforeunload", warn);
    });

    function reload() {
        leaving = true;
        location.reload();
    }

    async function act(request: () => Promise<ApiResult<unknown>>) {
        pending = true;
        failure = null;
        const result = await request();
        if (result.ok) {
            reload();
            return;
        }
        pending = false;
        failure = result.failure;
    }

    const saveMappings = () => putJson(`/api/Bindings/${editor!.id}/field-mappings`, { mappings: mappings() });

    async function save(binding: BindingEditorView) {
        // Saving a set that does not validate switches an Active Binding off, so that is asked first — against a
        // fresh validation, not one a pending edit is about to replace.
        if (binding.state === "active") {
            clearTimeout(timer);
            pending = true;
            const checked = await validateDraft();
            pending = false;
            if (!checked) {
                return;
            }
            if (!checked.canActivate) {
                saveWarning?.open();
                return;
            }
        }
        await act(saveMappings);
    }

    function discard() {
        draft = { ...saved };
    }

    function setSoftDelete(enabled: boolean, column: string | null) {
        act(() => putJson(`/api/Bindings/${editor!.id}/soft-delete`, { enabled, columnName: column }));
    }

    /** A result's subject in words: a field, the Key Mapping, the soft delete flag, or an unmapped column. */
    function subject(r: CompatibilityResult) {
        if (r.salesforceFieldName === "MappedSFKey") return `Key Mapping ${r.targetColumnName}`;
        if (r.salesforceFieldName === "SoftDelete") return `Soft delete ${r.targetColumnName}`;
        return r.salesforceFieldName || r.targetColumnName;
    }

    const problems = (v: BindingValidation) => [
        ...v.results.filter(r => r.level === "Error"),
        ...v.results.filter(r => r.level === "Warning")
    ];
</script>

<a href="/bindings" class="anchor mb-4 inline-flex items-center gap-1 text-sm"><ArrowLeft class="size-4" />Bindings</a>

{#if page.waiting.length > 0}
    <h1 class="h3 mb-6">Binding</h1>
    <WaitingForSetup steps={page.waiting} />
{:else if !editor}
    <h1 class="h3 mb-2">Binding not found</h1>
    <p class="text-surface-600-400">There is no Binding with that id. It may have been deleted.</p>
{:else}
    {@const binding = editor}
    {@const locked = dirty || pending}

    <!-- Header -->
    <div class="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div class="min-w-0 space-y-2">
            <h1 class="h3 flex flex-wrap items-center gap-3">{binding.entity} <ArrowRight class="size-5" /> <span class="font-mono">{binding.targetTable}</span></h1>
            <span class={["badge", bindingStates[binding.state].badge]}>{bindingStates[binding.state].label}</span>
            {#if binding.state === "needsAttention"}
                <p class="max-w-2xl text-sm text-error-700-300">
                    This Binding was Active and stopped validating, so it was set back to Incomplete and {binding.entity}'s changes are not being synced.
                    Fix what is listed under Ready to activate, then activate it again.
                </p>
            {:else if binding.state === "active"}
                <p class="text-sm text-surface-600-400">The worker is syncing {binding.entity}'s changes into this table.</p>
            {/if}
        </div>
        <div class="flex flex-col items-end gap-1">
            <div class="flex gap-2">
                {#if binding.state === "active"}
                    <button type="button" class="btn preset-tonal" disabled={locked}
                        onclick={() => act(() => postJson(`/api/Bindings/${binding.id}/deactivate`))}><Power class="size-4" />Deactivate</button>
                {:else}
                    <button type="button" class="btn preset-filled-success-500" disabled={locked || !target?.validation.canActivate}
                        title={target?.validation.canActivate ? "" : "Not ready to activate — see below"}
                        onclick={() => act(() => postJson(`/api/Bindings/${binding.id}/activate`))}><Check class="size-4" />Activate</button>
                {/if}
                <button type="button" class="btn preset-tonal-error" disabled={locked} onclick={() => deleteBinding?.open()}><Trash2 class="size-4" />Delete</button>
            </div>
            {#if dirty}<span class="text-xs text-warning-700-300">Save or discard Field Mappings first</span>{/if}
        </div>
    </div>

    {#if failure}<SalesforceError {failure} title="That didn't work" class="mb-6 max-w-5xl" />{/if}

    <div class="max-w-5xl space-y-8">
        <!-- 1. Target Table -->
        <section class="card space-y-1 border border-surface-200-800 p-6">
            <h2 class="h5">1 · Target Table</h2>
            <p class="text-sm text-surface-600-400">
                Writes to <span class="font-mono">{binding.targetTable}</span>. To write somewhere else, delete this Binding and bind {binding.entity} again.
            </p>
        </section>

        {#if binding.targetError || !target}
            <DatabaseError failure={binding.targetError ?? { message: "The Target Database could not be read.", rawResponse: "", occurredAt: null }}
                title="The Key Mapping, Field Mappings and deletes can't be shown" />
            <p class="text-sm text-surface-600-400">They come back once the Target Database can be read. Deactivating and deleting this Binding still work.</p>
        {:else}
            <!-- 2. Key Mapping -->
            <section class="card space-y-3 border border-surface-200-800 p-6">
                <h2 class="h5 flex items-center gap-2"><KeyRound class="size-4" />2 · Key Mapping</h2>
                <p class="text-sm text-surface-600-400">
                    The column that holds the Salesforce record ID. Updates and deletes find their row by it, and change events can arrive
                    more than once, so only a column with a unique constraint or primary key can hold it.
                </p>
                <select class="select max-w-md" value={binding.keyMappingColumn ?? ""} disabled={locked} aria-label="Key Mapping column"
                    onchange={e => act(() => putJson(`/api/Bindings/${binding.id}/key-mapping`, { targetColumnName: e.currentTarget.value }))}>
                    <option value="" disabled>Choose a column…</option>
                    <!-- The table's primary key is its own identity, so it is not offered — unless it already holds the
                         Key Mapping, which the select must still be able to show. -->
                    {#each target.columns.filter(c => !c.isPrimaryKey || c.columnName === binding.keyMappingColumn) as column (column.columnName)}
                        <option value={column.columnName} disabled={!column.isUnique}>
                            {column.columnName} · {column.dataType}{column.isUnique ? "" : " — no unique constraint"}
                        </option>
                    {/each}
                </select>
                {#if !binding.keyMappingColumn && target.suggestedKeyColumn}
                    {@const suggested = target.suggestedKeyColumn}
                    <p class="flex items-center gap-2 text-sm">
                        <Sparkles class="size-4 text-primary-600-400" />Suggested: <code class="code">{suggested}</code> ·
                        <button type="button" class="anchor" disabled={locked}
                            onclick={() => act(() => putJson(`/api/Bindings/${binding.id}/key-mapping`, { targetColumnName: suggested }))}>Use</button>
                    </p>
                {/if}
                {#if dirty}<p class="text-xs text-warning-700-300">Save or discard Field Mappings first.</p>{/if}
            </section>

            <!-- 3. Field Mappings -->
            <section class="card space-y-4 border border-surface-200-800 p-6">
                <div>
                    <h2 class="h5 mb-1">3 · Field Mappings</h2>
                    <p class="text-sm text-surface-600-400">Which Salesforce field feeds which column. Only the fields a change carries are written.</p>
                </div>
                {#if prefilled && dirty && binding.fieldMappingCount === 0}
                    <div class="card flex items-center gap-2 preset-tonal-primary p-3 text-sm">
                        <Sparkles class="size-4 shrink-0" />{plural(target.prefill.length, "field")} matched by name — review and Save.
                    </div>
                {/if}

                {#if !keyChosen}
                    <div class="card flex items-start gap-3 border border-dashed border-surface-300-700 p-4 text-sm">
                        <KeyRound class="size-4 shrink-0 text-surface-600-400" />
                        <p class="text-surface-600-400">
                            Choose the Key Mapping first. The record ID's column can't also take a field, so mapping starts once it is set{#if target.prefill.length > 0}
                            — {plural(target.prefill.length, "field")} already match a column by name{/if}.
                        </p>
                    </div>
                {:else}
                    <FieldMappings fields={target.fields} columns={target.columns} keyColumn={binding.keyMappingColumn}
                        bind:draft {validation} {checking} />

                    <div class="sticky bottom-0 flex flex-wrap items-center justify-end gap-3 border-t border-surface-200-800 bg-surface-50-950 py-3">
                        {#if dirty}
                            <span class="mr-auto text-sm text-warning-700-300">Unsaved changes</span>
                        {/if}
                        <span class="text-xs text-surface-600-400">
                            {binding.state === "active" ? "Saving applies to the running worker at once." : "Saved mappings take effect once the Binding is activated."}
                        </span>
                        <button type="button" class="btn preset-tonal" disabled={!dirty || pending} onclick={discard}>Discard</button>
                        <button type="button" class="btn preset-filled-primary-500" disabled={!dirty || pending} onclick={() => save(binding)}>Save Field Mappings</button>
                    </div>
                {/if}
            </section>

            <!-- 4. Deletes -->
            <section class="card space-y-3 border border-surface-200-800 p-6">
                <h2 class="h5">4 · When a record is deleted in Salesforce</h2>
                <fieldset class="space-y-2 text-sm" disabled={locked}>
                    <legend class="sr-only">When a record is deleted</legend>
                    <label class="flex items-center gap-2">
                        <input class="radio" type="radio" name="deletes" checked={!binding.softDeleteEnabled}
                            onchange={() => setSoftDelete(false, null)} />Delete the row
                    </label>
                    <label class="flex items-center gap-2">
                        <input class="radio" type="radio" name="deletes" checked={binding.softDeleteEnabled}
                            disabled={target.softDeleteColumns.length === 0}
                            onchange={() => setSoftDelete(true, binding.softDeleteColumnName ?? target.softDeleteColumns[0])} />
                        Keep the row and set a flag column
                    </label>
                    {#if target.softDeleteColumns.length === 0}
                        <p class="ml-6 text-xs text-surface-600-400">No column of {binding.targetTable} can hold the flag: it needs a boolean or integer column.</p>
                    {:else if binding.softDeleteEnabled}
                        <select class="select ml-6 max-w-xs" value={binding.softDeleteColumnName ?? ""} aria-label="Flag column"
                            onchange={e => setSoftDelete(true, e.currentTarget.value)}>
                            {#each target.softDeleteColumns as column}<option value={column}>{column}</option>{/each}
                        </select>
                    {/if}
                </fieldset>
                {#if dirty}<p class="text-xs text-warning-700-300">Save or discard Field Mappings first.</p>{/if}
            </section>

            <!-- 5. Validation -->
            <section class={["card space-y-3 border p-6", validation === null ? "border-surface-200-800" : validation.canActivate ? "border-success-500" : "border-error-500"]}>
                {#if validation === null}
                    <h2 class="h5">5 · Ready to activate?</h2>
                    <p class="text-sm text-surface-600-400">Checking your unsaved Field Mappings…</p>
                {:else}
                    <h2 class="h5">5 · {validation.canActivate ? "Ready to activate" : "Not ready to activate"}</h2>
                    {#if dirty}
                        <p class="text-sm text-surface-600-400">With your unsaved Field Mappings:</p>
                    {:else if !validation.canActivate}
                        <p class="text-sm">This Binding can't be activated until:</p>
                    {/if}
                    <ul class="space-y-1 text-sm">
                        {#each validation.blockers as blocker}
                            <li class="flex gap-2 text-error-700-300"><CircleAlert class="size-4 shrink-0" />{blocker}</li>
                        {/each}
                        {#each problems(validation) as result}
                            <li class={["flex gap-2", levelTones[result.level]]}>
                                <TriangleAlert class="size-4 shrink-0" /><span><span class="font-mono">{subject(result)}</span>: {result.message}</span>
                            </li>
                        {/each}
                        {#if validation.canActivate && problems(validation).length === 0}
                            <li class="text-success-700-300">Every mapped field is compatible.</li>
                        {/if}
                    </ul>
                {/if}
            </section>
        {/if}
    </div>

    <ConfirmDialog bind:this={saveWarning} title="Save and switch this Binding off?" confirmLabel="Save anyway" pendingLabel="Saving…" danger
        action={saveMappings} onsuccess={reload}>
        <p>These Field Mappings don't validate, so saving them takes {binding.entity} out of the worker's plan: the Binding is set back to Incomplete.</p>
        {#if draftValidation}
            <ul class="list-disc space-y-1 pl-5">
                {#each draftValidation.blockers as blocker}<li>{blocker}</li>{/each}
                {#each draftValidation.results.filter(r => r.level === "Error") as result}
                    <li><span class="font-mono">{subject(result)}</span>: {result.message}</li>
                {/each}
            </ul>
        {/if}
        <p class="font-semibold">Changes made in Salesforce from now until you fix it and activate it again will not be synced.</p>
    </ConfirmDialog>

    <ConfirmDialog bind:this={deleteBinding} title={`Delete the Binding for ${binding.entity}?`} confirmLabel="Delete Binding" pendingLabel="Deleting…" danger
        action={() => deleteJson(`/api/Bindings/${binding.id}`)}
        onsuccess={() => { leaving = true; location.href = binding.memberId === null ? "/bindings" : `/bindings/new?member=${binding.memberId}`; }}>
        <p>
            Deletes the Binding to <span class="font-mono">{binding.targetTable}</span>
            {#if binding.keyMappingColumn}, its Key Mapping{/if} and its {plural(binding.fieldMappingCount, "Field Mapping")}.
            Nothing in the Target Database is touched.
        </p>
        {#if binding.state === "active"}
            <p class="font-semibold">It is Active: syncing {binding.entity} stops at once.</p>
        {/if}
        {#if binding.memberId !== null}<p>You can then choose another table for {binding.entity}.</p>{/if}
    </ConfirmDialog>
{/if}
