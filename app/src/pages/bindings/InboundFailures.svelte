<!-- The change events that couldn't be applied to this Binding's Target Table. Each says what went wrong in the
     Binding's terms (the field, the column, the sizes) and how to fix the cause; the database's own words and the
     values that were refused are under Details. There is no Retry: StarPush doesn't repair data, so the user corrects
     the record some other way and then Dismisses the failure. Dismissing reloads the page (ADR 0006). -->
<script lang="ts">
    import { Wrench, X } from "@lucide/svelte";
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

    const changeName: Record<string, string> = { CREATE: "Create", UPDATE: "Update", DELETE: "Delete", UNDELETE: "Undelete" };

    const what = (f: InboundFailure) => {
        const change = f.changeType ? `${changeName[f.changeType] ?? f.changeType} of` : "A change to";
        if (f.recordIds.length === 0) return `${change} unknown records`;
        if (f.recordIds.length === 1) return `${change} ${f.recordIds[0]}`;
        return `${change} ${f.recordIds.length} records`;
    };

    function sent(f: InboundFailure): [string, unknown][] {
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
        <li class="card space-y-3 border border-error-500/50 p-4 text-sm">
            <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0 space-y-1">
                    <p class="font-semibold">{what(f)} wasn't applied</p>
                    <p class="text-xs text-surface-600-400">
                        Last failed {when(f.lastFailedAt)}{#if f.attempts > 1} · {f.attempts} attempts since {when(f.firstFailedAt)}{/if}
                    </p>
                </div>
                <button type="button" class="btn btn-sm hover:preset-tonal" disabled={pending !== null} onclick={() => dismiss(f.id)}>
                    <X class="size-3" />Dismiss
                </button>
            </div>

            <div class="space-y-1">
                <p class="text-xs font-semibold uppercase tracking-wide text-surface-600-400">What went wrong</p>
                <p class="break-words text-error-700-300">{f.problem}</p>
            </div>

            <div class="space-y-1">
                <p class="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-surface-600-400"><Wrench class="size-3" />How to fix it</p>
                <ol class="list-decimal space-y-1 pl-5">
                    {#each f.steps as step}<li class="break-words">{step}</li>{/each}
                </ol>
            </div>

            <details>
                <summary class="cursor-pointer text-xs text-surface-600-400 hover:underline">Details</summary>
                <div class="mt-2 space-y-2">
                    <p class="break-all font-mono text-xs">{f.message}</p>
                    {#if f.recordIds.length > 1}<p class="break-all font-mono text-xs">Records: {f.recordIds.join(", ")}</p>{/if}
                    {#if sent(f).length > 0}
                        <dl class="grid grid-cols-[auto_1fr] gap-x-4 font-mono text-xs">
                            {#each sent(f) as [column, value]}<dt class="text-surface-600-400">{column}</dt><dd class="break-all">{JSON.stringify(value)}</dd>{/each}
                        </dl>
                    {/if}
                </div>
            </details>
        </li>
    {/each}
</ul>
