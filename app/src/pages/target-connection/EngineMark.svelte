<!-- An engine's monogram, standing in for its logo. -->
<script lang="ts">
    import { presentationOf } from "./engines";

    let { engine, size = "md" }: { engine: string; size?: "md" | "lg" } = $props();

    const p = $derived(presentationOf(engine));
</script>

<span class={["grid shrink-0 place-items-center rounded-lg font-bold tracking-tight", size === "lg" ? "size-12 text-sm" : "size-10 text-xs", p.tone]}
    aria-hidden="true">{p.mark}</span>
