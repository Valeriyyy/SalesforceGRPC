<!-- Every Channel action needs the Administering User, so until the Org Connection is OK the page offers nothing. -->
<script lang="ts">
    import { Hourglass } from "@lucide/svelte";
</script>

<section class="card flex max-w-3xl items-center gap-4 border border-surface-200-800 bg-surface-50-950 p-6">
    <Hourglass class="size-8 shrink-0 text-surface-600-400" />
    <div class="space-y-1">
        <h2 class="h5">Waiting on Org Connection</h2>
        <p class="text-sm text-surface-600-400">Channels live in Salesforce, so they can only be managed once this application is connected to your org.</p>
        <a href="/org-connection" class="anchor text-sm">Set up the Org Connection</a>
    </div>
</section>
