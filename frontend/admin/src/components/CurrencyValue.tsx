import { Fragment } from "react";
import "./CurrencyValue.css";

export function CurrencyValue({ value, culture, currency }: {
  value: number;
  culture: string;
  currency: string;
}) {
  const parts = new Intl.NumberFormat(culture, { style: "currency", currency }).formatToParts(value);
  const groups: string[] = [];
  let group = "";
  let hasNumber = false;
  let leadingCurrency = false;
  for (const part of parts) {
    if (part.type === "integer" && !hasNumber && leadingCurrency && /\s$/u.test(group)) {
      groups.push(group);
      group = "";
    }
    if (part.type === "currency" && hasNumber) {
      groups.push(group);
      group = "";
    }
    group += part.value;
    if (part.type === "currency" && !hasNumber) leadingCurrency = true;
    if (part.type === "integer") hasNumber = true;
    if (part.type === "group") {
      groups.push(group);
      group = "";
    }
  }
  if (group) groups.push(group);

  return (
    <bdi className="currency-value" lang={culture}>
      {groups.map((text, index) => (
        <Fragment key={index}>
          {index > 0 && <wbr />}
          <span className="currency-value-group">{text}</span>
        </Fragment>
      ))}
    </bdi>
  );
}
