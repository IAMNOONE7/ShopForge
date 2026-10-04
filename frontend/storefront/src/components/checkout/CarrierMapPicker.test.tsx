// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { i18n, initializeI18n } from "../../i18n";
import { CarrierMapPicker } from "./CarrierMapPicker";

type PickedPoint = { id?: string | number; name?: string; place?: string } | null;

function packetaAnswering(point: PickedPoint) {
  return {
    Widget: {
      pick: (_key: string, callback: (chosen: PickedPoint) => void) => callback(point),
    },
  };
}

describe("CarrierMapPicker", () => {
  beforeAll(async () => {
    await initializeI18n();
  });

  beforeEach(() => {
    delete (window as { Packeta?: unknown }).Packeta;
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("asks the carrier for a point and keeps only its id", async () => {
    const user = userEvent.setup();
    const chose = vi.fn();
    (window as { Packeta?: unknown }).Packeta = packetaAnswering({
      id: 4321,
      name: "Z-BOX Hlavní nádraží",
      place: "Wilsonova 8, Praha",
    });
    render(
      <CarrierMapPicker
        apiKey="widget-key"
        language="cs"
        chosen={null}
        disabled={false}
        showError={false}
        onChoose={chose}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));

    expect(chose).toHaveBeenCalledWith({
      id: "4321",
      label: "Z-BOX Hlavní nádraží, Wilsonova 8, Praha",
    });
  });

  it("keeps the shopper where they are when the map is closed without a choice", async () => {
    const user = userEvent.setup();
    const chose = vi.fn();
    (window as { Packeta?: unknown }).Packeta = packetaAnswering(null);
    render(
      <CarrierMapPicker
        apiKey="widget-key"
        language="cs"
        chosen={null}
        disabled={false}
        showError={false}
        onChoose={chose}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));

    // Nothing chosen, and nothing gone wrong either: the shopper is exactly where they were.
    expect(chose).not.toHaveBeenCalled();
    expect(screen.queryByText(/could not be opened/)).toBeNull();
    expect(screen.getByRole("button", { name: "Choose a pickup point" })).toBeTruthy();
  });

  // A carrier having a bad day must not stop the page working, only this one method.
  it("says so when the carrier's script will not load", async () => {
    const user = userEvent.setup();
    vi.spyOn(document.head, "append").mockImplementation((...nodes: (Node | string)[]) => {
      const script = nodes[0] as HTMLScriptElement;
      script.onerror?.(new Event("error"));
    });
    render(
      <CarrierMapPicker
        apiKey="widget-key"
        language="cs"
        chosen={null}
        disabled={false}
        showError={false}
        onChoose={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));

    expect(
      await screen.findByText(/The carrier’s map could not be opened/),
    ).toBeTruthy();
  });

  it("offers nothing to click when the store has no key for the carrier", () => {
    render(
      <CarrierMapPicker
        apiKey={null}
        language="cs"
        chosen={null}
        disabled={false}
        showError={false}
        onChoose={vi.fn()}
      />,
    );

    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.getByText(/This delivery method is not ready yet/)).toBeTruthy();
  });

  it("shows the chosen point and offers to change it", () => {
    render(
      <CarrierMapPicker
        apiKey="widget-key"
        language="cs"
        chosen={{ id: "4321", label: "Z-BOX Hlavní nádraží" }}
        disabled={false}
        showError={false}
        onChoose={vi.fn()}
      />,
    );

    expect(screen.getByText("Z-BOX Hlavní nádraží")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Choose a different point" })).toBeTruthy();
  });

  it("says in Czech what it says in English", async () => {
    await i18n.changeLanguage("cs");

    try {
      render(
        <CarrierMapPicker
          apiKey={null}
          language="cs"
          chosen={null}
          disabled={false}
          showError={false}
          onChoose={vi.fn()}
        />,
      );

      expect(screen.getByText(/Tento způsob doručení zatím není připravený/)).toBeTruthy();
    } finally {
      await i18n.changeLanguage("en");
    }
  });
});
