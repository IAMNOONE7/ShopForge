import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { parseStack, type IsolatedEnvironment } from "./isolatedEnvironment.ts";

export type OperatorCredentials = { email: string; password: string };
export function parseOperatorCredentials(value: unknown, environment: IsolatedEnvironment): OperatorCredentials {
  const invalid = () => new Error("Expected the disposable stack's operator credentials.");
  if (!value || typeof value !== "object" || Array.isArray(value)) throw invalid();
  const stack = value as Record<string, unknown>, parsed = parseStack(stack);
  if (parsed.apiOrigin !== environment.apiOrigin || parsed.rigOrigin !== environment.rigOrigin) throw invalid();
  if (!stack.operator || typeof stack.operator !== "object" || Array.isArray(stack.operator)) throw invalid();
  const operator = stack.operator as Record<string, unknown>;
  if (operator.email !== "operator@e2e.test" || typeof operator.password !== "string" || !/^E2e-[a-f0-9]{36}$/.test(operator.password)) throw invalid();
  return { email: operator.email, password: operator.password };
}
export function loadOperatorCredentials(environment: IsolatedEnvironment) {
  return parseOperatorCredentials(JSON.parse(readFileSync(resolve(process.env.SHOPFORGE_E2E_STACK ?? "../../.e2e/stack.json"), "utf8")) as unknown, environment);
}
