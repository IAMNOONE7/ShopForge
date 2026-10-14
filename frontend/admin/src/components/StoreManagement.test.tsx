// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import {
  afterAll,
  afterEach,
  beforeAll,
  describe,
  expect,
  it,
  vi,
} from "vitest";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StoreLogoSection } from "./StoreLogoSection";
import { StorePublicationSection } from "./StorePublicationSection";
import { StoreSettingsSection } from "./StoreSettingsSection";
import {
  validateCreateStore,
  type CreateStoreDraft,
} from "./storeValidation";

const company = {
  legalName: "Example s.r.o.",
  line1: "Main 1",
  city: "Prague",
  postalCode: "11000",
  country: "CZ",
  registrationNumber: "12345678",
  vatNumber: "CZ12345678",
};

function store(overrides: Partial<AdminStore> = {}): AdminStore {
  return {
    id: "store-a",
    name: "Example Store",
    currency: "CZK",
    culture: "cs-CZ",
    status: "draft",
    theme: {
      primaryColor: "#1F6FEB",
      secondaryColor: "#EEF4FF",
      borderRadius: 6,
    },
    logoUrl: null,
    primaryHostName: "example.localhost",
    returnWindowDays: 14,
    company: null,
    ...overrides,
  };
}

function mockReadyReads() {
  vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: true, checks: [
    { check: "store.logo", isReady: true, problem: null, weight: "required" },
    { check: "store.company", isReady: true, problem: null, weight: "required" },
    { check: "catalog.visible-products", isReady: true, problem: null, weight: "required" },
    { check: "orders.payment-method", isReady: true, problem: null, weight: "required" },
    { check: "orders.shipping-method", isReady: true, problem: null, weight: "required" },
  ] });
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});
afterAll(() => i18n.changeLanguage("en"));

