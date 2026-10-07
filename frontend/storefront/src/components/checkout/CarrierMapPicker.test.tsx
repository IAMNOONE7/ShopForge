// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StrictMode } from "react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { i18n, initializeI18n } from "../../i18n";
import { CarrierMapPicker } from "./CarrierMapPicker";

type PickedPoint = { id?: string | number; name?: string; place?: string } | null;
const carrierWindow = window as unknown as { Packeta?: { Widget: { pick: (key: string, callback: (point: PickedPoint) => void, options: Record<string, string>) => void } } };

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

  afterEach(async () => {
    cleanup();
    vi.restoreAllMocks();
    await i18n.changeLanguage("en");
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

  it("accepts a real widget callback in Strict Mode, prevents another open and discards callbacks after unmount", async () => {
    const user = userEvent.setup();
    let callback!: (point: PickedPoint) => void;
    const pick = vi.fn((_key: string, answer: (point: PickedPoint) => void) => { callback = answer; });
    carrierWindow.Packeta = { Widget: { pick } };
    const choose = vi.fn();
    const view = render(<StrictMode><CarrierMapPicker apiKey="public-widget-key" language="cs" chosen={null}
      disabled={false} showError={false} onChoose={choose} /></StrictMode>);
    await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));
    expect(screen.getByRole("button", { name: "Opening the map…" })).toHaveProperty("disabled", true);
    expect(pick).toHaveBeenCalledOnce();
    expect(pick).toHaveBeenCalledWith("public-widget-key", expect.any(Function), { country: "cz", group: "zbox", language: "cs" });
    callback({ id: 4321, name: "Z-BOX", place: "Prague" });
    expect(choose).toHaveBeenCalledWith({ id: "4321", label: "Z-BOX, Prague" });
    view.unmount();
    callback({ id: 9999 });
    expect(choose).toHaveBeenCalledOnce();
  });

  it("reports a widget failure in Czech and permits a later explicit attempt", async () => {
    const user = userEvent.setup();
    await i18n.changeLanguage("cs");
    const pick = vi.fn().mockImplementationOnce(() => { throw new Error("unavailable"); }).mockImplementation((_key, callback) => callback(null));
    carrierWindow.Packeta = { Widget: { pick } };
    render(<CarrierMapPicker apiKey="public-widget-key" language="cs" chosen={null} disabled={false} showError={false} onChoose={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: "Vyberte výdejní místo" }));
    expect(await screen.findByText(/Mapu dopravce se nepodařilo otevřít/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Vyberte výdejní místo" }));
    expect(pick).toHaveBeenCalledTimes(2);
    expect(screen.queryByText(/Mapu dopravce se nepodařilo otevřít/)).toBeNull();
  });
});
