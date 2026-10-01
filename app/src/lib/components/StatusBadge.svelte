<script lang="ts">
    import type { StageStatus } from "../types/views";
    import { statusMeta } from "../utils/status";
    let { status }: { status: StageStatus } = $props();
    const meta = $derived(statusMeta[status]);
</script>

<span class={["badge gap-1", meta.badge]}><meta.icon />{meta.label}</span>
