<!-- What the user does in Salesforce Setup before anything else. None of it can be detected from here. -->
<script lang="ts">
    import CopyField from "../../lib/CopyField.svelte";

    let { callbackUrl }: { callbackUrl: string } = $props();
</script>

<ol class="list-decimal space-y-3 pl-5 text-sm">
    <li>In Salesforce Setup, search for <strong>External Client App Settings</strong> and turn on <strong>Allow creation of External Client Apps</strong>.</li>
    <li>
        Create a new <strong>External Client App</strong>. Enable OAuth, and paste this as its callback URL:
        <div class="mt-2 max-w-2xl">
            {#if callbackUrl}
                <CopyField label="Callback URL" value={callbackUrl} hint="Paste it exactly as shown. A mismatch is what Salesforce reports as redirect_uri_mismatch." />
            {:else}
                <p class="text-error-600-400">No callback URL is configured for this application. Set SalesforceConfig:CallbackUrl and restart.</p>
            {/if}
        </div>
    </li>
    <li>Select the scopes <code class="code">api</code>, <code class="code">refresh_token</code> and <code class="code">web</code>, save, then open <strong>Consumer Key and Secret</strong>. You'll paste both into the next step.</li>
</ol>
