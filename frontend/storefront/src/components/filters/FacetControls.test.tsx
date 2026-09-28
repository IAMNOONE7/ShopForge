// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
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
import { BooleanFilter } from "./BooleanFilter";
import { RangeFilter } from "./RangeFilter";

const booleanFacet: Facet = {
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

const rangeFacet: Facet = {
  code: "width",
  name: "Width",
  type: "decimal",
  unit: "cm",
  options: null,
  min: 10,
  max: 100,
  selectedMin: null,
  selectedMax: null,
  trueCount: null,
  falseCount: null,
  selected: null,
};

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

describe("catalog facet controls", () => {
  it("offers any, true, and false boolean values", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const { rerender } = render(
      <BooleanFilter facet={booleanFacet} onChange={onChange} />,
    );

    expect(screen.getByRole("radio", { name: "Any" })).toBeTruthy();
    await user.click(screen.getByRole("radio", { name: /No/ }));
    expect(onChange).toHaveBeenLastCalledWith("false");
    rerender(
      <BooleanFilter
        facet={{ ...booleanFacet, selected: false }}
        onChange={onChange}
      />,
    );
    await user.click(screen.getByRole("radio", { name: "Any" }));
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("shows localized validation when a range is reversed", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<RangeFilter facet={rangeFacet} onChange={onChange} />);

    await user.type(screen.getByRole("spinbutton", { name: "Width from" }), "80");
    await user.type(screen.getByRole("spinbutton", { name: "Width to" }), "20");
    await user.click(screen.getByRole("button", { name: "Apply" }));

    expect(screen.getByRole("alert").textContent).toContain(
      "must not be greater",
    );
    expect(onChange).not.toHaveBeenCalled();
  });
});
