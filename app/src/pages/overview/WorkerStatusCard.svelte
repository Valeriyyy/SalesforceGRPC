<!-- Reserved for the worker's runtime status: always present at a fixed size, so the layout does not jump when it arrives. -->
<script lang="ts">
    import { Activity } from "@lucide/svelte";
    let { focus = false, class: klass = "" }: { focus?: boolean; class?: string } = $props();
</script>

<section class={["card flex h-44 flex-col justify-center gap-2 p-6 border", focus ? "preset-outlined-primary-500" : "border-surface-200-800 bg-surface-50-950", klass]}>
    <h2 class="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-surface-600-400">
        <Activity class="size-4" /> Worker status
    </h2>
    <p class="text-lg">Not reported yet</p>
    <p class="text-sm text-surface-600-400">The worker does not publish its runtime state yet, so this is not a sign that it is running.</p>
</section>
