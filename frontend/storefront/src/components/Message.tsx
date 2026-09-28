import { EmptyState } from "./ui/EmptyState";

export function Message({ title, text }: { title: string; text: string }) {
  return <EmptyState title={title}>{text}</EmptyState>;
}
