// How Binding States and Type Compatibility levels look on the Bindings pages. The state tones match the Channel
// page's badges; Needs attention uses the Stage Status colour for the same condition.
import type { BindingRowState, CompatibilityLevel } from "../../lib/types/views";

export const bindingStates = {
    unbound: { label: "Not bound", badge: "preset-tonal" },
    incomplete: { label: "Incomplete", badge: "preset-filled-warning-500" },
    needsAttention: { label: "Needs attention", badge: "preset-filled-error-500" },
    active: { label: "Active", badge: "preset-filled-success-500" },
    inactive: { label: "Inactive", badge: "preset-tonal" }
} satisfies Record<BindingRowState, unknown>;

export const levelTones = {
    Compatible: "text-success-700-300",
    Warning: "text-warning-700-300",
    Error: "text-error-700-300"
} satisfies Record<CompatibilityLevel, string>;

export const plural = (n: number, noun: string) => `${n} ${noun}${n === 1 ? "" : "s"}`;
