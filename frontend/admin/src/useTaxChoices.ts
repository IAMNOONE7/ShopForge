import { api } from "./api";
import type { TaxClass } from "./api/taxClasses";
import { useRequest, type RequestState } from "./useRequest";

export function useTaxChoices(storeId: string, revision = 0, enabled = true) {
  const [state, reload] = useRequest(`tax-choices:${storeId}:${revision}:${enabled}`, async signal => ({
    items: enabled ? await api.taxClasses(storeId, signal) : [],
  }));
  return { state, reload };
}

export type TaxChoices = ReturnType<typeof useTaxChoices>;

export function taxChoicesReady(state: RequestState<{ items: TaxClass[] }>) {
  return state.status === "ready" && !state.refreshing && state.refreshError === null;
}

// Keeping the saved value and deliberately clearing it do not depend on a class lookup.
export function taxAssignmentValid(value: string | null | undefined, choices: TaxChoices, saved?: string | null) {
  return value !== undefined && (value === null || value === saved || taxChoicesReady(choices.state)
    && choices.state.status === "ready" && choices.state.data.items.some(item => item.code === value));
}
