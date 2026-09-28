// @vitest-environment jsdom
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getProfile } from "../account";
import { HttpError } from "../api/http";
import { useCustomer } from "../customerContext";
import { CustomerProvider } from "./CustomerProvider";

vi.mock("../account", () => ({ getProfile: vi.fn() }));
const profile = {
  id: "customer",
  email: "a@example.com",
  firstName: "Ada",
  lastName: "L",
  phone: null,
};

afterEach(() => {
  cleanup();
  vi.mocked(getProfile).mockReset();
});

function State() {
  const state = useCustomer();
  return (
    <>
      <span>{state.status}</span>
      <button type="button" onClick={state.retry}>
        retry
      </button>
    </>
  );
}

describe("CustomerProvider", () => {
  it("treats an account 401 as a valid guest state", async () => {
    vi.mocked(getProfile).mockRejectedValue(new HttpError("http", 401));
    render(
      <CustomerProvider>
        <State />
      </CustomerProvider>,
    );
    await screen.findByText("guest");
    expect(screen.queryByText("error")).toBeNull();
  });

  it("keeps an outage distinct and can retry it", async () => {
    vi.mocked(getProfile)
      .mockRejectedValueOnce(new HttpError("network", null))
      .mockResolvedValueOnce(profile);
    render(
      <CustomerProvider>
        <State />
      </CustomerProvider>,
    );
    await screen.findByText("error");
    fireEvent.click(screen.getByRole("button", { name: "retry" }));
    await waitFor(() => expect(screen.getByText("authenticated")).toBeTruthy());
    expect(getProfile).toHaveBeenCalledTimes(2);
  });
});
