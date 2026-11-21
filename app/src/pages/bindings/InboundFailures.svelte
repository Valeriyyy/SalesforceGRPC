<!-- The change events that couldn't be applied to this Binding's Target Table, each with the records it covered and
     why. There is no Retry: StarPush doesn't repair data, the user corrects it some other way and then Dismisses the
     failure. Dismissing reloads the page (ADR 0006). -->
<script lang="ts">
    import { X } from "@lucide/svelte";
    import { deleteJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { InboundFailure } from "../../lib/types/views";
    import { when } from "../../lib/utils/format";

    let { failures }: { failures: InboundFailure[] } = $props();

    let pending = $state<number | null>(null);
    let failure = $state<ApiFailure | null>(null);

    async function dismiss(id: number) {
        pending = id;
        failure = null;
        const result = await deleteJson(`/api/Bindings/inbound-failures/${id}`);
        if (result.ok) {
            location.reload();
            return;
        }
        pending = null;
        failure = result.failure;
    }

    const what = (f: InboundFailure) => {
        const change = f.changeType ?? "A change that couldn't be read";
        if (f.recordIds.length === 0) return `${change} · records unknown`;
        if (f.recordIds.length === 1) return `${change} · ${f.recordIds[0]}`;
        return `${change} · ${f.recordIds.length} records`;
    };
</script>

{#if failure}<SalesforceError {failure} title="That didn't work" class="mb-3" />{/if}

<ul class="space-y-3">
    {#each failures as f (f.id)}
        <li class="card space-y-2 border border-error-500/50 p-4 text-sm">
            <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0 space-y-1">
                    <p class="font-semibold">{what(f)}</p>
                    <p class="break-words text-error-700-300">{f.message}</p>
                    <p class="font-mono text-xs text-surface-600-400">
                        last failed {when(f.lastFailedAt)}{#if f.attempts > 1} · {f.attempts} attempts since {when(f.firstFailedAt)}{/if}
                    </p>
                </div>
                <button type="button" class="btn btn-sm hover:preset-tonal" disabled={pending !== null} onclick={() => dismiss(f.id)}>
                    <X class="size-3" />Dismiss
                </button>
            </div>
            {#if f.recordIds.length > 1}
                <details>
                    <summary class="cursor-pointer text-xs text-surface-600-400 hover:underline">Record IDs</summary>
                    <p class="mt-2 break-all font-mono text-xs">{f.recordIds.join(", ")}</p>
                </details>
            {/if}
        </li>
    {/each}
</ul>
