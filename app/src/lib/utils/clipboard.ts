/**
 * Copies text for a copy button, flagging `copied` true for a moment as feedback. Without a clipboard (an insecure
 * context) nothing happens; the text is still selectable by hand.
 */
export async function copyText(text: string, onCopied: (copied: boolean) => void) {
    try {
        await navigator.clipboard.writeText(text);
        onCopied(true);
        setTimeout(() => onCopied(false), 1500);
    } catch {
        // Nothing to report: the button simply does not confirm.
    }
}
