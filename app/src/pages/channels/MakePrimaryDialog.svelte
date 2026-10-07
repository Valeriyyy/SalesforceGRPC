<!-- Making a Channel the Primary Channel. Without a Checkpoint there is nothing to choose: it takes effect at once
     from the Channel's Starting Point, the dialog only showing progress or a failure. With one, the user picks
     Resume (the default, unless it has expired), Earliest or Latest. -->
<script lang="ts">
    import { Star } from "@lucide/svelte";
    import { putJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { ChannelDetailView } from "../../lib/types/views";
    import type { SetPrimaryChannel } from "./requests";

    let { channel }: { channel: ChannelDetailView } = $props();

    let dialog: HTMLDialogElement;
    let start = $state<SetPrimaryChannel["start"]>("Resume");
    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);

    /** With no Checkpoint there is nothing to choose, so it takes effect at once from the Starting Point. */
    export function open() {
        start = channel.checkpoint?.canResume === false ? "Earliest" : "Resume";
        failure = null;
        dialog.showModal();
        if (!channel.checkpoint) {
            confirm();
        }
    }

    async function confirm() {
        pending = true;
        failure = null;
        const result = await putJson("/api/Bindings/primary-channel", { channelId: channel.id, start } satisfies SetPrimaryChannel);
        if (result.ok) {
            location.reload();
            return;
        }
        pending = false;
        failure = result.failure;
    }
</script>

<dialog bind:this={dialog} class="card m-auto w-full max-w-lg border border-surface-200-800 bg-surface-50-950 p-0 backdrop:bg-black/50"
    aria-label="Make Primary Channel">
    <div class="space-y-4 p-6">
        <header class="flex items-center gap-3">
            <Star class="size-6 text-primary-600-400" />
            <h2 class="h5">Make {channel.label} the Primary Channel?</h2>
        </header>
        <p class="text-sm">The worker stops following the current Primary Channel, if there is one, and follows this one instead. The other Channel keeps its Checkpoint.</p>

        {#if channel.checkpoint}
            {@const cp = channel.checkpoint}
            <fieldset class="space-y-2 text-sm">
                <legend class="label-text mb-1">This Channel has a Checkpoint from {cp.age} ago. Where should the worker start?</legend>
                <label class={["flex items-start gap-2", !cp.canResume && "opacity-50"]}>
                    <input class="radio mt-1" type="radio" bind:group={start} value="Resume" disabled={!cp.canResume} />
                    <span>
                        <span class="font-semibold">Resume</span> — pick up from the Checkpoint, so nothing that changed while it was away is missed.
                        {#if !cp.canResume}<span class="block text-warning-700-300">Not possible: Salesforce keeps events for 72 hours, and this Checkpoint is older.</span>{/if}
                    </span>
                </label>
                <label class="flex items-start gap-2">
                    <input class="radio mt-1" type="radio" bind:group={start} value="Earliest" />
                    <span><span class="font-semibold">Earliest</span> — discard the Checkpoint and start from everything Salesforce still keeps.</span>
                </label>
                <label class="flex items-start gap-2">
                    <input class="radio mt-1" type="radio" bind:group={start} value="Latest" />
                    <span><span class="font-semibold">Latest</span> — discard the Checkpoint and take only changes from now on.</span>
                </label>
            </fieldset>
        {:else}
            <p class="text-sm text-surface-600-400">It has no Checkpoint, so the worker starts at its Starting Point: <span class="font-semibold capitalize">{channel.startingPoint}</span>.</p>
        {/if}

        {#if failure}<SalesforceError {failure} />{/if}
    </div>
    <footer class="flex justify-end gap-2 border-t border-surface-200-800 px-6 py-4">
        <button type="button" class="btn preset-tonal" onclick={() => dialog.close()} disabled={pending}>Cancel</button>
        <button type="button" class="btn preset-filled-primary-500" onclick={confirm} disabled={pending}>
            {pending ? "Switching…" : "Make Primary Channel"}
        </button>
    </footer>
</dialog>
