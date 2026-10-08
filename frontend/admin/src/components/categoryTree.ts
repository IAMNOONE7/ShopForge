import type { Category } from "../api";

export const maxCategoryDepth = 5;

// Active children can outlive an archived parent. Missing or cyclic ancestry ends the visible path.
export function categoryPath(categories: Category[], id: string): Category[] {
  const byId = new Map(categories.map((category) => [category.id, category]));
  const visited = new Set<string>();
  const path: Category[] = [];
  let current = byId.get(id);
  while (current && !visited.has(current.id)) {
    visited.add(current.id);
    path.unshift(current);
    current = current.parentId ? byId.get(current.parentId) : undefined;
  }
  return path;
}

export function categoryDescendants(categories: Category[], id: string): Set<string> {
  const found = new Set([id]);
  const pending = [id];
  for (let index = 0; index < pending.length; index++) {
    for (const category of categories) {
      if (category.parentId === pending[index] && !found.has(category.id)) {
        found.add(category.id);
        pending.push(category.id);
      }
    }
  }
  return found;
}

function branchHeight(categories: Category[], id: string, seen = new Set<string>()): number {
  if (seen.has(id)) return maxCategoryDepth + 1;
  const next = new Set(seen).add(id);
  const children = categories.filter((category) => category.parentId === id);
  return children.length ? 1 + Math.max(...children.map((child) => branchHeight(categories, child.id, next))) : 1;
}

export function canParentCategory(categories: Category[], parentId: string | null, categoryId?: string): boolean {
  if (parentId === null) return true;
  if (!categories.some((category) => category.id === parentId)) return false;
  if (categoryId && categoryDescendants(categories, categoryId).has(parentId)) return false;
  return categoryPath(categories, parentId).length + (categoryId ? branchHeight(categories, categoryId) : 1) <= maxCategoryDepth;
}
