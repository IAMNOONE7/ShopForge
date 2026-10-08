import { useRef, useState } from "react";

export type ActionRunner = (change: () => Promise<unknown>) => Promise<void>;

// Mutations never retry automatically. A synchronous lock prevents duplicate clicks before React can repaint.
export function useAction(
  onSuccess: () => void,
): [unknown | null, ActionRunner, boolean] {
  const [error, setError] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);

  async function run(change: () => Promise<unknown>) {
    if (lock.current) return;
    lock.current = true;
    setError(null);
    setPending(true);

    const active =
      document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
    const form = active?.closest("form") ?? null;
    let failed = false;
    // React action forms reset after a caught failure. Cancel that reset instead of restoring a draft
    // later, which could overwrite edits already made in a controlled onSubmit form.
    const preserveFailedDraft = (event: Event) => {
      if (failed) event.preventDefault();
    };
    form?.addEventListener("reset", preserveFailedDraft);
    const buttons = form
      ? [...form.querySelectorAll<HTMLButtonElement>('button[type="submit"]')]
      : active instanceof HTMLButtonElement
        ? [active]
        : [];
    const disabled = buttons.map((button) => button.disabled);
    buttons.forEach((button) => {
      button.disabled = true;
    });
    form?.setAttribute("aria-busy", "true");

    try {
      await change();
      onSuccess();
    } catch (exception) {
      failed = true;
      setError(exception);
    } finally {
      buttons.forEach((button, index) => {
        button.disabled = disabled[index];
      });
      form?.removeAttribute("aria-busy");
      setPending(false);
      lock.current = false;
      window.setTimeout(() => form?.removeEventListener("reset", preserveFailedDraft), 50);
    }
  }

  return [error, run, pending];
}
