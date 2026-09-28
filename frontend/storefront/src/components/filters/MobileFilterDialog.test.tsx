// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
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
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

describe("MobileFilterDialog", () => {
  it("applies immediately and restores trigger focus after Escape", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <MobileFilterDialog
        facets={[facet]}
        activeCount={0}
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

    await user.keyboard("{Escape}");
    await waitFor(() => expect(document.activeElement).toBe(trigger));
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