describe("store management", () => {
  it("rejects malformed creation values before a request is possible", () => {
    const draft: CreateStoreDraft = {
      name: "Example",
      hostName: "https://example.com/path",
      currency: "EU",
      culture: "not-a-culture",
      primaryColor: "blue",
      secondaryColor: "#FFFFFF",
      borderRadius: "-1",
    };

    expect(validateCreateStore(draft).map((issue) => issue.field)).toEqual([
      "hostName",
      "currency",
      "culture",
      "primaryColor",
      "borderRadius",
    ]);
  });

  it("requires an optional company group to be complete", async () => {
    const user = userEvent.setup();
    const update = vi.spyOn(api, "updateStore");
    render(
      <StoreSettingsSection store={store()} reloadStores={() => undefined} />,
    );

    await user.type(screen.getByRole("textbox", { name: "Street and number" }), "Main 1");
    await user.click(screen.getByRole("button", { name: "Save settings" }));

    expect(update).not.toHaveBeenCalled();
    expect(
      screen.getAllByText(
        "Complete this field or leave the whole company group empty.",
      ).length,
    ).toBeGreaterThan(0);
    await waitFor(() =>
      expect(document.activeElement?.id).toBe("store-settings-validation"),
    );
  });

  it("preserves the real currency and omits an empty company on published saves", async () => {
    const user = userEvent.setup();
    const published = store({ status: "published" });
    const update = vi.spyOn(api, "updateStore").mockResolvedValue(published);
    render(
      <StoreSettingsSection
        store={published}
        reloadStores={() => undefined}
      />,
    );

    const currency = screen.getByRole("textbox", { name: "Currency" });
    expect((currency as HTMLInputElement).readOnly).toBe(true);
    await user.click(screen.getByRole("button", { name: "Save settings" }));

    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("Store settings were saved.")).toBeTruthy();
    expect(update.mock.calls[0][1]).toMatchObject({
      currency: "CZK",
      company: undefined,
    });
    expect(update.mock.calls[0][1].currency).not.toBe("null");
  });

  it("blocks unsupported logos without calling the API", async () => {
    const user = userEvent.setup({ applyAccept: false });
    const upload = vi.spyOn(api, "uploadLogo");
    render(<StoreLogoSection store={store()} reloadStores={() => undefined} />);

    await user.upload(
      screen.getByLabelText("Choose logo"),
      new File(["gif"], "logo.gif", { type: "image/gif" }),
    );

    expect(screen.getByText("Choose a PNG, JPEG or WebP image.")).toBeTruthy();
    expect(
      (screen.getByRole("button", {
        name: "Upload selected logo",
      }) as HTMLButtonElement).disabled,
    ).toBe(true);
    await user.upload(
      screen.getByLabelText("Choose logo"),
      new File([new Uint8Array(5 * 1024 * 1024 + 1)], "large.png", {
        type: "image/png",
      }),
    );
    expect(screen.getByText("Choose an image no larger than 5 MiB.")).toBeTruthy();
    expect(upload).not.toHaveBeenCalled();
  });

  it("uploads a valid logo once and cache-busts the displayed image", async () => {
    const user = userEvent.setup();
    const upload = vi.spyOn(api, "uploadLogo").mockResolvedValue(undefined);
    const branded = store({ logoUrl: "/api/admin/stores/store-a/logo" });
    render(
      <StoreLogoSection store={branded} reloadStores={() => undefined} />,
    );

    await user.upload(
      screen.getByLabelText("Choose replacement"),
      new File(["png"], "logo.png", { type: "image/png" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Upload selected logo" }),
    );

    await waitFor(() => expect(upload).toHaveBeenCalledTimes(1));
    expect(upload.mock.calls[0][0]).toBe("store-a");
    await waitFor(() =>
      expect(
        (screen.getByRole("img", { name: "Example Store logo" }) as HTMLImageElement)
          .src,
      ).toContain("?v="),
    );
  });

  it("keeps failed readiness reads unknown and retries them", async () => {
    const user = userEvent.setup();
    const readiness = vi.spyOn(api, "storeReadiness")
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValue({ isReady: true, checks: [{ check: "catalog.visible-products", isReady: true, problem: null, weight: "required" }] });

    render(
      <MemoryRouter>
        <StorePublicationSection
          canManage
          store={store({ logoUrl: "/logo", company })}
          reloadStores={() => undefined}
        />
      </MemoryRouter>,
    );

    expect(
      await screen.findByText(
        "Current readiness could not be verified. Any displayed checklist is from an earlier response.",
      ),
    ).toBeTruthy();
    expect(screen.getByRole("button", { name: "Review and publish" })).toHaveProperty("disabled", true);

    await user.click(screen.getByRole("button", { name: "Refresh readiness checks" }));
    await waitFor(() => expect(readiness).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(screen.getAllByText("Ready")).toHaveLength(1),
    );
  });

  it("focuses publish confirmation and shows server refusal details", async () => {
    const user = userEvent.setup();
    mockReadyReads();
    vi.spyOn(api, "publishStore").mockRejectedValue(
      new HttpError("http", 409, {
        status: 409,
        title: "Cannot publish",
        problems: ["Internal publish check detail"],
      }),
    );

    render(
      <MemoryRouter>
        <StorePublicationSection
          canManage
          store={store({ logoUrl: "/logo", company })}
          reloadStores={() => undefined}
        />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getAllByText("Ready")).toHaveLength(5),
    );
    vi.mocked(api.storeReadiness).mockRejectedValue(new Error("offline"));
    await user.click(screen.getByRole("button", { name: "Review and publish" }));

    const confirm = screen.getByRole("button", { name: "Publish store" });
    await waitFor(() => expect(document.activeElement).toBe(confirm));
    expect(screen.getByText("Publish Example Store?")).toBeTruthy();

    await user.click(confirm);
    expect(
      await screen.findByText(
        "The data changed or conflicts with an existing record. Review it and try again.",
      ),
    ).toBeTruthy();
    expect(screen.getByText("Internal publish check detail")).toBeTruthy();
    expect(
      screen.getByText(
        "Review the readiness checks and store settings, then try again.",
      ),
    ).toBeTruthy();
  });
});
