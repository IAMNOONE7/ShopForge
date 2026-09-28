import type { Address } from "../../cart";

export type AddressDraft = {
  fullName: string;
  line1: string;
  line2: string;
  city: string;
  postalCode: string;
  country: string;
};

export const emptyAddress = (): AddressDraft => ({
  fullName: "",
  line1: "",
  line2: "",
  city: "",
  postalCode: "",
  country: "",
});

export function isAddressComplete(value: AddressDraft) {
  return (
    value.fullName.trim().length > 0 &&
    value.line1.trim().length > 0 &&
    value.city.trim().length > 0 &&
    value.postalCode.trim().length > 0 &&
    value.country.trim().length === 2
  );
}

export function toAddress(value: AddressDraft): Address {
  const line2 = value.line2.trim();
  return {
    fullName: value.fullName.trim(),
    line1: value.line1.trim(),
    line2: line2 || null,
    city: value.city.trim(),
    postalCode: value.postalCode.trim(),
    country: value.country.trim().toUpperCase(),
  };
}
