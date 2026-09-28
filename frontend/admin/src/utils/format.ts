export function uiLocale(language: string | undefined) {
  return language?.split("-")[0] === "cs" ? "cs-CZ" : "en-GB";
}

export function currencyFormatter(
  currency: string,
  language: string | undefined,
) {
  return new Intl.NumberFormat(uiLocale(language), {
    style: "currency",
    currency,
  });
}

export function formatDate(value: string, language: string | undefined) {
  return new Intl.DateTimeFormat(uiLocale(language)).format(new Date(value));
}

export function formatDateTime(value: string, language: string | undefined) {
  return new Intl.DateTimeFormat(uiLocale(language), {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

export function formatNumber(value: number, language: string | undefined) {
  return new Intl.NumberFormat(uiLocale(language)).format(value);
}
