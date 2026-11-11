<!-- The Target Table changes Salesforce refused for this Binding, each with Salesforce's reason. Retry sends the row
     as it is now, so fixing the row and pressing Retry is enough; Dismiss drops a failure the user has decided to
     ignore. A Retry Salesforce still refuses says why in place; anything that changes the list reloads (ADR 0006). -->
<script lang="ts">
    import { RotateCcw, X } from "@lucide/svelte";
    import { deleteJson, postJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { WritebackFailure, WritebackRetry } from "../../lib/types/views";
    import { when } from "../../lib/utils/format";

    let { failures, disabled }: { failures: WritebackFailure[]; disabled: boolean } = $props();

    let pending = $state<number | null>(null);
    let stillRefused = $state<Record<number, string>>({});
    let failure = $state<ApiFailure | null>(null);

    async function retry(id: number) {
        pending = id;
        failure = null;
        const result = await postJson<WritebackRetry>(`/api/Bindings/writeback-failures/${id}/retry`);
        if (result.ok && result.value.outcome !== "Failed") {
            location.reload();
            return;
        }
        pending = null;
        if (result.ok) {
            stillRefused = { ...stillRefused, [id]: result.value.message ?? "Salesforce still refused it." };
        } else {
            failure = result.failure;
        }
    }

    async function dismiss(id: number) {
        pending = id;
        failure = null;
        const result = await deleteJson(`/api/Bindings/writeback-failures/${id}`);
        if (result.ok) {
            location.reload();
            return;
        }
        pending = null;
        failure = result.failure;
    }

    /** The row, named the way the user will look for it in the table. */
    const rowName = (f: WritebackFailure) => f.writebackKeyValue !== null ? `Row ${f.writebackKeyValue}` : `Record ${f.salesforceId}`;

    function sent(f: WritebackFailure) {
        try {
            return Object.entries(JSON.parse(f.valuesSent) as Record<string, unknown>);
        } catch {
            return [];
        }
    }
</script>

{#if failure}<SalesforceError {failure} title="That didn't work" class="mb-3" />{/if}

<ul class="space-y-3">
    {#each failures as f (f.id)}
        <li class="card space-y-2 border border-error-500/50 p-4 text-sm">
            <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0 space-y-1">
                    <p class="font-semibold">{rowName(f)} · {f.operation === "Create" ? "creating a record" : "updating the record"}</p>
                    <p class="text-error-700-300">{f.message}</p>
                    <p class="font-mono text-xs text-surface-600-400">
                        {f.statusCodes} · last failed {when(f.lastFailedAt)}{#if f.attempts > 1} · {f.attempts} attempts since {when(f.firstFailedAt)}{/if}
                    </p>
                </div>
                <div class="flex gap-2">
                    <button type="button" class="btn btn-sm preset-tonal" disabled={disabled || pending !== null} onclick={() => retry(f.id)}>
                        <RotateCcw class={["size-3", pending === f.id && "animate-spin"]} />Retry
                    </button>
                    <button type="button" class="btn btn-sm hover:preset-tonal" disabled={disabled || pending !== null} onclick={() => dismiss(f.id)}>
                        <X class="size-3" />Dismiss
                    </button>
                </div>
            </div>
            {#if stillRefused[f.id]}<p class="text-warning-700-300">Retried, and Salesforce still refused it: {stillRefused[f.id]}</p>{/if}
            {#if sent(f).length > 0}
                <details>
                    <summary class="cursor-pointer text-xs text-surface-600-400 hover:underline">What was sent</summary>
                    <dl class="mt-2 grid grid-cols-[auto_1fr] gap-x-4 font-mono text-xs">
                        {#each sent(f) as [field, value]}<dt class="text-surface-600-400">{field}</dt><dd class="break-all">{JSON.stringify(value)}</dd>{/each}
                    </dl>
                </details>
            {/if}
        </li>
    {/each}
</ul>
