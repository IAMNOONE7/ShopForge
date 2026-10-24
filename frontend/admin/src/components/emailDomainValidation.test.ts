import { expect, it } from "vitest";
import type { StoreDomain } from "../api/domains";
import { emailDomainDraft } from "./emailDomainValidation";
const addresses: StoreDomain[] = [{ edge: null, zone: null, id: "own", hostName: "shop.cz", isVerified: true, isPrimary: false, verifiedAt: "2026-10-24T07:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null }];
const draft = { domain: "mail.shop.cz", senderAddress: "orders@shop.cz", replyTo: "" };
it("normalizes the host and sender, trims optional reply-to and omits an empty reply-to", () => {
  expect(emailDomainDraft({ domain: " Mail.Shop.CZ. ", senderAddress: " Orders@Shop.CZ ", replyTo: " " }, addresses)).toEqual({ errors: {}, body: draftBody() });
  expect(emailDomainDraft({ ...draft, senderAddress: "orders@mail.shop.cz", replyTo: " Replies@Elsewhere.cz " }, addresses).body).toMatchObject({ senderAddress: "orders@mail.shop.cz", replyTo: "Replies@Elsewhere.cz" });
});
function draftBody() { return { domain: draft.domain, senderAddress: draft.senderAddress }; }
it.each(["shop.cz", "evilshop.cz", "mail.other.cz", "https://mail.shop.cz", "shop.cz.evil.test", "", "mail.*.shop.cz"])("requires a strict subdomain of a verified address (%s)", domain => {
  expect(emailDomainDraft({ ...draft, domain }, addresses)).toMatchObject({ body: null, errors: { domain: "domainInvalid" } });
});
it("does not treat an unverified or absent address as proof", () => {
  for (const list of [[], [{ ...addresses[0], isVerified: false }]]) expect(emailDomainDraft(draft, list).body).toBeNull();
});
it.each(["bad", "orders@elsewhere.cz", "orders@notshop.cz", "orders@sub.mail.shop.cz"])("rejects an invalid or unaligned sender (%s)", senderAddress => {
  expect(emailDomainDraft({ ...draft, senderAddress }, addresses)).toMatchObject({ body: null, errors: { senderAddress: "senderInvalid" } });
});
it("validates reply-to without requiring sender-domain alignment", () => {
  expect(emailDomainDraft({ ...draft, replyTo: "invalid" }, addresses)).toMatchObject({ body: null, errors: { replyTo: "replyInvalid" } });
  expect(emailDomainDraft({ ...draft, replyTo: "support@elsewhere.cz" }, addresses).body?.replyTo).toBe("support@elsewhere.cz");
});
