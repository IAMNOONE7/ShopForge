import { loadStack, type IsolatedEnvironment } from "./isolatedEnvironment.ts";

export function merchantOrigin(environment: IsolatedEnvironment, port = "5176") {
  const occupied = [environment.storefrontPort, Number(new URL(environment.apiOrigin).port), Number(new URL(environment.rigOrigin).port)];
  if (!/^\d+$/.test(port) || Number(port) < 1024 || Number(port) > 65535 || occupied.includes(Number(port))) throw new Error("Choose a distinct unprivileged admin port.");
  return `http://127.0.0.1:${Number(port)}`;
}
export function loadMerchantEnvironment() {
  const environment = loadStack();
  return { ...environment, adminOrigin: merchantOrigin(environment, process.env.SHOPFORGE_E2E_ADMIN_PORT) };
}
