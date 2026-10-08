<!-- The stored connection: its state, address and details, with Verify and Edit, and the last error when it isn't
     working. -->
<script lang="ts">
    import { CircleCheck, CircleX, Hourglass, Pencil, RefreshCw } from "@lucide/svelte";
    import type { ApiFailure } from "../../lib/api/client";
    import DatabaseError from "../../lib/components/DatabaseError.svelte";
    import type { TargetConnectionDetailsView, TargetConnectionView } from "../../lib/types/views";
    import { when } from "../../lib/utils/format";
    import EngineMark from "./EngineMark.svelte";
    import { addressOf, isOption, presentationOf } from "./engines";

    let { page, connection, canEdit, pending, error, onverify, onedit }: {
        page: TargetConnectionView;
        connection: TargetConnectionDetailsView;
        canEdit: boolean;
        pending: boolean;
        error: ApiFailure | null;
        onverify: () => void;
        onedit: () => void;
    } = $props();

    const engine = $derived(page.engines.find(e => e.engine === connection.engine));
    const options = $derived(engine?.fields.filter(isOption) ?? []);
    const asks = (name: string) => engine?.fields.some(f => f.name === name) ?? false;
    const head = $derived({
        connected: { icon: CircleCheck, tone: "text-success-600-400", title: "Connected" },
        failed: { icon: CircleX, tone: "text-error-600-400", title: "Connection failed" },
        incomplete: { icon: Hourglass, tone: "text-warning-600-400", title: "Saved, but not working yet" },
        notStarted: { icon: Hourglass, tone: "text-surface-600-400", title: "Not set up" }
    }[page.phase]);

    function optionValue(name: string, kind: string, fallback: string | null) {
        const value = connection.options[name] ?? fallback;
        if (kind === "bool") {
            return value === "true" ? "Yes" : "No";
        }
        return value || "Driver default";
    }
</script>

<section class={["card border p-5", page.phase === "failed" ? "border-error-500" : "border-surface-200-800 bg-surface-50-950"]}>
    <div class="flex flex-wrap items-start gap-4">
        <EngineMark engine={connection.engine} size="lg" />
        <div class="min-w-0 flex-1">
            <h2 class={["flex items-center gap-1.5 font-semibold", head.tone]}><head.icon class="size-5" />{head.title}</h2>
            <p class="break-all font-mono text-sm">{addressOf(connection)}</p>
            <p class="text-xs text-surface-600-400">
                {presentationOf(connection.engine).label}{#if connection.lastConnectedAt} · last worked {when(connection.lastConnectedAt)}{/if}
            </p>
        </div>
        <div class="flex gap-2">
            <button type="button" class={["btn", page.phase === "connected" ? "preset-tonal" : "preset-filled-primary-500"]}
                disabled={pending} onclick={onverify}>
                <RefreshCw class={["size-4", pending && "animate-spin"]} />{pending ? "Verifying…" : "Verify"}
            </button>
            {#if canEdit}
                <button type="button" class="btn hover:preset-tonal" disabled={pending} onclick={onedit}><Pencil class="size-4" />Edit</button>
            {/if}
        </div>
    </div>

    {#if connection.lastError && page.phase !== "connected"}<DatabaseError failure={connection.lastError} class="mt-4" />{/if}
    {#if error}<DatabaseError failure={error} class="mt-4" />{/if}

    <dl class="mt-5 grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 border-t border-surface-200-800 pt-4 text-sm">
        {#if asks("username")}<dt class="text-surface-600-400">Username</dt><dd>{connection.username ?? "—"}</dd>{/if}
        {#if asks("password")}
            <dt class="text-surface-600-400">Password</dt><dd>{connection.hasPassword ? "Stored, encrypted" : "None stored"}</dd>
        {/if}
        {#each options as f (f.name)}
            <dt class="text-surface-600-400">{f.label}</dt><dd>{optionValue(f.name, f.kind, f.default)}</dd>
        {/each}
        <dt class="text-surface-600-400">Bindings writing here</dt>
        <dd><a class="anchor" href="/bindings">{page.bindings}</a></dd>
    </dl>
</section>
