<!-- A Stage page that is not built yet. Rendered inside the shell so the nav and action buttons do not 404. -->
<script lang="ts">
    import { Construction } from "@lucide/svelte";
    import type { PlaceholderView } from "../../lib/types/views";

    let { page }: { page: PlaceholderView } = $props();
</script>

<h1 class="h3 mb-2">{page.title}</h1>
<p class="mb-8 text-surface-600-400">{page.description}</p>

<section class="card flex max-w-3xl items-center gap-4 border border-surface-200-800 bg-surface-50-950 p-6">
    <Construction class="size-8 shrink-0 text-surface-600-400" />
    <div>
        <h2 class="h5">Coming soon</h2>
        <p class="text-sm text-surface-600-400">This page is not built yet. Its Stage Status is shown in the sidebar and on the Overview.</p>
    </div>
</section>
