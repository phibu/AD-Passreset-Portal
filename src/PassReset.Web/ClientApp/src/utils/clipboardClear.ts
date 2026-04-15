/**
 * FEAT-003: Clipboard auto-clear with readback guard.
 *
 * Schedules a delayed clipboard clear after the password generator writes a
 * generated password to the clipboard. The clear only fires if the clipboard
 * content still matches the generated value — so it never clobbers anything
 * the user copied after generation.
 *
 * Notes on browser behavior:
 * - `navigator.clipboard.readText()` may trigger a user permission prompt in
 *   Firefox/Safari. This is intentional per CONTEXT.md; operators are informed
 *   via the appsettings docs.
 * - If the clipboard API is unavailable (insecure context, older browsers),
 *   this helper is a silent no-op.
 */

export interface ClipboardClearHandle {
  cancel(): void;
}

const NOOP_HANDLE: ClipboardClearHandle = { cancel: () => { /* no-op */ } };

/**
 * Schedule a clipboard auto-clear.
 *
 * @param value       The value that was just written to the clipboard (the generated password).
 * @param seconds     Delay before attempting to clear. 0 or negative disables entirely (no timer).
 * @param onTick      Optional callback fired every second with remaining seconds.
 * @param onCleared   Optional callback fired after the clear attempt (whether or not it actually wiped).
 * @param onCancelled Optional callback fired when `cancel()` is invoked.
 * @returns A handle whose `cancel()` stops the pending clear.
 */
export function scheduleClipboardClear(
  value: string,
  seconds: number,
  onTick?: (remaining: number) => void,
  onCleared?: () => void,
  onCancelled?: () => void,
): ClipboardClearHandle {
  // Feature disabled by configuration — do nothing.
  if (!seconds || seconds <= 0) {
    return NOOP_HANDLE;
  }

  // Clipboard API unavailable (insecure context, older browsers, SSR) — silent no-op.
  if (
    typeof navigator === 'undefined' ||
    !navigator.clipboard ||
    typeof navigator.clipboard.writeText !== 'function' ||
    typeof navigator.clipboard.readText !== 'function'
  ) {
    return NOOP_HANDLE;
  }

  let remaining = seconds;
  let cancelled = false;

  const performClear = async () => {
    try {
      const current = await navigator.clipboard.readText();
      if (current === value) {
        await navigator.clipboard.writeText('');
      }
    } catch {
      // Permission denied or API unavailable — silent no-op.
    } finally {
      onCleared?.();
    }
  };

  const interval = setInterval(() => {
    if (cancelled) return;
    remaining -= 1;
    onTick?.(remaining);
    if (remaining <= 0) {
      clearInterval(interval);
      void performClear();
    }
  }, 1000);

  return {
    cancel: () => {
      if (cancelled) return;
      cancelled = true;
      clearInterval(interval);
      onCancelled?.();
    },
  };
}

/**
 * Convenience helper: cancels a handle if present. Safe to call with null.
 */
export function cancelClipboardClear(handle: ClipboardClearHandle | null): void {
  handle?.cancel();
}
