<!-- Manual Registration: the steps Self-Configuration left to do by hand when there is a stored outcome, otherwise
     the fixed guide for orgs that do not allow the browser approval. Either way, the certificate to upload. -->
<script lang="ts">
    import { Download } from "@lucide/svelte";
    import { when } from "../../lib/format";
    import type { CertificateView, SelfConfigurationView } from "../../lib/views";

    let { outcome, certificate }: { outcome: SelfConfigurationView | null; certificate: CertificateView | null } = $props();

    const guide = [
        "Upload this application's Signing Certificate (below) into the External Client App's OAuth settings, under Digital Signatures, with the JWT Bearer flow enabled.",
        "Set the app's Permitted Users policy to admin-approved / pre-authorized.",
        "Create a permission set granting API Enabled and access to the External Client App, and assign it to both the Run-as User and the Administering User.",
        "Verify the connection here. Salesforce propagates these changes with a delay, so give it a minute before deciding something is wrong."
    ];
    const steps = $derived(outcome && !outcome.configured ? outcome.manualSteps : guide);
</script>

<div class="space-y-3 text-sm">
    {#if outcome}
        <p class={outcome.configured ? "text-success-600-400" : ""}>
            <span class="font-semibold">Last approval ({when(outcome.at)}):</span> {outcome.summary}
        </p>
    {:else}
        <p>For orgs that don't allow the browser approval. Do these in Salesforce Setup yourself; the end state is the same.</p>
    {/if}
    {#if steps.length}
        <ol class="list-decimal space-y-1 pl-5">
            {#each steps as step}<li>{step}</li>{/each}
        </ol>
    {/if}
    {#if certificate}
        <div class="flex flex-wrap items-center gap-3">
            <a class="btn btn-sm preset-tonal" href={certificate.downloadHref} download><Download class="size-4" /> Download Signing Certificate</a>
            <span class="break-all font-mono text-xs text-surface-600-400">SHA-256 {certificate.fingerprint}</span>
        </div>
    {:else}
        <p class="text-xs text-surface-600-400">The Signing Certificate is generated when you save the connection details.</p>
    {/if}
</div>
