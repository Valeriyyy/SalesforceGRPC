<!-- PROTOTYPE — floating bar. ←/→ cycles the variant, Shift+←/→ cycles the Pipeline state. -->
<script lang="ts">
    import { ChevronLeft, ChevronRight } from "@lucide/svelte";

    type Cycler = { title: string; keys: string[]; labels: Record<string, string>; current: string; set: (key: string) => void };
    let { variant, pipeline }: { variant: Cycler; pipeline: Cycler } = $props();

    function step(c: Cycler, by: number) {
        const i = c.keys.indexOf(c.current);
        c.set(c.keys[(i + by + c.keys.length) % c.keys.length]);
    }

    function onkeydown(e: KeyboardEvent) {
        const t = e.target as HTMLElement;
        if (t.closest("input, textarea, [contenteditable]")) return;
        if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
        step(e.shiftKey ? pipeline : variant, e.key === "ArrowLeft" ? -1 : 1);
    }
</script>

<svelte:window {onkeydown} />

{#if import.meta.env.DEV}
    <div class="fixed bottom-4 left-1/2 z-50 flex -translate-x-1/2 gap-6 rounded-full bg-black/90 px-5 py-2 text-sm text-white shadow-2xl">
        {#each [variant, pipeline] as c (c.title)}
            <div class="flex items-center gap-2">
                <span class="text-xs uppercase tracking-wide text-white/50">{c.title}</span>
                <button type="button" class="rounded-full p-1 hover:bg-white/20" onclick={() => step(c, -1)} aria-label="Previous {c.title}"><ChevronLeft class="size-4" /></button>
                <span class="min-w-48 text-center">{c.labels[c.current]}</span>
                <button type="button" class="rounded-full p-1 hover:bg-white/20" onclick={() => step(c, 1)} aria-label="Next {c.title}"><ChevronRight class="size-4" /></button>
            </div>
        {/each}
    </div>
{/if}
