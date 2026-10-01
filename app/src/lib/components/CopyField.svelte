<!-- A read-only value with a copy button, for text the user pastes somewhere else verbatim. -->
<script lang="ts">
    import { Check, Copy } from "@lucide/svelte";
    import { copyText } from "../utils/clipboard";

    let { label, value, hint = "" }: { label: string; value: string; hint?: string } = $props();

    let copied = $state(false);
</script>

<div class="space-y-1">
    <span class="label-text">{label}</span>
    <div class="flex w-full gap-2">
        <input class="input min-w-0 flex-1 font-mono text-xs" readonly {value} aria-label={label} onfocus={e => e.currentTarget.select()} />
        <button type="button" class="btn-icon preset-tonal" onclick={() => copyText(value, c => copied = c)} aria-label="Copy {label}">
            {#if copied}<Check class="size-4" />{:else}<Copy class="size-4" />{/if}
        </button>
    </div>
    {#if hint}<p class="text-xs text-surface-600-400">{hint}</p>{/if}
</div>
