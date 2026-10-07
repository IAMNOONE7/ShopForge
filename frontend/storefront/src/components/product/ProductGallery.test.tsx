// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StrictMode } from "react";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { i18n, initializeI18n } from "../../i18n";
import { ProductGallery } from "./ProductGallery";

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

describe("ProductGallery", () => {
  it("selects an image with labeled native buttons and preserves its alt text", async () => {
    const user = userEvent.setup();
    render(
      <ProductGallery
        productName="Oak chair"
        images={[
          { url: "/front.jpg", altText: "Chair from the front" },
          { url: "/back.jpg", altText: "Chair from the back" },
        ]}
      />,
    );

    const first = screen.getByRole("button", { name: "Show image 1 of 2" });
    const second = screen.getByRole("button", { name: "Show image 2 of 2" });
    expect(first.getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("img", { name: "Chair from the front" })).toBeTruthy();
    const image = screen.getByRole("img", { name: "Chair from the front" });
    expect(image.getAttribute("width")).toBe("960");
    expect(image.getAttribute("height")).toBe("960");
    expect(image.getAttribute("loading")).toBe("eager");
    expect(image.getAttribute("fetchpriority")).toBe("high");
    expect(first.querySelector("img")?.getAttribute("loading")).toBe("lazy");

    await user.click(second);
    expect(second.getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("img", { name: "Chair from the back" })).toBeTruthy();
  });

  it("keeps the gallery area usable when there is no image or an image fails", () => {
    const { rerender } = render(
      <ProductGallery productName="Oak chair" images={[]} />,
    );
    expect(screen.getByRole("img", { name: "Image unavailable" })).toBeTruthy();

    rerender(
      <ProductGallery
        productName="Oak chair"
        images={[{ url: "/missing.jpg", altText: null }]}
      />,
    );
    fireEvent.error(screen.getByRole("img", { name: "Oak chair" }));
    expect(screen.getByRole("img", { name: "Image unavailable" })).toBeTruthy();
  });

  it("browses thumbnails by keyboard and retains image identity through a reordered response", async () => {
    const user = userEvent.setup();
    const images = [
      { url: "/front.jpg", altText: "Front" },
      { url: "/side.jpg", altText: "Side" },
      { url: "/back.jpg", altText: "Back" },
    ];
    const view = render(<ProductGallery images={images} productName="Chair" />);
    screen.getByRole("button", { name: "Show image 1 of 3" }).focus();
    await user.keyboard("{ArrowLeft}");
    expect(screen.getByRole("img", { name: "Back" })).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Show image 3 of 3" }));
    await user.keyboard("{Home}{ArrowRight}");
    expect(screen.getByRole("img", { name: "Side" })).toBeTruthy();
    await user.keyboard("{End}{ArrowRight}");
    expect(screen.getByRole("img", { name: "Front" })).toBeTruthy();
    view.rerender(<ProductGallery images={[images[2], images[1], images[0]]} productName="Chair" />);
    expect(screen.getByRole("img", { name: "Front" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Show image 3 of 3" }).getAttribute("tabindex")).toBe("0");
    view.rerender(<ProductGallery images={[images[1]]} productName="Chair" />);
    expect(screen.getByRole("img", { name: "Side" })).toBeTruthy();
  });

  it("isolates the larger-image view, cycles focus and restores its trigger and scrolling on dismissal", async () => {
    const user = userEvent.setup();
    document.body.style.overflow = "auto";
    render(<StrictMode><ProductGallery productName="Chair" images={[{ url: "/front.jpg", altText: "Front" }]} /></StrictMode>);
    const trigger = screen.getByRole("button", { name: "View larger image" });
    await user.click(trigger);
    const viewer = screen.getByRole("dialog", { name: "Product gallery · Chair" });
    const close = within(viewer).getByRole("button", { name: "Close" });
    expect(within(viewer).getByRole("img", { name: "Front" })).toBeTruthy();
    expect(document.body.style.overflow).toBe("hidden");
    expect(document.activeElement).toBe(close);
    await user.tab();
    expect(document.activeElement).toBe(close);
    await user.tab({ shift: true });
    expect(document.activeElement).toBe(close);
    fireEvent(viewer, new Event("cancel", { cancelable: true }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(trigger);
    expect(document.body.style.overflow).toBe("auto");
    await user.click(trigger);
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(document.activeElement).toBe(trigger);
  });

  it("keeps a failed expanded photo dismissible and restores scrolling when the gallery unmounts", async () => {
    const user = userEvent.setup();
    const view = render(<ProductGallery productName="Chair" images={[{ url: "/front.jpg", altText: null }]} />);
    await user.click(screen.getByRole("button", { name: "View larger image" }));
    const viewer = screen.getByRole("dialog");
    fireEvent.error(within(viewer).getByRole("img", { name: "Chair" }));
    expect(within(viewer).getByRole("img", { name: "Image unavailable" })).toBeTruthy();
    await user.click(within(viewer).getByRole("button", { name: "Close" }));
    expect(document.activeElement).toBe(screen.getByRole("region", { name: "Product gallery" }));
    view.rerender(<ProductGallery productName="Chair" images={[{ url: "/new.jpg", altText: null }]} />);
    await user.click(screen.getByRole("button", { name: "View larger image" }));
    view.unmount();
    expect(document.body.style.overflow).toBe("");
  });

  it("translates image controls without replacing merchant alt text", async () => {
    const user = userEvent.setup();
    await i18n.changeLanguage("cs");
    render(<ProductGallery productName="Chair" contentLanguage="en-IE"
      images={[{ url: "/front.jpg", altText: "Front" }, { url: "/side.jpg", altText: " " }]} />);
    expect(screen.getByText("Obrázek 1 z 2")).toBeTruthy();
    expect(screen.getByRole("img", { name: "Front" }).getAttribute("lang")).toBe("en-IE");
    await user.click(screen.getByRole("button", { name: "Zobrazit obrázek 2 z 2" }));
    expect(screen.getByRole("img", { name: "Chair" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Zobrazit větší obrázek" })).toBeTruthy();
  });
});
