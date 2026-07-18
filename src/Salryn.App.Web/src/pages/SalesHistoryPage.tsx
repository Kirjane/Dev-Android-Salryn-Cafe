import { type ReactNode, useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchSaleDetail, fetchSalesHistory, printReceiptForSale, type ReceiptPrintResult, type SaleHistoryItem } from "../api/sales";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, DataTable, ErrorState, LoadingState, StatusPill, TextInput, useNotifications } from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";
import { voidSale } from "../api/saleVoid";
import { safeNotificationDescription } from "../utils/notificationError";

function Dialog({ title, children, footer, onClose }: { title: string; children: ReactNode; footer: ReactNode; onClose: () => void }) {
  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) { if (event.key === "Escape") { event.preventDefault(); onClose(); } }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);
  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4" role="dialog" aria-modal="true"><div className="max-h-[90vh] w-full max-w-4xl overflow-hidden rounded-xl bg-white shadow-2xl"><div className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4"><div><h2 className="text-lg font-semibold text-slate-950">{title}</h2><p className="mt-1 text-xs text-slate-500">Read-only sale detail. Reprint uses persisted receipt snapshot/file source only. Esc closes this dialog.</p></div><Button variant="ghost" size="sm" onClick={onClose}>Close</Button></div><div className="max-h-[62vh] overflow-y-auto px-5 py-4">{children}</div><div className="flex flex-wrap justify-end gap-2 border-t border-slate-200 bg-slate-50 px-5 py-4">{footer}</div></div></div>;
}

function printTone(status: string | null | undefined): "success" | "warning" | "danger" | "info" {
  return status === "Succeeded" ? "success" : status === "Failed" ? "danger" : status === "Disabled/Unavailable" ? "warning" : "info";
}

