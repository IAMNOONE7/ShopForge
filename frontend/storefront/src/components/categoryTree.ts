import type { Category } from "../api";

export type CategoryNode = { category: Category; children: CategoryNode[] };

// The API supplies a flat list in tree order. Keep that order and use its parent identity;
// a missing parent remains reachable as a root rather than disappearing from navigation.
export function categoryTree(categories: Category[]): CategoryNode[] {
  const nodes = new Map(
    categories.map((category) => [category.slug, { category, children: [] as CategoryNode[] }]),
  );
  const roots: CategoryNode[] = [];
  for (const category of categories) {
    const node = nodes.get(category.slug)!;
    const parent = category.parentSlug ? nodes.get(category.parentSlug) : undefined;
    if (parent) parent.children.push(node);
    else roots.push(node);
  }
  return roots;
}
