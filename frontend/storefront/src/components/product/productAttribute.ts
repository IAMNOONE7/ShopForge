import type { ProductAttribute } from "../../api";
import type { Store } from "../../store";
import { formatDate, formatNumber } from "../../utils/format";

export function formatProductAttribute(
  attribute: ProductAttribute,
  store: Store,
  yes: string,
  no: string,
) {
  const { value, unit } = attribute;
  if (Array.isArray(value)) return value.join(", ");
  switch (attribute.type) {
    case "boolean":
      return value ? yes : no;
    case "date":
      return formatDate(String(value), store.culture);
    case "integer":
    case "decimal":
      return `${formatNumber(Number(value), store.culture)}${unit ? ` ${unit}` : ""}`;
    default:
      return String(value);
  }
}
