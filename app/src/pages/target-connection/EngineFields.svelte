<!-- One engine's fields, rendered from its definition: the connection's own details in two columns, then its options
     in a visible group. Identity fields can be locked (Bindings exist); a stored password can be kept by leaving it
     blank. The browser's required checks are a convenience; the server is the validator. -->
<script lang="ts">
    import { Lock } from "@lucide/svelte";
    import type { EngineView } from "../../lib/types/views";
    import { isIdentity, isOption, type FormValues } from "./engines";

    let { engine, values = $bindable(), locked, keepPassword }: {
        engine: EngineView;
        values: FormValues;
        locked: boolean;
        keepPassword: boolean;
    } = $props();

    const details = $derived(engine.fields.filter(f => !isOption(f)));
    const options = $derived(engine.fields.filter(isOption));
</script>

<div class="grid gap-4 sm:grid-cols-2">
    {#each details as f (f.name)}
        {@const isLocked = locked && isIdentity(f)}
        <label class={["label", (f.name === "host" || f.name === "filePath") && "sm:col-span-2"]}>
            <span class="label-text flex items-center gap-1">
                {f.label}
                {#if isLocked}<Lock class="size-3 text-surface-500" aria-label="Locked" />{/if}
                {#if f.kind === "secret" && keepPassword}<span class="text-surface-600-400">(leave blank to keep)</span>{/if}
            </span>
            {#if f.kind === "secret"}
                <input class="input" type="password" bind:value={values[f.name]} required={f.required && !keepPassword}
                    placeholder={keepPassword ? "•••••••• stored" : ""} autocomplete="new-password" />
            {:else if f.kind === "int"}
                <input class="input" inputmode="numeric" pattern="[0-9]*" bind:value={values[f.name]} required={f.required} />
            {:else}
                <input class={["input", f.name === "filePath" && "font-mono text-sm", isLocked && "opacity-60"]}
                    bind:value={values[f.name]} required={f.required} readonly={isLocked} spellcheck="false" autocomplete="off"
                    placeholder={f.name === "filePath" ? "/var/lib/salesforcegrpc/target.db" : ""} />
            {/if}
            {#if f.name === "filePath"}
                <span class="text-xs text-surface-600-400">A path on the machine running this application, not on your computer.</span>
            {/if}
        </label>
    {/each}
</div>

{#if options.length}
    <fieldset class="mt-5 border-t border-surface-200-800 pt-4">
        <legend class="label-text pr-2 text-surface-600-400">Connection options</legend>
        <div class="grid gap-4 sm:grid-cols-2">
            {#each options as f (f.name)}
                {#if f.kind === "bool"}
                    <label class="flex items-center gap-2 self-end">
                        <input class="checkbox" type="checkbox" checked={values[f.name] === "true"}
                            onchange={e => values[f.name] = String(e.currentTarget.checked)} />
                        {f.label}
                    </label>
                {:else if f.kind === "choice"}
                    <label class="label">
                        <span class="label-text">{f.label}</span>
                        <select class="select" bind:value={values[f.name]} required={f.required}>
                            {#if !f.required && !f.default}<option value="">Driver default</option>{/if}
                            {#each f.choices ?? [] as choice}<option value={choice}>{choice}</option>{/each}
                        </select>
                    </label>
                {:else}
                    <label class="label">
                        <span class="label-text">{f.label}</span>
                        <input class="input" bind:value={values[f.name]} required={f.required} spellcheck="false" autocomplete="off" />
                    </label>
                {/if}
            {/each}
        </div>
    </fieldset>
{/if}
