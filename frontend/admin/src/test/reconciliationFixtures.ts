import { differenceKinds, type ReconciliationReport, type ReportRange } from "../api/reconciliation";

export function reconciliationFixture(range: ReportRange = { from: "2026-10-01", to: "2026-10-14" }): ReconciliationReport {
  const differences = differenceKinds.map((kind, index) => ({ date: range.from, kind, explained: index < 2,
    orderNumber: index < 2 ? "2026/17" : null, transactionId: "TX-" + index,
    expected: 10, settled: 10, currency: "EUR", detail: kind === "amountDiffers" ? "ShopForge asked for 10.00 EUR; statement confirms 10.00 CZK." : "Literal <script>statement text</script> " + index }));
  const unexplained = differences.filter(item => !item.explained).length;
  const first = Date.parse(range.from + "T00:00:00Z"), last = Date.parse(range.to + "T00:00:00Z");
  const days = Array.from({ length: (last - first) / 86400000 + 1 }, (_, index) => ({ date: new Date(first + index * 86400000).toISOString().slice(0, 10),
    paid: 2, paidAmount: 98765.43, settled: 3, settledAmount: 98765.43, fees: 98765.43, transferred: 98765.43, unexplained: index === 0 ? unexplained : 0 }));
  return { ...range, provider: "comgate", timeZone: "Europe/Prague", statementsThrough: "2026-10-12", testPayments: 2, unexplained, days, differences };
}
