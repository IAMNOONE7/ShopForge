export const parcelLimitFields = ["parcelMaxWeightGrams", "parcelLengthMm", "parcelWidthMm", "parcelHeightMm"] as const;
export function wholeNumber(text: string, max = 2147483647) { const value = text.trim(); return /^\d+$/.test(value) && Number(value) > 0 && Number(value) <= max ? Number(value) : null; }
