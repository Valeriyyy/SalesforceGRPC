<!-- Mounts one registered page inside the shared Shell: the envelope's shell goes to Shell, its page to the page. -->
<script lang="ts">
    import type { Component } from "svelte";
    import type { PageView } from "../types/views";
    import Shell from "./Shell.svelte";

    let { page: Page, view }: { page: Component<{ page: any }>; view: PageView<unknown> } = $props();
</script>

<Shell shell={view.shell}>
    <Page page={view.page} />
</Shell>
