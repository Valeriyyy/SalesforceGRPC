<!-- A Target Database failure: the summary in plain words, then the driver's own text behind a disclosure with a copy
     button — the only text a user can search for or hand to a DBA. Takes an API failure or a stored last error. -->
<script lang="ts">
    import { Check, CircleX, Copy } from "@lucide/svelte";
    import type { ApiFailure } from "../api/client";
    import type { TargetDatabaseErrorView } from "../types/views";
    import { copyText } from "../utils/clipboard";
    import { when } from "../utils/format";

    let { failure, title = "", class: cls = "" }:
        { failure: ApiFailure | TargetDatabaseErrorView; title?: string; class?: string } = $props();

    let copied = $state(false);

    const shown = $derived.by(() => {
        if (!("kind" in failure)) {
            return failure;
        }
        if (failure.kind === "database") {
            return { message: failure.message, rawResponse: failure.rawResponse, occurredAt: null };
        }
        if (failure.kind === "salesforce") {
            return { message: failure.error.errorDescription || failure.error.error, rawResponse: failure.error.rawResponse, occurredAt: null };
        }
        return { message: failure.message, rawResponse: "", occurredAt: null };
    });
</script>

<div class={["card flex gap-3 border border-error-500 bg-error-50-950 p-4 text-sm", cls]} role="alert">
    <CircleX class="size-5 shrink-0 text-error-600-400" />
    <div class="min-w-0 flex-1 space-y-2">
        {#if title}<p class="font-semibold">{title}</p>{/if}
        <p class={[!title && "font-semibold"]}>
            {shown.message}{#if shown.occurredAt}<span class="font-normal text-surface-600-400"> · {when(shown.occurredAt)}</span>{/if}
        </p>
        {#if shown.rawResponse}
            <details>
                <summary class="cursor-pointer text-xs text-surface-600-400 hover:underline">What the database said</summary>
                <div class="relative mt-2">
                    <pre class="pre overflow-x-auto whitespace-pre-wrap break-all pr-10 text-xs">{shown.rawResponse}</pre>
                    <button type="button" class="btn-icon btn-icon-sm absolute right-1 top-1 hover:preset-tonal"
                        onclick={() => copyText(shown.rawResponse, c => copied = c)} aria-label="Copy what the database said">
                        {#if copied}<Check class="size-4" />{:else}<Copy class="size-4" />{/if}
                    </button>
                </div>
            </details>
        {/if}
    </div>
</div>
