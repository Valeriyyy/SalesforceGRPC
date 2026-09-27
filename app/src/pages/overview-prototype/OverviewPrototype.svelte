<!--
    PROTOTYPE — throwaway. Question: what should the shell and Overview look like, across every Pipeline state?
    Three variants of the Overview on /prototype/overview, switchable via ?variant=A|B|C, and the fixture state via
    ?state=fresh|half|allOk|degraded|degradedStrict. Delete once the design is settled (see .scratch/ui-shell/spec.md).
-->
<script lang="ts">
    import PrototypeShell from "./PrototypeShell.svelte";
    import PrototypeSwitcher from "./PrototypeSwitcher.svelte";
    import VariantA, { name as nameA } from "./VariantA.svelte";
    import VariantB, { name as nameB } from "./VariantB.svelte";
    import VariantC, { name as nameC } from "./VariantC.svelte";
    import { fixture, fixtureKeys, fixtureLabels, type FixtureKey } from "./fixtures";

    const variants = { A: VariantA, B: VariantB, C: VariantC };
    const variantLabels = { A: `A — ${nameA}`, B: `B — ${nameB}`, C: `C — ${nameC}` };
    type VariantKey = keyof typeof variants;

    const params = new URLSearchParams(location.search);
    let variant = $state<VariantKey>((params.get("variant") as VariantKey) in variants ? params.get("variant") as VariantKey : "A");
    let pipeline = $state<FixtureKey>(fixtureKeys.includes(params.get("state") as FixtureKey) ? params.get("state") as FixtureKey : "fresh");

    $effect(() => {
        const url = new URL(location.href);
        url.searchParams.set("variant", variant);
        url.searchParams.set("state", pipeline);
        history.replaceState(null, "", url);
    });

    const view = $derived(fixture(pipeline));
    const Variant = $derived(variants[variant]);
</script>

<PrototypeShell shell={view.shell}>
    <Variant page={view.page} />
</PrototypeShell>

<PrototypeSwitcher
    variant={{ title: "Variant", keys: Object.keys(variants), labels: variantLabels, current: variant, set: k => variant = k as VariantKey }}
    pipeline={{ title: "State", keys: fixtureKeys, labels: fixtureLabels, current: pipeline, set: k => pipeline = k as FixtureKey }} />
