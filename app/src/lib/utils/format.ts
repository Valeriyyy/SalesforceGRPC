/** A timestamp from a view model, in the viewer's locale, or a dash when there is none. */
export function when(iso: string | null | undefined): string {
    return iso ? new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }) : "—";
}
