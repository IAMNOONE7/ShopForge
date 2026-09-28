export function formatCurrency(
  value: number,
  culture: string,
  currency: string,
) {
  return new Intl.NumberFormat(culture, { style: "currency", currency }).format(
    value,
  );
}

export function formatNumber(value: number, culture: string) {
  return new Intl.NumberFormat(culture).format(value);
}

export function formatDate(value: string, culture: string) {
  return new Intl.DateTimeFormat(culture).format(dateOnly(value));
}

export function formatDateTime(value: string, culture: string) {
  return new Intl.DateTimeFormat(culture, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function dateOnly(value: string) {
  const [year, month, day] = value.slice(0, 10).split("-").map(Number);
  return new Date(year, month - 1, day);
}
