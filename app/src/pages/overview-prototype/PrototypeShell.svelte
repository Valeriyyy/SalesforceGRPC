<!-- PROTOTYPE — the left-nav shell shared by every variant. -->
<script lang="ts">
    import type { Snippet } from "svelte";
    import { Navigation } from "@skeletonlabs/skeleton-svelte";
    import { LayoutDashboard, Moon, Sun } from "@lucide/svelte";
    import type { ShellView } from "./views";
    import { statusMeta } from "./status";

    let { shell, children }: { shell: ShellView; children: Snippet } = $props();

    let mode = $state(document.documentElement.dataset.mode === "dark" ? "dark" : "light");

    function toggleMode() {
        mode = mode === "dark" ? "light" : "dark";
        document.documentElement.dataset.mode = mode;
        try { localStorage.setItem("mode", mode); } catch { }
    }
</script>

<div class="flex h-screen">
    <Navigation layout="sidebar" class="shrink-0 flex flex-col border-r border-surface-200-800">
        <Navigation.Header class="px-2 pb-6 pt-2">
            <span class="text-lg font-bold tracking-tight">{shell.appName}</span>
        </Navigation.Header>
        <Navigation.Content class="flex-1">
            <Navigation.Menu>
                {#each shell.nav as item (item.key)}
                    {@const current = item.key === shell.currentNav}
                    <Navigation.TriggerAnchor
                        href={item.href}
                        aria-current={current ? "page" : undefined}
                        class={["gap-3", current && "preset-tonal-primary"]}
                        title={item.status ? `${item.label}: ${statusMeta[item.status].label}` : item.label}>
                        {#if item.stepNumber}
                            <span class="grid size-6 shrink-0 place-items-center rounded-full border border-surface-400-600 text-xs font-semibold">
                                {item.stepNumber}
                            </span>
                        {:else}
                            <LayoutDashboard class="size-6 shrink-0 p-0.5" />
                        {/if}
                        <Navigation.TriggerText class="flex-1 text-left">{item.label}</Navigation.TriggerText>
                        {#if item.status}
                            {@const meta = statusMeta[item.status]}
                            <meta.icon class={["size-5 shrink-0", meta.text]} aria-label={meta.label} />
                        {/if}
                    </Navigation.TriggerAnchor>
                    {#if item.key === "overview"}
                        <hr class="hr my-2" />
                    {/if}
                {/each}
            </Navigation.Menu>
        </Navigation.Content>
        <Navigation.Footer class="pt-4">
            <button type="button" class="btn w-full justify-start gap-3 hover:preset-tonal" onclick={toggleMode}>
                {#if mode === "dark"}<Sun class="size-5" />Light mode{:else}<Moon class="size-5" />Dark mode{/if}
            </button>
        </Navigation.Footer>
    </Navigation>

    <main class="flex-1 overflow-y-auto">
        <div class="max-w-[112.5rem] px-10 py-8">
            {@render children()}
        </div>
    </main>
</div>
