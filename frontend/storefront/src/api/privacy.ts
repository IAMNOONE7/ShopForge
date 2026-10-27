import { HttpError, isHttpError, requestBlob, requestNoContent, type Download } from "./http";

export type PrivacySummary = { exportedAt: string; sections: { name: string; entries: number }[] };
export type PrivacyDownload = Download & PrivacySummary;
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const invalid = () => new HttpError("invalid-response", 200);
function safeFailure(error: unknown) {
  return isHttpError(error) ? new HttpError(error.kind, error.status, null, null, error.retryAfter) : error;
}
export const privacyApi = {
  async export(expectedEmail: string, signal: AbortSignal): Promise<PrivacyDownload> {
    try {
      const download = await requestBlob("/api/storefront/account/export", "shopforge-data.json", { expectedContentType: "application/json", signal, cache: "no-store", notifyUnauthorized: false });
      const raw: unknown = JSON.parse(await download.blob.text()); signal.throwIfAborted();
      if (!record(raw) || typeof raw.exportedAt !== "string" || !Number.isFinite(Date.parse(raw.exportedAt)) || !record(raw.sections) ||
        !Object.values(raw.sections).every(Array.isArray)) throw invalid();
      const account = raw.sections.account;
      if (!Array.isArray(account) || account.length !== 1 || !record(account[0]) || account[0].email !== expectedEmail) throw invalid();
      // Validate the envelope and owner without rewriting exported decimals or unknown module data.
      return { blob: download.blob, filename: "shopforge-data.json", exportedAt: raw.exportedAt,
        sections: Object.entries(raw.sections).map(([name, entries]) => ({ name, entries: (entries as unknown[]).length })) };
    } catch (error) {
      if (error instanceof SyntaxError) throw invalid();
      throw safeFailure(error);
    }
  },
  async erase(password: string, signal: AbortSignal) {
    try { await requestNoContent("/api/storefront/account/delete", { method: "POST", body: { password }, signal, notifyUnauthorized: false }); }
    catch (error) { throw safeFailure(error); }
  },
};
