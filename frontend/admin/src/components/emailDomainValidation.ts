import type { StoreDomain } from "../api/domains";
import type { ConnectEmailDomain } from "../api/storeEmail";
import { normalizedDomain } from "./domainValidation";

export type EmailDraft = { domain: string; senderAddress: string; replyTo: string };
export type EmailField = keyof EmailDraft;
const validEmail = (value: string) => value.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
export function emailDomainDraft(draft: EmailDraft, addresses: StoreDomain[]) {
  const domain = normalizedDomain(draft.domain), senderAddress = draft.senderAddress.trim().toLowerCase(), replyTo = draft.replyTo.trim();
  const parents = addresses.filter(address => address.isVerified && domain?.endsWith(`.${address.hostName}`));
  const errors: Partial<Record<EmailField, "domainInvalid" | "senderInvalid" | "replyInvalid">> = {};
  if (!domain || parents.length === 0) errors.domain = "domainInvalid";
  const senderDomain = senderAddress.split("@")[1];
  if (!validEmail(senderAddress) || domain && parents.length > 0 && senderDomain !== domain && !parents.some(parent => parent.hostName === senderDomain)) errors.senderAddress = "senderInvalid";
  if (replyTo && !validEmail(replyTo)) errors.replyTo = "replyInvalid";
  const body: ConnectEmailDomain | null = Object.keys(errors).length || !domain ? null : { domain, senderAddress, ...(replyTo ? { replyTo } : {}) };
  return { body, errors };
}
