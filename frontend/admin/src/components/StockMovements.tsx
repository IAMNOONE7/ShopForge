import { useTranslation } from "react-i18next";
import type { StockMovement } from "../api";
import { formatDateTime, formatNumber } from "../utils/format";

function movementReason(reason: string): "adjustment" | "sale" | "return" | "other" {
  switch (reason) {
    case "Adjustment": return "adjustment";
    case "Sale": return "sale";
    case "Return": return "return";
    default: return "other";
  }
}

export function StockMovements({ movements }: { movements: StockMovement[] }) {
  const { t, i18n } = useTranslation("stock");
  const amount = (quantity: number) => {
    const value = formatNumber(Math.abs(quantity), i18n.resolvedLanguage);
    return quantity < 0 ? t("outAmount", { amount: value }) : t("inAmount", { amount: value });
  };
  const reason = (code: string) => {
    const known = movementReason(code);
    return known === "other"
      ? t("reasonOther", { code })
      : t(`reasons.${known}`);
  };

  return (
    <section className="physical-panel stock-movements" aria-labelledby="stock-movements-title">
      <h2 id="stock-movements-title">{t("movementsTitle")}</h2>
      <p className="hint">{t("movementsLimit")}</p>
      {movements.length === 0 ? (
        <p className="stock-empty-movements">{t("noMovements")}</p>
      ) : (
        <>
          <table className="stock-movements-table">
            <thead><tr>
              <th scope="col">{t("when")}</th>
              <th scope="col">{t("change")}</th>
              <th scope="col">{t("reason")}</th>
              <th scope="col">{t("reference")}</th>
            </tr></thead>
            <tbody>
              {movements.map((movement, index) => (
                <tr key={`${movement.occurredAt}:${movement.reference}:${index}`}>
                  <td>{formatDateTime(movement.occurredAt, i18n.resolvedLanguage)}</td>
                  <td>{amount(movement.quantity)}</td>
                  <td>{reason(movement.reason)}</td>
                  <td className="stock-reference">{movement.reference || "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <ol className="stock-movement-cards">
            {movements.map((movement, index) => (
              <li key={`${movement.occurredAt}:${movement.reference}:${index}`}>
                <strong>{amount(movement.quantity)}</strong>
                <dl>
                  <div><dt>{t("when")}</dt><dd>{formatDateTime(movement.occurredAt, i18n.resolvedLanguage)}</dd></div>
                  <div><dt>{t("reason")}</dt><dd>{reason(movement.reason)}</dd></div>
                  <div><dt>{t("reference")}</dt><dd className="stock-reference">{movement.reference || "—"}</dd></div>
                </dl>
              </li>
            ))}
          </ol>
        </>
      )}
    </section>
  );
}