export function SalesHistoryPage({ canVoid = false }: { canVoid?: boolean }) {
  const { notify } = useNotifications();
  const [receiptNo, setReceiptNo] = useState("");
  const [status, setStatus] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isReprinting, setIsReprinting] = useState(false);
  const [reprintResult, setReprintResult] = useState<ReceiptPrintResult | null>(null);
  const [voidReason, setVoidReason] = useState("");
  const [voidConfirmed, setVoidConfirmed] = useState(false);
  const [voidMessage, setVoidMessage] = useState<string | null>(null);
  const [isVoiding, setIsVoiding] = useState(false);

  const salesQuery = useQuery({ queryKey: ["sales-history", receiptNo, status, from, to, page], queryFn: () => fetchSalesHistory({ receiptNo: receiptNo || undefined, status: status || undefined, from: from || undefined, to: to || undefined, page, pageSize: 25 }) });
  const detailQuery = useQuery({ queryKey: ["sale-detail", selectedId], queryFn: () => fetchSaleDetail(selectedId!), enabled: selectedId !== null });

  useEffect(() => { if (salesQuery.data && salesQuery.data.totalPages > 0 && page > salesQuery.data.totalPages) setPage(salesQuery.data.totalPages); }, [page, salesQuery.data]);
  useEffect(() => { setSelectedId(null); }, [receiptNo, status, from, to]);
  useEffect(() => { setReprintResult(null); setVoidReason(""); setVoidConfirmed(false); setVoidMessage(null); }, [selectedId]);

  function resetFilters() { setReceiptNo(""); setStatus(""); setFrom(""); setTo(""); setPage(1); }

  async function handleReprint() {
    if (!detailQuery.data?.id || isReprinting) return;
    setIsReprinting(true); setReprintResult(null);
    try {
      const result = await printReceiptForSale(detailQuery.data.id);
      setReprintResult(result.print);
      if (result.print.succeeded || result.print.status === "Succeeded") {
        notify.success("Receipt reprinted", "The saved receipt was sent to the selected Windows printer.");
      } else if (result.print.status === "Disabled/Unavailable") {
        notify.warning("Receipt was not reprinted", "Printing is disabled or the selected printer is unavailable.");
      } else {
        notify.error("Reprint failed", safeNotificationDescription(new Error(result.print.errorMessage || result.print.message), "Check printer settings and try again."));
      }
    }
    catch (error) {
      setReprintResult({ succeeded: false, status: "Failed", saleId: detailQuery.data.id, receiptNumber: detailQuery.data.receiptNo, source: "PersistedReceiptSnapshot", adapterName: "Unknown", message: "Reprint request failed. Sale, stock, payments, and receipt identity remain unchanged.", errorMessage: error instanceof Error ? error.message : "Unknown reprint error.", printedAtUtc: null });
      notify.error("Reprint failed", safeNotificationDescription(error, "Check printer settings and try again."));
    }
    finally { setIsReprinting(false); }
  }

  async function handleVoid() {
    if (!detailQuery.data?.id || !voidReason.trim() || !voidConfirmed || isVoiding) return;
    setIsVoiding(true); setVoidMessage(null);
    try { const result = await voidSale(detailQuery.data.id, voidReason.trim()); setVoidMessage(`${result.message} Reversal movements: ${result.reversalMovementCount}.`); setVoidReason(""); setVoidConfirmed(false); notify.success("Sale voided", "Stock was restored through recorded reversal movements."); await Promise.all([detailQuery.refetch(), salesQuery.refetch()]); }
    catch (error) { setVoidMessage(error instanceof Error ? error.message : "Sale void failed."); notify.error("Void failed", safeNotificationDescription(error, "The sale was not voided.")); }
    finally { setIsVoiding(false); }
  }

  const columns = [
    { key: "receipt", header: "Receipt", render: (row: SaleHistoryItem) => row.receiptNo },
    { key: "date", header: "Date", render: (row: SaleHistoryItem) => formatDateTime(row.saleDate) },
    { key: "cashier", header: "Cashier", render: (row: SaleHistoryItem) => row.cashierDisplayName },
    { key: "total", header: "Total", align: "right" as const, render: (row: SaleHistoryItem) => formatMoney(row.total) },
    { key: "status", header: "Status", render: (row: SaleHistoryItem) => <StatusPill tone={row.status === "Completed" ? "success" : "warning"}>{row.status}</StatusPill> },
    { key: "view", header: "Details", render: (row: SaleHistoryItem) => <Button size="sm" variant="secondary" onClick={() => setSelectedId(row.id)}>Sale & Receipt Details</Button> },
  ];

  const isVoided = detailQuery.data?.status === "Voided";
  const canReprint = Boolean(detailQuery.data?.receiptSnapshot?.trim()) && !isVoided;

  return (
    <div className="space-y-4 overflow-hidden">
      <CommandBar title="Sales & Receipts" subtitle="Find completed or voided sales, review the saved receipt, and reprint without changing sale, payment, or stock records." actions={<Button variant="secondary" onClick={() => void salesQuery.refetch()}>Refresh</Button>} />
      <Alert tone="info" title="Saved sales and receipts">Open Sale & Receipt Details to review the saved receipt. Reprint reads that saved snapshot only and does not create another sale, payment, or stock movement.</Alert>
      <Card className="flex min-h-[34rem] flex-col xl:h-[calc(100vh-13rem)]"><CardHeader className="shrink-0"><CardTitle>Sales search</CardTitle></CardHeader><CardBody className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden"><div className="flex shrink-0 flex-wrap gap-3"><TextInput className="min-w-0 flex-[2_1_14rem]" placeholder="Receipt number..." value={receiptNo} onChange={(event) => { setPage(1); setReceiptNo(event.target.value); }} /><select className="w-auto min-w-0 flex-[1_1_10rem] rounded-lg border border-slate-300 px-3 py-2 text-sm" value={status} onChange={(event) => { setPage(1); setStatus(event.target.value); }}><option value="">All statuses</option><option value="Completed">Completed</option><option value="Voided">Voided</option></select><TextInput className="min-w-0 flex-[1_1_10rem]" type="date" aria-label="From date" value={from} onChange={(event) => { setPage(1); setFrom(event.target.value); }} /><TextInput className="min-w-0 flex-[1_1_10rem]" type="date" aria-label="To date" value={to} onChange={(event) => { setPage(1); setTo(event.target.value); }} /><Button className="flex-none" variant="secondary" onClick={resetFilters} disabled={!receiptNo && !status && !from && !to}>Clear</Button></div><p className="shrink-0 text-xs text-slate-500">Date filters use closed date controls. Receipt remains the only free-text filter.</p>{salesQuery.isLoading ? <LoadingState label="Loading sales..." /> : null}{salesQuery.isError ? <ErrorState message={(salesQuery.error as Error).message} /> : null}<div className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1">{salesQuery.data ? <DataTable columns={columns} rows={salesQuery.data.items} getRowKey={(row) => row.id} selectedRowKey={selectedId} emptyTitle="No sales found" emptyDescription="Try adjusting receipt, date, or status filters." /> : null}</div>{salesQuery.data ? <div className="flex shrink-0 items-center justify-between border-t border-slate-100 pt-3 text-sm text-slate-500"><span>{salesQuery.data.totalCount.toLocaleString()} sales · page {salesQuery.data.pageNumber} of {Math.max(1, salesQuery.data.totalPages || 1)}</span><div className="flex gap-2"><Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => setPage((current) => current - 1)}>Previous</Button><Button variant="secondary" size="sm" disabled={page >= salesQuery.data.totalPages} onClick={() => setPage((current) => current + 1)}>Next</Button></div></div> : null}</CardBody></Card>
      {selectedId ? <Dialog title={detailQuery.data ? `Receipt ${detailQuery.data.receiptNo}` : "Sale detail"} onClose={() => setSelectedId(null)} footer={<><Button variant="secondary" onClick={() => setSelectedId(null)}>Close</Button><Button variant="success" onClick={() => void handleReprint()} disabled={!canReprint || isReprinting}>{isVoided ? "Reprint blocked: VOIDED" : isReprinting ? "Reprinting..." : "Reprint Receipt"}</Button></>}>{detailQuery.isLoading ? <LoadingState label="Loading sale detail..." /> : null}{detailQuery.isError ? <ErrorState message={(detailQuery.error as Error).message} /> : null}{detailQuery.data ? <div className="space-y-4 text-sm text-slate-600"><div className="flex flex-wrap items-center gap-2"><StatusPill tone={detailQuery.data.status === "Completed" ? "success" : "warning"}>{detailQuery.data.status}</StatusPill><span>{formatDateTime(detailQuery.data.saleDate)}</span><span>Cashier: {detailQuery.data.cashierDisplayName}</span></div><div className="grid gap-3 sm:grid-cols-5"><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Subtotal</p><p className="font-semibold text-slate-950">{formatMoney(detailQuery.data.subtotal)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Discount</p><p className="font-semibold text-slate-950">{formatMoney(detailQuery.data.discount)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Tax</p><p className="font-semibold text-slate-950">{formatMoney(detailQuery.data.tax)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Total</p><p className="font-semibold text-slate-950">{formatMoney(detailQuery.data.total)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Change</p><p className="font-semibold text-emerald-700">{formatMoney(detailQuery.data.change)}</p></div></div>{detailQuery.data.voidReason ? <Alert tone="warning">Void reason: {detailQuery.data.voidReason}</Alert> : null}{detailQuery.data.voidedAt ? <Alert tone="warning" title="VOIDED">Voided at {formatDateTime(detailQuery.data.voidedAt)}. The original persisted receipt snapshot remains unchanged; normal reprint is blocked.</Alert> : null}{canVoid && detailQuery.data.status === "Completed" ? <div className="space-y-2 rounded-md border border-rose-200 bg-rose-50 p-3"><label className="text-sm font-medium text-rose-950">Void reason</label><TextInput value={voidReason} onChange={(event) => { setVoidReason(event.target.value); setVoidConfirmed(false); }} placeholder="Required reason..." /><label className="flex items-start gap-2 rounded-md border border-rose-200 bg-white p-3 text-xs text-rose-900"><input type="checkbox" className="mt-0.5" checked={voidConfirmed} onChange={(event) => setVoidConfirmed(event.target.checked)} /><span>I understand the original sale, payment, and saved receipt remain historical while stock is restored through reversal movements.</span></label><Button variant="danger" onClick={() => void handleVoid()} disabled={!voidReason.trim() || !voidConfirmed || isVoiding}>{isVoiding ? "Voiding..." : "Confirm Void Sale"}</Button><p className="text-xs text-rose-800">This action cannot be repeated after the sale is marked voided.</p></div> : null}{voidMessage ? <Alert tone={detailQuery.data.status === "Voided" ? "success" : "danger"}>{voidMessage}</Alert> : null}<Alert tone={canReprint ? "info" : "warning"} title="Reprint source">{canReprint ? "Persisted receipt snapshot is available. Reprint will use that stored text only." : "No persisted receipt snapshot was exposed for this sale detail, so reprint is disabled in the UI."}</Alert>{detailQuery.data.receiptSnapshot ? <div className="rounded-xl border border-slate-300 bg-white p-4 shadow-inner"><div className="mb-3 flex items-center justify-between gap-3 border-b border-dashed border-slate-300 pb-2"><h3 className="text-sm font-semibold text-slate-950">Receipt Snapshot Preview</h3><StatusPill tone="info">Saved text</StatusPill></div><pre className="max-h-80 overflow-y-auto whitespace-pre-wrap break-words rounded-md bg-slate-50 p-3 font-mono text-xs leading-relaxed text-slate-800">{detailQuery.data.receiptSnapshot}</pre></div> : null}{reprintResult ? <Alert tone={printTone(reprintResult.status)} title="Reprint status">{reprintResult.message}{reprintResult.errorMessage ? ` — ${reprintResult.errorMessage}` : ""} Source: {reprintResult.source}. Adapter: {reprintResult.adapterName}.</Alert> : null}<DataTable columns={[{ key: "item", header: "Item", render: (row) => row.productNameSnapshot }, { key: "qty", header: "Qty", align: "right" as const, render: (row) => formatQuantity(row.quantity) }, { key: "unit", header: "Unit", align: "right" as const, render: (row) => formatMoney(row.unitPrice) }, { key: "total", header: "Line total", align: "right" as const, render: (row) => formatMoney(row.lineTotal) }]} rows={detailQuery.data.items} getRowKey={(row) => row.id} /><div><h3 className="mb-2 text-sm font-semibold text-slate-900">Payments</h3><DataTable columns={[{ key: "method", header: "Method", render: (row) => row.method }, { key: "amount", header: "Amount", align: "right" as const, render: (row) => formatMoney(row.amount) }, { key: "paidAt", header: "Paid at", render: (row) => formatDateTime(row.paidAt) }]} rows={detailQuery.data.payments} getRowKey={(row) => row.id} /></div></div> : null}</Dialog> : null}
    </div>
  );
}
