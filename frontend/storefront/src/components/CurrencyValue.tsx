import { Fragment } from "react";

export function CurrencyValue({ value, culture, currency, prefix = "" }: {
  value: number;
  culture: string;
  currency: string;
  prefix?: string;
}) {
  const parts = new Intl.NumberFormat(culture, { style: "currency", currency }).formatToParts(value);
  const groups: string[] = [];
  let group = prefix;
  let hasNumber = false;
  for (const part of parts) {
    if (part.type === "currency" && hasNumber) {
      groups.push(group);
      group = "";
    }
    group += part.value;
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
