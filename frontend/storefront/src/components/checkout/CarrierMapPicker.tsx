import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { InlineMessage } from "../ui/InlineMessage";

// One country and one kind of place, matching what the server validates the answer against. A point that is
// real but is a counter in another country is not one this method may use.
const country = "cz";
const group = "zbox";
const library = "https://widget.packeta.com/v6/www/js/library.js";

export type ChosenPoint = {
  id: string;
  label: string;
};

type Packeta = {
  Widget: {
    pick: (
      apiKey: string,
      callback: (point: { id?: string | number; name?: string; place?: string } | null) => void,
      options: Record<string, string>,
    ) => void;
  };
};

// Packeta's own map, opened in a window of theirs. The script is fetched the first time the shopper asks for
// it rather than with the page: a carrier having a bad day must not stop anybody buying anything by another
// method. Only the point's id is kept for the server — what it is called is Packeta's to confirm, and the
// label here is shown to the shopper and sent nowhere.
export function CarrierMapPicker({
  apiKey,
  language,
  chosen,
  disabled,
  showError,
  onChoose,
}: {
  apiKey: string | null;
  language: string;
  chosen: ChosenPoint | null;
  disabled: boolean;
  showError: boolean;
  onChoose: (point: ChosenPoint | null) => void;
}) {
  const { t } = useTranslation("checkout");
  const [opening, setOpening] = useState(false);
  const [unavailable, setUnavailable] = useState(false);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  if (apiKey === null) {
    return (
      <div className="checkout-pickup-state">
        <h3>{t("pickupPoint")}</h3>
        <InlineMessage tone="error">{t("carrierMapNotConfigured")}</InlineMessage>
      </div>
    );
  }

  async function open() {
    if (disabled || opening) return;
    setOpening(true);
    setUnavailable(false);

    try {
      const packeta = await library_();
      if (!alive.current) return;
      packeta.Widget.pick(
        apiKey!,
        (point) => {
          if (!alive.current) return;
          setOpening(false);
          if (point?.id === undefined || point.id === null) return;
          onChoose({
            id: String(point.id),
            label: [point.name, point.place].filter(Boolean).join(", ") || String(point.id),
          });
        },
        { country, group, language },
      );
    } catch {
      if (!alive.current) return;
      setOpening(false);
      setUnavailable(true);
    }
  }

  return (
    <div className="checkout-pickup-state">
      <h3>{t("pickupPoint")}</h3>
      {chosen !== null && <p className="checkout-chosen-point">{chosen.label}</p>}
      <p className="inline-form">
        <button type="button" name="pickupPointCode" disabled={disabled || opening} onClick={() => void open()}>
          {opening
            ? t("openingCarrierMap")
            : chosen === null
              ? t("chooseInCarrierMap")
              : t("changeInCarrierMap")}
        </button>
      </p>
      {unavailable && <InlineMessage tone="error">{t("carrierMapUnavailable")}</InlineMessage>}
      {showError && chosen === null && !unavailable && (
        <InlineMessage tone="error">{t("pickupIssue")}</InlineMessage>
      )}
    </div>
  );
}

// Fetched once and remembered. A failure is not cached, so a shopper whose connection dropped can try again.
let loading: Promise<Packeta> | null = null;

function library_(): Promise<Packeta> {
  const existing = (window as unknown as { Packeta?: Packeta }).Packeta;
  if (existing) return Promise.resolve(existing);
  if (loading) return loading;

  loading = new Promise<Packeta>((resolve, reject) => {
    const script = document.createElement("script");
    script.src = library;
    script.async = true;
    script.onload = () => {
      const packeta = (window as unknown as { Packeta?: Packeta }).Packeta;
      if (packeta) resolve(packeta);
      else reject(new Error("The carrier's map did not load."));
    };
    script.onerror = () => reject(new Error("The carrier's map did not load."));
    document.head.append(script);
  }).catch((error: unknown) => {
    loading = null;
    throw error;
  });

  return loading;
}
