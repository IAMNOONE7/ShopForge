export function methodVat(text: string): number | null {
  const written = text.trim().replace(",", ".");
  if (!/^\d+(?:\.\d+)?$/.test(written)) return null;
  const [whole, fraction = ""] = written.split(".");
  const significantFraction = fraction.replace(/0+$/, "");
  const canonical = (whole.replace(/^0+/, "") || "0") + (significantFraction ? `.${significantFraction}` : "");
  const value = Number(written);
  return Number.isFinite(value) && value >= 0 && value <= 100 && significantFraction.length <= 100
    && value.toFixed(significantFraction.length) === canonical ? value : null;
}
