import { api, type AttributeDefinition, type AttributeUpdate } from "../api";
import { HttpError } from "../api/http";

export type AttributeCommand =
  | { kind: "update"; input: AttributeUpdate }
  | { kind: "add"; name: string }
  | { kind: "rename"; optionId: string; name: string }
  | { kind: "reorder"; optionIds: string[] }
  | { kind: "removeOption"; optionId: string }
  | { kind: "removeDefinition" };

export async function changeAttribute(storeId: string, before: AttributeDefinition, command: AttributeCommand, signal: AbortSignal) {
  if (command.kind === "removeDefinition") { await api.removeAttribute(storeId, before.id, signal); return; }
  if (command.kind === "removeOption") { await api.removeOption(storeId, before.id, command.optionId, signal); return; }
  const after = command.kind === "update" ? await api.updateAttribute(storeId, before.id, command.input, signal)
    : command.kind === "add" ? await api.addOption(storeId, before.id, command.name, signal)
      : command.kind === "rename" ? await api.renameOption(storeId, before.id, command.optionId, command.name, signal)
        : await api.reorderOptions(storeId, before.id, command.optionIds, signal);
  if (after.id !== before.id || after.code !== before.code || after.type !== before.type) throw new HttpError("invalid-response", 200);
  if (command.kind === "update") return;
  const settings = ({ options: _options, ...rest }: AttributeDefinition) => rest;
  if (JSON.stringify(settings(after)) !== JSON.stringify(settings(before))) throw new HttpError("invalid-response", 200);
  const expected = command.kind === "rename" ? before.options.map(option => option.id === command.optionId ? { ...option, name: command.name } : option)
    : command.kind === "reorder" ? command.optionIds.map(id => before.options.find(option => option.id === id)) : before.options;
  const actual = command.kind === "add" ? after.options.slice(0, -1) : after.options;
  if (JSON.stringify(actual) !== JSON.stringify(expected) || command.kind === "add" &&
    (after.options.length !== before.options.length + 1 || after.options.at(-1)?.name !== command.name || before.options.some(option => option.id === after.options.at(-1)?.id))) throw new HttpError("invalid-response", 200);
}
