// @vitest-environment jsdom
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { HttpError } from "../../api/http";
import { initializeI18n } from "../../i18n";
import { DownloadButton } from "./DownloadButton";

beforeAll(() => initializeI18n());

describe("DownloadButton", () => {
  it("keeps a failed document request in the page as a localized error", async () => {
    const download = vi
      .fn()
      .mockRejectedValue(new HttpError("invalid-response", 200));
    render(<DownloadButton download={download}>Invoice PDF</DownloadButton>);
    await userEvent.click(screen.getByRole("button", { name: "Invoice PDF" }));
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("unexpected response");
    expect(download).toHaveBeenCalledTimes(1);
  });
});
