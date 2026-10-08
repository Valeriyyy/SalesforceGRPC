<!-- The engine picker and its details form: a row of radio cards, the chosen engine's fields beneath, then the hint,
     the inline error and the buttons. Its values outlive a failed save, so nothing typed (the password included) is
     lost. Switching engine starts that engine's form afresh. -->
<script lang="ts">
    import { untrack } from "svelte";
    import { CircleCheck, Info, Lock } from "@lucide/svelte";
    import type { ApiFailure } from "../../lib/api/client";
    import DatabaseError from "../../lib/components/DatabaseError.svelte";
    import type { TargetConnectionView } from "../../lib/types/views";
    import EngineFields from "./EngineFields.svelte";
    import EngineMark from "./EngineMark.svelte";
    import { defaultsOf, identityChanged, presentationOf, requestOf, valuesOf, type FormMode, type FormValues,
        type SaveTargetConnection } from "./engines";

    let { page, mode, pending, error, onsubmit, oncancel }: {
        page: TargetConnectionView;
        mode: FormMode;
        pending: boolean;
        error: ApiFailure | null;
        onsubmit: (request: SaveTargetConnection) => void;
        oncancel?: () => void;
    } = $props();

    // Seeded once from the view model; the form owns them after that.
    const seed = untrack(() => {
        const connection = mode === "new" ? null : page.connection;
        const engine = connection ? page.engines.find(e => e.engine === connection.engine) ?? null : null;
        return { engine, values: engine && connection ? valuesOf(engine, connection) : {} };
    });
    let engine = $state(seed.engine);
    let values = $state<FormValues>(seed.values);

    /** Bindings exist, so the identity changes only through a repoint. */
    const locked = $derived(mode === "edit" && page.identityLocked);
    const changed = $derived(mode !== "new" && !!page.connection && !!engine && identityChanged(page.connection, engine, values));
    const keepPassword = $derived(mode === "edit" && !!page.connection?.hasPassword && !changed);
    const submitLabel = $derived(mode === "repoint" ? "Continue…" : changed ? "Verify and switch database" : "Verify and save");

    function select(name: string) {
        const next = page.engines.find(e => e.engine === name);
        if (!next || !next.isAvailable || locked || engine?.engine === name) {
            return;
        }
        engine = next;
        values = defaultsOf(next);
    }

    function submit(e: SubmitEvent) {
        e.preventDefault();
        if (engine) {
            onsubmit(requestOf(engine, values));
        }
    }
</script>

<form onsubmit={submit}>
    <fieldset>
        <legend class="label-text mb-2">Database engine</legend>
        <div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
            {#each page.engines as e (e.engine)}
                {@const selected = engine?.engine === e.engine}
                {@const disabled = !e.isAvailable || (locked && !selected)}
                {@const p = presentationOf(e.engine)}
                <label class={["card relative flex flex-col gap-3 border-2 p-4 transition",
                    selected ? "border-primary-500 bg-primary-50-950" : "border-surface-200-800",
                    disabled ? "cursor-not-allowed opacity-50" : "cursor-pointer hover:border-surface-400-600",
                    "has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-primary-500"]}>
                    <input class="sr-only" type="radio" name="engine" value={e.engine} checked={selected} {disabled}
                        onchange={() => select(e.engine)} />
                    {#if selected}<CircleCheck class="absolute right-3 top-3 size-5 text-primary-600-400" />{/if}
                    <EngineMark engine={e.engine} />
                    <div>
                        <p class="font-semibold">{p.label}</p>
                        <p class="text-xs text-surface-600-400">{e.isAvailable ? p.blurb : e.unavailableReason}</p>
                    </div>
                </label>
            {/each}
        </div>
    </fieldset>

    {#if engine}
        <div class="card mt-6 border border-surface-200-800 bg-surface-50-950 p-5">
            <h2 class="h6 mb-4">{presentationOf(engine.engine).label} details</h2>
            <EngineFields {engine} bind:values {locked} {keepPassword} />
        </div>
    {/if}

    <div class="mt-5 space-y-4">
        {#if locked}
            <p class="flex items-start gap-2 text-xs text-surface-600-400">
                <Lock class="mt-0.5 size-3.5 shrink-0" />
                <span>
                    Engine, host and database are locked because {page.bindings} Binding{page.bindings === 1 ? "" : "s"}
                    write{page.bindings === 1 ? "s" : ""} to this database. To use a different one, cancel and choose
                    <strong>Point at a different database</strong>.
                </span>
            </p>
        {:else if mode === "edit" && changed}
            <p class="flex items-start gap-2 text-xs text-surface-600-400">
                <Info class="mt-0.5 size-3.5 shrink-0" />
                This points the application at a different database. No Bindings exist yet, so nothing is lost. Enter the password for it.
            </p>
        {:else if mode === "repoint" && !changed}
            <p class="flex items-start gap-2 text-xs text-surface-600-400">
                <Info class="mt-0.5 size-3.5 shrink-0" />
                Change the engine, host, database or file path to continue.
            </p>
        {/if}

        {#if error}<DatabaseError failure={error} />{/if}

        <div class="flex items-center gap-2">
            <button type="submit" class="btn preset-filled-primary-500" disabled={pending || !engine || (mode === "repoint" && !changed)}>
                {pending ? "Verifying…" : submitLabel}
            </button>
            {#if oncancel}<button type="button" class="btn preset-tonal" onclick={oncancel} disabled={pending}>Cancel</button>{/if}
            {#if !engine}<span class="text-sm text-surface-600-400">Choose an engine first.</span>{/if}
        </div>
    </div>
</form>
