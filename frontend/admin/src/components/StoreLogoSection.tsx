import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { api, type AdminStore } from "../api";
import { useAction } from "../useAction";
import { validateLogo, type StoreIssue } from "./storeValidation";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

export function StoreLogoSection({
  store,
  reloadStores,
}: {
  store: AdminStore;
  reloadStores: () => void;
}) {
  const { t } = useTranslation("stores");
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [issue, setIssue] = useState<StoreIssue | null>(null);
  const [saved, setSaved] = useState(false);
  const [cacheVersion, setCacheVersion] = useState(0);
  const [error, run, pending] = useAction(reloadStores);

  function selectFile(selected: File | null) {
    setSaved(false);
    setFile(selected);
    setIssue(selected ? validateLogo(selected) : null);
  }

  async function upload() {
    if (!file) return;
    const nextIssue = validateLogo(file);
    setIssue(nextIssue);
    if (nextIssue) {
      input.current?.focus();
      return;
    }
    await run(async () => {
      await api.uploadLogo(store.id, file);
      setCacheVersion(Date.now());
      setSaved(true);
      setFile(null);
      if (input.current) input.current.value = "";
    });
  }

  const logoUrl =
    store.logoUrl && cacheVersion
      ? `${store.logoUrl}${store.logoUrl.includes("?") ? "&" : "?"}v=${cacheVersion}`
      : store.logoUrl;

  return (
    <section className="store-settings-panel" aria-labelledby="store-logo-title">
      <div className="section-heading">
        <div>
          <h2 id="store-logo-title">{t("branding")}</h2>
          <p className="hint">{t("logoHint")}</p>
        </div>
      </div>
      <div className="logo-settings">
        <div className="logo-current">
          {logoUrl ? (
            <img
              src={logoUrl}
              alt={t("logoAlt", { name: store.name })}
              className="store-logo-preview"
            />
          ) : (
            <div className="store-logo-placeholder" aria-hidden="true">
              {store.name.trim().slice(0, 1).toUpperCase() || "S"}
            </div>
          )}
          <span>
            <strong>{t(store.logoUrl ? "currentLogo" : "noLogo")}</strong>
            <small>{t("logoRequirements")}</small>
          </span>
        </div>
        <div className="logo-upload-controls">
          <label className="upload" htmlFor="logo">
            {store.logoUrl ? t("chooseReplacementLogo") : t("chooseLogo")}
          </label>
          <input
            ref={input}
            id="logo"
            name="logo"
            className="visually-hidden-file"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            aria-invalid={issue ? true : undefined}
            aria-describedby={issue ? "logo-error" : "logo-requirements"}
            onChange={(event) => selectFile(event.target.files?.[0] ?? null)}
          />
          <span id="logo-requirements" className="hint">
            {file ? t("selectedFile", { name: file.name }) : t("noFileSelected")}
          </span>
          <Button
            type="button"
            onClick={() => void upload()}
            disabled={!file || Boolean(issue)}
            busy={pending}
            busyLabel={t("uploadingLogo")}
          >
            {error !== null ? t("retryLogoUpload") : t("uploadSelectedLogo")}
          </Button>
        </div>
      </div>
      {issue && (
        <InlineMessage tone="error">
          <span id="logo-error">{t(`validation.${issue.key}`)}</span>
        </InlineMessage>
      )}
      {saved && <InlineMessage tone="success">{t("logoSaved")}</InlineMessage>}
      {error !== null && <RequestError error={error} operation="write" />}
    </section>
  );
}
