import { useRef, useState, type ReactNode } from "react";
import { RequestError } from "./RequestError";

export function DownloadButton({
  children,
  download,
}: {
  children: ReactNode;
  download: () => Promise<void>;
}) {
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
    <span>
      <button
        type="button"
        className="link-button"
        disabled={pending}
        aria-busy={pending}
        onClick={() => void start()}
      >
        {children}
      </button>
      {error !== null && <RequestError error={error} operation="download" />}
    </span>
  );
}
