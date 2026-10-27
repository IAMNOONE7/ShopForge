import type { AttributeDefinition } from "../api";

export const attributeDefinition: AttributeDefinition = {
  id: "bbbbbbbb-1111-2222-3333-444444444444", code: "material", name: "Material", type: "select", unit: null,
  isFilterable: true, isVisibleOnProductPage: true, isInFeeds: true, isSearchable: false, sortOrder: 2,
  options: [
    { id: "11111111-1111-2222-3333-444444444444", code: "oak", name: "Oak" },
    { id: "22222222-1111-2222-3333-444444444444", code: "beech", name: "Beech" },
    { id: "33333333-1111-2222-3333-444444444444", code: "walnut", name: "Walnut" },
  ],
};
export const textAttribute: AttributeDefinition = { ...attributeDefinition, id: "cccccccc-1111-2222-3333-444444444444",
  code: "care", name: "Care", type: "text", isFilterable: false, isSearchable: true, options: [] };
