<!-- What the user does in Salesforce Setup before anything else. None of it can be detected from here. -->
<script lang="ts">
    import CopyField from "../../lib/components/CopyField.svelte";

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
    <li>
        Under <strong>Selected OAuth Scopes</strong>, add <strong>Manage user data via APIs (api)</strong> and
        <strong>Perform requests at any time (refresh_token, offline_access)</strong>. Both are required: without
        them Salesforce refuses the approval with <code class="code">invalid_scope</code>. Save, then open
        <strong>Consumer Key and Secret</strong>. You'll paste both into the next step.
    </li>
</ol>
