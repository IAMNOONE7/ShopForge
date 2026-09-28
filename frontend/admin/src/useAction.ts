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
    const snapshot = form ? snapshotFields(form) : [];
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
      setError(exception);
      // React resets action forms after their promise settles; restore the captured draft on the next task.
      window.setTimeout(() => restoreFields(snapshot), 50);
    } finally {
      buttons.forEach((button, index) => {
        button.disabled = disabled[index];
      });
      form?.removeAttribute("aria-busy");
      setPending(false);
      lock.current = false;
    }
  }

  return [error, run, pending];
}

type FieldSnapshot = {
  field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
  value: string;
  checked: boolean | null;
  selected: string[] | null;
};

function snapshotFields(form: HTMLFormElement): FieldSnapshot[] {
  return [...form.elements]
    .filter(
      (
        field,
      ): field is HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement =>
        field instanceof HTMLInputElement ||
        field instanceof HTMLTextAreaElement ||
        field instanceof HTMLSelectElement,
    )
    .map((field) => ({
      field,
      value: field.value,
      checked:
        field instanceof HTMLInputElement &&
        (field.type === "checkbox" || field.type === "radio")
          ? field.checked
          : null,
      selected:
        field instanceof HTMLSelectElement && field.multiple
          ? [...field.selectedOptions].map((option) => option.value)
          : null,
    }));
}

function restoreFields(snapshot: FieldSnapshot[]) {
  for (const { field, value, checked, selected } of snapshot) {
    if (
      !field.isConnected ||
      (field instanceof HTMLInputElement && field.type === "file")
    )
      continue;
    field.value = value;
    if (checked !== null && field instanceof HTMLInputElement)
      field.checked = checked;
    if (selected && field instanceof HTMLSelectElement) {
      for (const option of field.options)
        option.selected = selected.includes(option.value);
    }
  }
}
