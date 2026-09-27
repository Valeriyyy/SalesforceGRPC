<script lang="ts">
    import type { StageStatus } from "./views";
    import { statusMeta } from "./status";
    let { status }: { status: StageStatus } = $props();
    const meta = $derived(statusMeta[status]);
</script>

<span class={["badge gap-1", meta.badge]}><meta.icon />{meta.label}</span>
