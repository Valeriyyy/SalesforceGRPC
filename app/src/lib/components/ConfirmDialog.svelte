<!-- A confirmation for an action that cannot be taken back. With `typeToConfirm`, the action stays disabled until
     that word is typed, for the cases where a slip would stop the worker. -->
<script lang="ts">
    import type { Snippet } from "svelte";
    import { TriangleAlert } from "@lucide/svelte";
    import type { ApiFailure, ApiResult } from "../api/client";
    import SalesforceError from "./SalesforceError.svelte";

    let { title, confirmLabel, pendingLabel, danger = false, typeToConfirm = null, action, onsuccess, children }: {
        title: string;
        confirmLabel: string;
        pendingLabel: string;
        danger?: boolean;
        typeToConfirm?: string | null;
        action: () => Promise<ApiResult<unknown>>;
        onsuccess: () => void;
        children: Snippet;
    } = $props();

    let dialog: HTMLDialogElement;
    let typed = $state("");
    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);

    export function open() {
        typed = "";
        failure = null;
        dialog.showModal();
    }

    async function confirm() {
        pending = true;
        failure = null;
        const result = await action();
        if (result.ok) {
            // Stays pending while the page moves on, so nothing can be pressed twice.
            onsuccess();
            return;
        }
        pending = false;
        failure = result.failure;
    }
</script>

<dialog bind:this={dialog} class="card m-auto w-full max-w-lg border border-surface-200-800 bg-surface-50-950 p-0 backdrop:bg-black/50"
    aria-label={title}>
    <div class="space-y-4 p-6">
        <header class="flex items-center gap-3">
            {#if danger}<TriangleAlert class="size-6 text-error-600-400" />{/if}
            <h2 class="h5">{title}</h2>
        </header>
        <div class="space-y-3 text-sm">{@render children()}</div>
        {#if typeToConfirm}
            <label class="label">
                <span class="label-text">Type <code class="code">{typeToConfirm}</code> to confirm</span>
                <input class="input font-mono" bind:value={typed} autocomplete="off" spellcheck="false" />
            </label>
        {/if}
        {#if failure}<SalesforceError {failure} />{/if}
    </div>
    <footer class="flex justify-end gap-2 border-t border-surface-200-800 px-6 py-4">
        <button type="button" class="btn preset-tonal" onclick={() => dialog.close()} disabled={pending}>Cancel</button>
        <button type="button" class={["btn", danger ? "preset-filled-error-500" : "preset-filled-primary-500"]}
            disabled={pending || (typeToConfirm !== null && typed.trim() !== typeToConfirm)} onclick={confirm}>
            {pending ? pendingLabel : confirmLabel}
        </button>
    </footer>
</dialog>
