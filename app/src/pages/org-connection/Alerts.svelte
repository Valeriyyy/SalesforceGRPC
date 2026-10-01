<!-- Above the page: the blocking Secret Protection alert, and the one-time notice from the way back from Salesforce. -->
<script lang="ts">
    import { CircleCheck, ShieldAlert, X } from "@lucide/svelte";
    import type { ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { OrgConnectionNotice, SecretProtectionView } from "../../lib/types/views";

    let { secretProtection, notice }: { secretProtection: SecretProtectionView; notice: OrgConnectionNotice | null } = $props();

    let dismissed = $state(false);

    function failureOf(n: OrgConnectionNotice): ApiFailure {
        if (n.orgMismatch) {
            return { kind: "orgMismatch", message: n.message, ...n.orgMismatch };
        }
        return n.error ? { kind: "salesforce", error: n.error } : { kind: "message", message: n.message };
    }
</script>

{#if secretProtection.status !== "ready"}
    <div class="card mb-6 flex gap-3 border border-error-500 bg-error-50-950 p-4 text-sm" role="alert">
        <ShieldAlert class="size-6 shrink-0 text-error-600-400" />
        <div class="space-y-1">
            <p class="font-semibold">
                {secretProtection.status === "unreadable" ? "Stored credentials can't be decrypted" : "This application can't store credentials yet"}
            </p>
            <p>{secretProtection.guidance}</p>
            <p class="text-xs text-surface-600-400">Protecting key: {secretProtection.protectingKey}</p>
        </div>
    </div>
{/if}

{#if notice && !dismissed}
    <div class="relative mb-6">
        {#if notice.kind === "success"}
            <div class="card flex items-center gap-3 border border-success-500 bg-success-50-950 p-3 pr-12 text-sm" role="status">
                <CircleCheck class="size-5 shrink-0 text-success-600-400" />
                <p>{notice.message}</p>
            </div>
        {:else}
            <SalesforceError failure={failureOf(notice)} title={notice.message} class="pr-12" />
        {/if}
        <button type="button" class="btn-icon btn-icon-sm absolute right-2 top-2 hover:preset-tonal" onclick={() => dismissed = true}
            aria-label="Dismiss"><X class="size-4" /></button>
    </div>
{/if}
