// @vitest-environment jsdom
import { useState } from "react";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAction } from "./useAction";

function Form({ change }: { change: () => Promise<void> }) {
  const [error, run] = useAction(() => undefined);
  async function save() {
    await run(change);
  }
  return (
    <form action={save}>
      <label>
        Name <input name="name" />
      </label>
      <button type="submit">Save</button>
      {error !== null && <p>failed</p>}
    </form>
  );
}

afterEach(cleanup);

function ControlledForm() {
  const [value, setValue] = useState("old value");
  const [error, run] = useAction(() => undefined);
  return <form onSubmit={(event) => { event.preventDefault(); void run(async () => { throw new Error("failure"); }); }}>
    <label>Name <input name="name" value={value} onChange={(event) => setValue(event.target.value)} /></label>
    <button type="submit">Save</button>
    {error !== null && <p>failed</p>}
  </form>;
}

describe("useAction", () => {
  it("does not overwrite a controlled correction made immediately after a failed save", async () => {
    const user = userEvent.setup();
    render(<ControlledForm />);
    const input = screen.getByLabelText("Name") as HTMLInputElement;
    await user.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByText("failed");
    await user.clear(input);
    await user.type(input, "correction");
    await new Promise((resolve) => window.setTimeout(resolve, 60));
    expect(input.value).toBe("correction");
  });
  it("locks a pending action and restores an uncontrolled draft after failure", async () => {
    let reject!: (reason: unknown) => void;
    const change = vi.fn(
      () =>
        new Promise<void>((_resolve, no) => {
          reject = no;
        }),
    );
    const user = userEvent.setup();
    render(<Form change={change} />);
    const input = screen.getByLabelText("Name") as HTMLInputElement;
    const save = screen.getByRole("button", { name: "Save" });
    await user.type(input, "kept draft");
    await user.click(save);
    await user.click(save);
    expect(change).toHaveBeenCalledTimes(1);

    reject(new Error("failed"));
    await screen.findByText("failed");
    await waitFor(() => expect(input.value).toBe("kept draft"));
  });
});
