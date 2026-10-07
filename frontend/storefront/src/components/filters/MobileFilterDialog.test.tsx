// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  afterAll,
  afterEach,
  beforeAll,
  describe,
  expect,
  it,
  vi,
} from "vitest";
import type { Facet } from "../../api";
import { i18n, initializeI18n } from "../../i18n";
import { MobileFilterDialog } from "./MobileFilterDialog";

const facet: Facet = {
  code: "foldable",
  name: "Foldable",
  type: "boolean",
  unit: null,
  options: null,
  min: null,
  max: null,
  selectedMin: null,
  selectedMax: null,
  trueCount: 1,
  falseCount: 2,
  selected: null,
};

beforeAll(async () => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () {
    this.open = false;
    this.dispatchEvent(new Event("close"));
  };
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(async () => { cleanup(); document.body.style.overflow = ""; await i18n.changeLanguage("en"); });
afterAll(() => i18n.changeLanguage("en"));

describe("MobileFilterDialog", () => {
  it("locks page scrolling and waits for a current result count before showing results", async () => {
    const user = userEvent.setup();
    document.body.style.overflow = "auto";
    const props = { facets: [facet], activeCount: 0, resultCount: 3, refreshing: true, onChange: vi.fn(), onClear: vi.fn() };
    const { rerender } = render(<MobileFilterDialog {...props} />);
    const trigger = screen.getByRole("button", { name: "Filters" });
    await user.click(trigger);
    expect(document.body.style.overflow).toBe("hidden");
    const close = screen.getByRole("button", { name: "Close" });
    const updating = screen.getByRole("button", { name: "Updating results…" }) as HTMLButtonElement;
    expect(updating.disabled).toBe(true);
    fireEvent.keyDown(close, { key: "Tab", shiftKey: true });
    expect(document.activeElement).toBe(screen.getByRole("radio", { name: "Any" }));
    fireEvent.keyDown(document.activeElement!, { key: "Tab" });
    expect(document.activeElement).toBe(close);
    rerender(<MobileFilterDialog {...props} refreshing={false} resultCount={1} activeCount={1} />);
    fireEvent.keyDown(close, { key: "Tab", shiftKey: true });
    const show = screen.getByRole("button", { name: "Show 1 product" });
    expect(document.activeElement).toBe(show);
    fireEvent.keyDown(show, { key: "Tab" });
    expect(document.activeElement).toBe(close);
    await user.click(screen.getByRole("button", { name: "Show 1 product" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.body.style.overflow).toBe("auto");
    expect(document.activeElement).toBe(trigger);
    document.body.style.overflow = "";
  });

  it("restores scrolling on route unmount and renders the Czech result action", async () => {
    const user = userEvent.setup();
    await i18n.changeLanguage("cs");
    const view = render(<MobileFilterDialog facets={[facet]} activeCount={0} resultCount={4} refreshing={false} onChange={vi.fn()} onClear={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: "Filtry" }));
    expect(screen.getByRole("button", { name: "Zobrazit 4 produkty" })).toBeTruthy();
    view.unmount();
    expect(document.body.style.overflow).toBe("");
    await i18n.changeLanguage("en");
  });

  it("applies immediately and restores trigger focus after Escape", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <MobileFilterDialog
        facets={[facet]}
        activeCount={0}
        resultCount={3}
        refreshing={false}
        onChange={onChange}
        onClear={vi.fn()}
      />,
    );

    const trigger = screen.getByRole("button", { name: "Filters" });
    await user.click(trigger);
    expect(screen.getByRole("dialog").getAttribute("aria-modal")).toBe("true");
    await waitFor(() =>
      expect(document.activeElement).toBe(
        screen.getByRole("button", { name: "Close" }),
      ),
    );

    await user.click(screen.getByRole("radio", { name: /No/ }));
    expect(onChange).toHaveBeenCalledWith("foldable", "false");
    expect(screen.getByRole("dialog")).toBeTruthy();

    fireEvent(screen.getByRole("dialog"), new Event("cancel", { cancelable: true }));
    await waitFor(() => expect(document.activeElement).toBe(trigger));
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
