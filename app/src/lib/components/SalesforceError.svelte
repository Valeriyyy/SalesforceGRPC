<!-- Every failure shown the same way: what it means in plain words, then Salesforce's code and description, then
     the raw response behind a disclosure — the only text a user can search for when the guidance has nothing. -->
<script lang="ts">
    import { Check, CircleAlert, Copy } from "@lucide/svelte";
    import type { ApiFailure } from "../api/client";
    import { copyText } from "../utils/clipboard";
    import { when } from "../utils/format";

    let { failure, title = "Salesforce refused the request", class: cls = "" }:
        { failure: ApiFailure; title?: string; class?: string } = $props();

    let copied = $state(false);
</script>

<div class={["card flex gap-3 border border-error-500 bg-error-50-950 p-4 text-sm", cls]} role="alert">
    <CircleAlert class="size-5 shrink-0 text-error-600-400" />
    <div class="min-w-0 flex-1 space-y-2">
        {#if failure.kind === "salesforce"}
            {@const e = failure.error}
            <p class="font-semibold">{title}</p>
            <p>{e.guidance ?? "Salesforce returned an error this application doesn't recognise. The raw response below is what to search for or send to support."}</p>
            <p class="break-words font-mono text-xs">
                <span class="font-semibold">{e.error}</span>{#if e.errorDescription}: {e.errorDescription}{/if}
                {#if e.occurredAt}<span class="text-surface-600-400"> · {when(e.occurredAt)}</span>{/if}
            </p>
            {#if e.rawResponse}
                <details>
                    <summary class="cursor-pointer text-xs text-surface-600-400 hover:underline">Raw Salesforce response</summary>
                    <div class="relative mt-2">
                        <pre class="pre overflow-x-auto whitespace-pre-wrap break-all pr-10 text-xs">{e.rawResponse}</pre>
                        <button type="button" class="btn-icon btn-icon-sm absolute right-1 top-1 hover:preset-tonal"
                            onclick={() => copyText(e.rawResponse, c => copied = c)} aria-label="Copy raw response">
                            {#if copied}<Check class="size-4" />{:else}<Copy class="size-4" />{/if}
                        </button>
                    </div>
                </details>
            {/if}
        {:else if failure.kind === "orgMismatch"}
            <p class="font-semibold">That user is in a different org</p>
            <p>{failure.message}</p>
            <dl class="grid grid-cols-[auto_1fr] gap-x-4 font-mono text-xs">
                <dt class="text-surface-600-400">Connected org</dt><dd>{failure.storedOrgId}</dd>
                <dt class="text-surface-600-400">That user's org</dt><dd>{failure.discoveredOrgId}</dd>
            </dl>
            <a href="/org-connection#disconnect" class="anchor text-xs">Go to Disconnect</a>
        {:else}
            <p>{failure.message}</p>
        {/if}
    </div>
</div>
