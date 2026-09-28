// @vitest-environment jsdom
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
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

describe("useAction", () => {
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
