// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { i18n, initializeI18n } from "../../i18n";
import { ProductGallery } from "./ProductGallery";

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(cleanup);
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
});
