import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { RequestError } from "./RequestError";

export function DownloadButton({
  children,
  download,
}: {
  children: React.ReactNode;
  download: () => Promise<void>;
}) {
  const { t } = useTranslation("orders");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown | null>(null);
  const lock = useRef(false);

  async function start() {
    if (lock.current) return;
    lock.current = true;
    setPending(true);
    setError(null);
    try {
      await download();
    } catch (exception) {
      setError(exception);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  return (
    <div className="document-download">
      <button
        type="button"
        className="link-button"
        disabled={pending}
        aria-busy={pending}
        onClick={() => void start()}
      >
        {pending ? t("preparingDownload") : children}
      </button>
      {error !== null && <RequestError error={error} operation="download" />}
    </div>
  );
}
