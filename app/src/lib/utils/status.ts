// How each Stage Status looks, wherever it is shown.
import { CircleCheck, CircleDashed, Hourglass, TriangleAlert } from "@lucide/svelte";
import type { StageStatus } from "../types/views";

export const statusMeta = {
    ok: { label: "OK", icon: CircleCheck, text: "text-success-600-400", badge: "preset-filled-success-500" },
    toDo: { label: "To do", icon: CircleDashed, text: "text-primary-600-400", badge: "preset-tonal-primary" },
    needsAttention: { label: "Needs attention", icon: TriangleAlert, text: "text-error-600-400", badge: "preset-filled-error-500" },
    waiting: { label: "Waiting", icon: Hourglass, text: "text-surface-600-400", badge: "preset-tonal-surface" }
} satisfies Record<StageStatus, unknown>;
