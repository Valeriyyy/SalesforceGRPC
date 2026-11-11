<!-- The Replication Credential: a second login on the same database, used only to read its changes for Writeback,
     so the login that writes rows never needs replication rights. Only needed once a Binding is Two-way. Saving
     proves it and reloads; a credential edit never repoints. -->
<script lang="ts">
    import { CircleCheck, CircleX, Hourglass, KeyRound, RefreshCw, Trash2 } from "@lucide/svelte";
    import { deleteJson, postJson, putJson, type ApiFailure } from "../../lib/api/client";
    import SalesforceError from "../../lib/components/SalesforceError.svelte";
    import type { TargetConnectionDetailsView } from "../../lib/types/views";
    import { when } from "../../lib/utils/format";

    let { connection, disabled }: { connection: TargetConnectionDetailsView; disabled: boolean } = $props();

    const replication = $derived(connection.replication);

    let editing = $state(false);
    let username = $state("");
    let password = $state("");
    let pending = $state(false);
    let failure = $state<ApiFailure | null>(null);

    const head = $derived(replication === null ? null : {
        Connected: { icon: CircleCheck, tone: "text-success-600-400", title: "Proved" },
        Failed: { icon: CircleX, tone: "text-error-600-400", title: "Stopped working" },
        Incomplete: { icon: Hourglass, tone: "text-warning-600-400", title: "Not proved yet" }
    }[replication.state] ?? { icon: Hourglass, tone: "text-warning-600-400", title: replication.state });

    function edit() {
        username = replication?.username ?? "";
        password = "";
        failure = null;
        editing = true;
    }

    async function run(request: () => ReturnType<typeof postJson>) {
        pending = true;
        failure = null;
        const result = await request();
        if (result.ok) {
            location.reload();
            return;
        }
        pending = false;
        failure = result.failure;
    }

    const save = () => run(() => putJson("/api/targetconnection/replication-credential", { username, password: password || null }));
    const verify = () => run(() => postJson("/api/targetconnection/replication-credential/verify"));
    const remove = () => run(() => deleteJson("/api/targetconnection/replication-credential"));
</script>

<section class="card mt-6 space-y-3 border border-surface-200-800 p-5">
    <h2 class="h6 flex items-center gap-2"><KeyRound class="size-4" />Replication Credential</h2>
    <p class="text-sm text-surface-600-400">
        Needed only for Two-way Bindings. Writeback reads the database's changes with this second login, so the login above never needs
        replication rights. It needs the REPLICATION attribute and SELECT on the Two-way tables. The server needs Postgres 16 or later
        with <code class="code">wal_level = logical</code>.
    </p>

    {#if replication && !editing}
        <div class="flex flex-wrap items-start gap-4">
            <div class="min-w-0 flex-1 text-sm">
                <p class={["flex items-center gap-1.5 font-semibold", head?.tone]}>{#if head}<head.icon class="size-4" />{head.title}{/if}</p>
                <p class="font-mono">{replication.username}</p>
                {#if replication.lastProvedAt}<p class="text-xs text-surface-600-400">Last proved {when(replication.lastProvedAt)}</p>{/if}
                {#if replication.lastError}<p class="mt-1 text-error-700-300">{replication.lastError}</p>{/if}
                {#if connection.writebackArmed}<p class="mt-1 text-xs text-surface-600-400">Writeback is reading this database's changes.</p>{/if}
            </div>
            <div class="flex gap-2">
                <button type="button" class="btn btn-sm preset-tonal" disabled={disabled || pending} onclick={verify}>
                    <RefreshCw class={["size-3", pending && "animate-spin"]} />Verify
                </button>
                <button type="button" class="btn btn-sm hover:preset-tonal" disabled={disabled || pending} onclick={edit}>Change</button>
                <button type="button" class="btn btn-sm hover:preset-tonal-error" disabled={disabled || pending} onclick={remove}>
                    <Trash2 class="size-3" />Remove
                </button>
            </div>
        </div>
    {:else if editing || !replication}
        {#if !editing}
            <button type="button" class="btn btn-sm preset-tonal" disabled={disabled} onclick={edit}>Enter a Replication Credential</button>
        {:else}
            <form class="grid max-w-md gap-3 text-sm" onsubmit={e => { e.preventDefault(); save(); }}>
                <label class="label">
                    <span class="label-text">Username</span>
                    <input class="input" autocomplete="off" required bind:value={username} disabled={pending} />
                </label>
                <label class="label">
                    <span class="label-text">Password</span>
                    <input class="input" type="password" autocomplete="new-password" bind:value={password} disabled={pending}
                        placeholder={replication && username === replication.username ? "Leave blank to keep the stored one" : ""} />
                </label>
                <div class="flex gap-2">
                    <button type="submit" class="btn preset-filled-primary-500" disabled={pending || !username.trim()}>
                        {pending ? "Proving…" : "Save and prove"}
                    </button>
                    <button type="button" class="btn hover:preset-tonal" disabled={pending} onclick={() => editing = false}>Cancel</button>
                </div>
            </form>
        {/if}
    {/if}

    {#if failure}<SalesforceError {failure} title="That didn't work" />{/if}
</section>
