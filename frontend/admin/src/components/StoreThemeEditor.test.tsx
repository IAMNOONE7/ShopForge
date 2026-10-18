// @vitest-environment jsdom
import { useState } from "react";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it } from "vitest";
import { i18n, initializeI18n } from "../i18n";
import { StoreThemeEditor, ThemePreview } from "./StoreThemeEditor";
import type { ThemeDraft } from "./storeValidation";

const theme: ThemeDraft = {
  primaryColor: "#7a2853",
  secondaryColor: "#17302a",
  borderRadius: "17",
};

beforeAll(initializeI18n);
beforeEach(async () => { await i18n.changeLanguage("en"); });
afterEach(cleanup);

function preview(value = theme, storeName = "Workshop") {
  render(<ThemePreview value={value} storeName={storeName} previewPrice="123 Kč" />);
  return screen.getByRole("complementary", { name: i18n.t("stores:preview") });
}

describe("theme preview under the admin policy", () => {
  it("shows arbitrary merchant colors and rounded surfaces without inline styles", () => {
    const element = preview();
    expect([...element.querySelectorAll("rect")].map((rect) => rect.getAttribute("fill")))
      .toEqual(["#7a2853", "#17302a", "#FFFFFF", "#7a2853"]);
    expect(element.querySelector(".theme-preview-card rect")?.getAttribute("rx")).toBe("17");
    expect(element.querySelector(".theme-preview-button rect")?.getAttribute("rx")).toBe("17");
    expect(document.querySelectorAll("[style], style, script")).toHaveLength(0);
    expect(screen.getByText("Workshop").namespaceURI).toBe("http://www.w3.org/1999/xhtml");
    expect(screen.getByText("123 Kč").namespaceURI).toBe("http://www.w3.org/1999/xhtml");
    expect(screen.queryByRole("button")).toBeNull();
    for (const surface of element.querySelectorAll("svg")) {
      expect(surface.getAttribute("aria-hidden")).toBe("true");
      expect(surface.getAttribute("focusable")).toBe("false");
    }
  });

  it.each([
    ["#000000", "theme-preview-light-text"],
    ["#FFFFFF", "theme-preview-dark-text"],
    ["#1F6FEB", "theme-preview-light-text"],
    ["#ffcc00", "theme-preview-dark-text"],
  ])("retains readable primary text against %s", (primaryColor, className) => {
    const element = preview({ ...theme, primaryColor });
    expect(element.querySelector(".theme-preview-header")?.classList.contains(className)).toBe(true);
    expect(element.querySelector(".theme-preview-button")?.classList.contains(className)).toBe(true);
    // The card remains white with dark text, even with a dark secondary background.
    expect(element.querySelector(".theme-preview-card")?.className).toBe("theme-preview-card");
    expect(element.querySelector(".theme-preview-card rect")?.getAttribute("fill")).toBe("#FFFFFF");
  });

  it.each([
    ["0", "0"], ["48", "48"], ["49", "48"], ["999999999999999999999999999", "48"],
    ["", "6"], ["-1", "6"], ["2.5", "6"], ["NaN", "6"],
  ])("keeps the bounded preview radius for draft %s", (borderRadius, expected) => {
    const element = preview({ ...theme, borderRadius });
    expect(element.querySelector(".theme-preview-button rect")?.getAttribute("rx")).toBe(expected);
  });

  it("uses safe defaults while incomplete or unsafe color drafts remain in the editor", () => {
    const value = { primaryColor: "url(#bad)", secondaryColor: "red", borderRadius: "" };
    render(<StoreThemeEditor value={value} storeName="<img src=x>" issues={[]} onChange={() => {}} />);
    expect(screen.getByRole("textbox", { name: "Primary color" })).toHaveProperty("value", "url(#bad)");
    expect(screen.getByRole("textbox", { name: "Secondary color" })).toHaveProperty("value", "red");
    expect([...document.querySelectorAll(".theme-preview rect")].map((rect) => rect.getAttribute("fill")))
      .toEqual(["#1F6FEB", "#EEF4FF", "#FFFFFF", "#1F6FEB"]);
    expect(screen.getByText("<img src=x>")).toBeTruthy();
    expect(document.querySelectorAll("img, [style], style, script")).toHaveLength(0);
  });

  it("updates colors, contrast and radius without stale styles or remounting the preview", () => {
    const view = render(<ThemePreview value={theme} storeName="Before" />);
    const element = screen.getByRole("complementary");
    view.rerender(<ThemePreview value={{ primaryColor: "#FFFFFF", secondaryColor: "#abcdef", borderRadius: "0" }} storeName="After" />);
    expect(screen.getByRole("complementary")).toBe(element);
    expect(screen.queryByText("Before")).toBeNull();
    expect(screen.getByText("After")).toBeTruthy();
    expect(element.querySelector(".theme-preview-button rect")?.getAttribute("fill")).toBe("#FFFFFF");
    expect(element.querySelector(".theme-preview-button rect")?.getAttribute("rx")).toBe("0");
    expect(element.querySelector(".theme-preview-header")?.classList.contains("theme-preview-dark-text")).toBe(true);
    expect(document.querySelectorAll("[style], style")).toHaveLength(0);
  });

  it.each(["cs", "en"])("keeps localized, accessible HTML content in %s", async (language) => {
    await i18n.changeLanguage(language);
    const element = preview(theme, " ");
    expect(screen.getByText(i18n.t("stores:previewStoreName"))).toBeTruthy();
    expect(screen.getByText(i18n.t("stores:previewProduct"))).toBeTruthy();
    expect(screen.getByText(i18n.t("stores:previewAction"))).toBeTruthy();
    expect(element.querySelectorAll("svg title, svg text, foreignObject, [tabindex]")).toHaveLength(0);
  });

  it("keeps keyboard color and radius edits local to the draft", async () => {
    function Editor() {
      const [value, setValue] = useState(theme);
      return <StoreThemeEditor value={value} storeName="Workshop" issues={[]}
        onChange={(field, input) => setValue((current) => ({ ...current, [field]: input }))} />;
    }
    const user = userEvent.setup();
    render(<Editor />);
    const color = screen.getByRole("textbox", { name: "Primary color" });
    await user.clear(color);
    await user.type(color, "#ffcc00");
    expect(document.querySelector(".theme-preview-header rect")?.getAttribute("fill")).toBe("#ffcc00");
    const radius = screen.getByRole("spinbutton", { name: "Corner radius" });
    await user.clear(radius);
    await user.type(radius, "23");
    expect(document.querySelector(".theme-preview-card rect")?.getAttribute("rx")).toBe("23");
    expect(document.activeElement).toBe(radius);
    expect(document.querySelectorAll("[style], style")).toHaveLength(0);
  });
});
