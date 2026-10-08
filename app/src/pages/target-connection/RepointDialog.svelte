<!-- The last step of a repoint: from and to, what is destroyed, and a typed confirmation of the current database's
     name. The confirmation carries the view's counts, so a Binding created since makes the server refuse rather than
     destroy something the user never saw. The new database is checked before anything is destroyed. -->
<script lang="ts">
    import { TriangleAlert } from "@lucide/svelte";
    import { postJson, type ApiFailure } from "../../lib/api/client";
    import DatabaseError from "../../lib/components/DatabaseError.svelte";
    import type { TargetConnectionView } from "../../lib/types/views";
    import { addressOf, confirmationWord, type RepointTargetConnection, type SaveTargetConnection } from "./engines";

    let { page }: { page: TargetConnectionView } = $props();

    let dialog: HTMLDialogElement;
    let request = $state<SaveTargetConnection | null>(null);
    let failure = $state<ApiFailure | null>(null);
    let typed = $state("");
    let pending = $state(false);

    const word = $derived(page.connection ? confirmationWord(page.connection) : "");

    export function open(to: SaveTargetConnection) {
        request = to;
        failure = null;
        typed = "";
        dialog.showModal();
    }

    async function confirm() {
        if (!request) {
            return;
        }

        pending = true;
        failure = null;
        const result = await postJson("/api/targetconnection/repoint", {
            ...request,
            expectedBindings: page.bindings,
            expectedFieldMappings: page.fieldMappings
        } satisfies RepointTargetConnection);

        if (result.ok) {
            // Stays pending while the page reloads, so nothing can be pressed twice.
            location.reload();
            return;
        }

        pending = false;
        failure = result.failure;
    }

    const plural = (n: number, noun: string) => `${n} ${noun}${n === 1 ? "" : "s"}`;
</script>

<dialog bind:this={dialog} class="card m-auto w-full max-w-lg border border-surface-200-800 bg-surface-50-950 p-0 backdrop:bg-black/50"
    aria-labelledby="repoint-title" oncancel={e => { if (pending) e.preventDefault(); }}>
    <div class="space-y-4 p-6">
        <header class="flex items-center gap-3">
            <TriangleAlert class="size-6 text-error-600-400" />
            <h2 id="repoint-title" class="h5">Switch the Target Database?</h2>
        </header>

        {#if page.connection && request}
            <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
                <dt class="text-surface-600-400">From</dt><dd class="break-all font-mono">{addressOf(page.connection)}</dd>
                <dt class="text-surface-600-400">To</dt><dd class="break-all font-mono">{addressOf(request)}</dd>
            </dl>
        {/if}

        <div class="text-sm">
            <p class="font-semibold">Destroyed</p>
            <ul class="list-disc pl-5">
                <li>{plural(page.bindings, "Binding")}</li>
                <li>{plural(page.fieldMappings, "Field Mapping")}</li>
            </ul>
        </div>
        <p class="text-sm text-surface-600-400">
            Channels and Channel Members are kept. Nothing in either database is changed. The new database is checked first;
            if it doesn't answer, nothing is destroyed.
        </p>

        <label class="label">
            <span class="label-text">Type <code class="code">{word}</code> to confirm</span>
            <input class="input font-mono" bind:value={typed} autocomplete="off" spellcheck="false" />
        </label>

        {#if failure}<DatabaseError {failure} />{/if}
    </div>
    <footer class="flex justify-end gap-2 border-t border-surface-200-800 px-6 py-4">
        <button type="button" class="btn preset-tonal" onclick={() => dialog.close()} disabled={pending}>Cancel</button>
        <button type="button" class="btn preset-filled-error-500" disabled={!request || typed.trim() !== word || pending} onclick={confirm}>
            {pending ? "Verifying…" : "Destroy Bindings and switch"}
        </button>
    </footer>
</dialog>
