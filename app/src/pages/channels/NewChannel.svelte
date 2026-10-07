<!-- Creating a Channel. Always Change Data Capture; the Full Name follows the Label until edited, checked with the
     server's own rule as it is typed. On success the new Channel's page opens, where its Entities are added. -->
<script lang="ts">
    import { untrack } from "svelte";
    import { ArrowLeft } from "@lucide/svelte";
    import { postJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { NewChannelView } from "../../lib/types/views";
    import WaitingOnOrg from "./WaitingOnOrg.svelte";
    import { ChannelSuffix, developerNameFor, developerNameProblem, type NewChannel, type NewChannelResult } from "./requests";

    let { page }: { page: NewChannelView } = $props();

    let label = $state("");
    let name = $state("");
    let nameEdited = $state(false);
    let startingPoint = $state<"Latest" | "Earliest">("Latest");
    let makePrimary = $state(untrack(() => page.makePrimaryByDefault));
    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);

    const derivedName = $derived(nameEdited ? name : developerNameFor(label));
    const problem = $derived(derivedName.length === 0 && !nameEdited ? null : developerNameProblem(derivedName));

    function editName(value: string) {
        nameEdited = true;
        name = value;
    }

    async function submit(e: SubmitEvent) {
        e.preventDefault();
        if (developerNameProblem(derivedName)) {
            return;
        }
        pending = true;
        failure = null;
        const result = await postJson<NewChannelResult>("/api/PlatformEventManagement/channels/data", {
            fullName: derivedName + ChannelSuffix,
            label: label.trim(),
            startingPoint,
            makePrimary
        } satisfies NewChannel);
        if (result.ok) {
            location.href = `/channels/${result.value.channelId}`;
            return;
        }
        pending = false;
        failure = result.failure;
    }
</script>

<a href="/channels" class="anchor mb-4 inline-flex items-center gap-1 text-sm"><ArrowLeft class="size-4" />Channels</a>
<h1 class="h3 mb-2">New Channel</h1>
<p class="mb-6 text-surface-600-400">A Change Data Capture Channel. You'll choose the Entities it carries on its page next.</p>

{#if !page.orgConnectionReady}
    <WaitingOnOrg />
{:else}
    <form class="card max-w-2xl space-y-5 border border-surface-200-800 bg-surface-50-950 p-6" onsubmit={submit}>
        <label class="label">
            <span class="label-text">Label</span>
            <input class="input" required maxlength="80" bind:value={label} placeholder="Sales Events" />
            <span class="text-xs text-surface-600-400">Shown in Salesforce Setup. You can change it later.</span>
        </label>

        <label class="label">
            <span class="label-text">Full Name</span>
            <div class="input-group grid-cols-[1fr_auto]">
                <input class="ig-input font-mono" required value={derivedName} oninput={e => editName(e.currentTarget.value)}
                    autocomplete="off" spellcheck="false" aria-invalid={problem !== null} />
                <div class="ig-cell preset-tonal font-mono">{ChannelSuffix}</div>
            </div>
            {#if problem}
                <span class="text-xs text-error-600-400">{problem}</span>
            {:else}
                <span class="text-xs text-surface-600-400">The API name. Salesforce fixes it once the Channel exists.</span>
            {/if}
        </label>

        <fieldset class="space-y-2">
            <legend class="label-text mb-1">Starting Point</legend>
            <label class="flex items-start gap-2">
                <input class="radio mt-1" type="radio" bind:group={startingPoint} value="Latest" />
                <span><span class="font-semibold">Latest</span> <span class="text-sm text-surface-600-400">— only changes from now on</span></span>
            </label>
            <label class="flex items-start gap-2">
                <input class="radio mt-1" type="radio" bind:group={startingPoint} value="Earliest" />
                <span><span class="font-semibold">Earliest</span> <span class="text-sm text-surface-600-400">— everything Salesforce still keeps, up to 72 hours back</span></span>
            </label>
            <p class="text-xs text-surface-600-400">Used only until the worker saves its first Checkpoint for this Channel.</p>
        </fieldset>

        <label class="flex items-start gap-2">
            <input class="checkbox mt-1" type="checkbox" bind:checked={makePrimary} />
            <span>
                Make this the Primary Channel
                <span class="block text-sm text-surface-600-400">
                    {#if page.primaryChannelFullName}
                        The worker follows <span class="font-mono">{page.primaryChannelFullName}</span> now; this would replace it.
                    {:else}
                        There is no Primary Channel yet, so nothing is streaming.
                    {/if}
                </span>
            </span>
        </label>

        {#if failure}<SalesforceError {failure} title="The Channel was not created" />{/if}

        <div class="flex justify-end gap-2">
            <a href="/channels" class="btn preset-tonal">Cancel</a>
            <button type="submit" class="btn preset-filled-primary-500" disabled={pending || problem !== null || derivedName.length === 0}>
                {pending ? "Creating…" : "Create Channel"}
            </button>
        </div>
    </form>
{/if}
