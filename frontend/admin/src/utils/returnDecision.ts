import type { OrderReturn } from "../api";
import type { ReturnDecision } from "../api/returns";

export function canDecideReturn(record: OrderReturn, decision: ReturnDecision) {
  return decision === "accept" ? record.status === "Requested" : record.status === "Requested" || record.status === "Accepted";
}
