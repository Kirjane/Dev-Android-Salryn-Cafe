import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  approveInvoiceVariance,
  approveSupplierPayment,
  createSupplierInvoice,
  fetchApAging,
  fetchPurchaseOrders,
  fetchReceivingReports,
  fetchSupplierInvoices,
  fetchSupplierPayments,
  fetchSuppliers,
  recordSupplierPayment,
  type SupplierInvoice,
  type SupplierPayment,
} from "../api/procureToPay";
import { fetchStoreSettings } from "../api/storeSettings";
import { PrintableChequeDisbursementModal } from "../components/documents/PrintableChequeDisbursementModal";
import { Button, Card, CardBody, Dialog, useNotifications } from "../components/ui";
import { useAppSession } from "../hooks/useAppSession";
import { Permissions, hasPermission } from "../security/permissions";
import { formatMoney } from "../utils/format";

const money = (cents: number) => Number.isFinite(cents) ? formatMoney(cents / 100) : "Amount unavailable";
const today = () => new Date().toISOString().slice(0, 10);
const inputClass = "w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900";
const labelClass = "block text-xs font-semibold text-slate-600 mb-1";

type InvoiceDraft = {
  supplierId: string;
  purchaseOrderId: string;
  receivingReportId: string;
  invoiceNumber: string;
  invoiceDate: string;
  dueDate: string;
  notes: string;
};

export function AccountsPayablePage() {
  const session = useAppSession();
  const { notify } = useNotifications();
  const queryClient = useQueryClient();
  const canView = hasPermission(session.data, Permissions.PayablesView);
  const canIntake = hasPermission(session.data, Permissions.PayablesManageInvoices);
  const canApproveVariance = hasPermission(session.data, Permissions.PayablesApproveVariance);
  const canRecordPayment = hasPermission(session.data, Permissions.PayablesRecordPayment);
  const canApprovePayment = hasPermission(session.data, Permissions.PayablesApprovePayment);
  const actorId = session.data?.user?.userId;

  const invoices = useQuery({ queryKey: ["lpi-invoices"], queryFn: () => fetchSupplierInvoices(), enabled: canView });
  const aging = useQuery({ queryKey: ["lpi-ap-aging"], queryFn: fetchApAging, enabled: canView });
  const payments = useQuery({ queryKey: ["lpi-payments"], queryFn: () => fetchSupplierPayments(), enabled: canView });
  const suppliers = useQuery({ queryKey: ["ap-suppliers"], queryFn: () => fetchSuppliers("", true), enabled: canIntake });
  const purchaseOrders = useQuery({ queryKey: ["ap-pos"], queryFn: () => fetchPurchaseOrders(), enabled: canIntake });
  const receivingReports = useQuery({ queryKey: ["ap-rrs"], queryFn: () => fetchReceivingReports(), enabled: canIntake });
  const storeSettings = useQuery({ queryKey: ["store-settings"], queryFn: fetchStoreSettings, enabled: canView });

  const [statusFilter, setStatusFilter] = useState("All");
  const [newInvoiceOpen, setNewInvoiceOpen] = useState(false);
  const [invoiceDraft, setInvoiceDraft] = useState<InvoiceDraft>({
    supplierId: "", purchaseOrderId: "", receivingReportId: "", invoiceNumber: "",
    invoiceDate: today(), dueDate: today(), notes: "",
  });
  const [lineQuantities, setLineQuantities] = useState<Record<string, string>>({});
  const [lineCosts, setLineCosts] = useState<Record<string, string>>({});
  const [varianceTarget, setVarianceTarget] = useState<SupplierInvoice | null>(null);
  const [varianceReason, setVarianceReason] = useState("");
  const [paymentTarget, setPaymentTarget] = useState<SupplierInvoice | null>(null);
  const [paymentAmount, setPaymentAmount] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("BankTransfer");
  const [paymentReference, setPaymentReference] = useState("");
  const [paymentBank, setPaymentBank] = useState("");
  const [paymentCheck, setPaymentCheck] = useState("");
  const [approvalTarget, setApprovalTarget] = useState<SupplierPayment | null>(null);
  const [approvalReason, setApprovalReason] = useState("");
  const [voucher, setVoucher] = useState<SupplierPayment | null>(null);
  const [busy, setBusy] = useState(false);

  const linkedPo = (purchaseOrders.data ?? []).find((po) => po.id === invoiceDraft.purchaseOrderId);
  const linkedRr = (receivingReports.data ?? []).find((rr) => rr.id === invoiceDraft.receivingReportId);
  const invoiceLines = (linkedRr?.items ?? []).filter((line) => line.acceptedQuantityX1000 > 0);
  const listedInvoices = (invoices.data ?? []).filter((inv) => statusFilter === "All" || inv.status === statusFilter);
  const outstanding = (aging.data ?? []).reduce((sum, row) => sum + row.totalOutstandingCents, 0);
  const overdue = (aging.data ?? []).reduce((sum, row) => sum + row.days1To30Cents + row.days31To60Cents + row.days61To90Cents + row.over90DaysCents, 0);
  const awaitingDocuments = (invoices.data ?? []).filter((inv) => inv.matchStatus === "PendingDocuments").length;

  const refresh = () => {
    for (const key of ["lpi-invoices", "lpi-ap-aging", "lpi-payments"]) {
      void queryClient.invalidateQueries({ queryKey: [key] });
    }
  };

  const submitInvoice = async () => {
    if (!canIntake || !linkedPo || !linkedRr || linkedRr.purchaseOrderId !== linkedPo.id) {
      notify.error("A matching purchase order and posted receiving report are required.");
      return;
    }
    if (!invoiceDraft.invoiceNumber.trim() || !invoiceDraft.invoiceDate || !invoiceDraft.dueDate) {
      notify.error("Invoice number and invoice/due dates are mandatory.");
      return;
    }
    const entries = invoiceLines.map((line) => {
      const qty = Number(lineQuantities[line.id] ?? (line.acceptedQuantityX1000 / 1000).toString());
      const unitCost = Number(lineCosts[line.id] ?? (line.unitCostCents / 100).toFixed(2));
      const poLine = linkedPo.items.find((item) => item.id === line.poItemId);
      return { line, poLine, qty, unitCost };
    }).filter((entry) => entry.qty > 0);
    if (entries.length === 0 || entries.some((e) =>
      !e.poLine || !Number.isFinite(e.qty) || !Number.isFinite(e.unitCost) ||
      e.qty <= 0 || e.unitCost <= 0 || !Number.isSafeInteger(Math.round(e.qty * 1000)) ||
      !Number.isSafeInteger(Math.round(e.unitCost * 100))
    )) {
      notify.error("Every selected invoice line needs a valid PO/RR match, positive quantity and positive unit cost.");
      return;
    }
    const items = entries.map((e) => ({
      productId: e.line.productId,
      invoicedQuantityX1000: Math.round(e.qty * 1000),
      unitCostCents: Math.round(e.unitCost * 100),
      poItemId: e.poLine!.id,
      rrItemId: e.line.id,
    }));
    const subtotalCents = items.reduce((sum, item) =>
      sum + Math.round(item.invoicedQuantityX1000 * item.unitCostCents / 1000), 0);
    if (!Number.isSafeInteger(subtotalCents) || subtotalCents <= 0) {
      notify.error("Invoice total must be greater than zero.");
      return;
    }
    setBusy(true);
    try {
      await createSupplierInvoice({
        supplierId: invoiceDraft.supplierId,
        invoiceNumber: invoiceDraft.invoiceNumber.trim(),
        invoiceDate: invoiceDraft.invoiceDate,
        dueDate: invoiceDraft.dueDate,
        purchaseOrderId: linkedPo.id,
        receivingReportId: linkedRr.id,
        subtotalCents,
        totalCents: subtotalCents,
        notes: invoiceDraft.notes.trim(),
        items,
      });
      setNewInvoiceOpen(false);
      setLineCosts({});
      setLineQuantities({});
      refresh();
      notify.success("Supplier invoice recorded. Matching status is available in the AP register.");
    } catch (error) { notify.error(error instanceof Error ? error.message : "Invoice intake failed."); }
    finally { setBusy(false); }
  };

  const submitPayment = async () => {
    if (!paymentTarget || !canRecordPayment) return;
    const cents = Math.round(Number(paymentAmount) * 100);
    if (!Number.isSafeInteger(cents) || cents <= 0 || cents > paymentTarget.balanceDueCents || !paymentReference.trim()) {
      notify.error("Payment requires a positive amount within the outstanding balance and a real reference.");
      return;
    }
    if (paymentMethod === "Check" && !paymentCheck.trim()) {
      notify.error("Check number is mandatory for check disbursements.");
      return;
    }
    setBusy(true);
    try {
      await recordSupplierPayment({
        supplierId: paymentTarget.supplierId, invoiceId: paymentTarget.id,
        amountCents: cents, paymentMethod, referenceNumber: paymentReference.trim(),
        bankAccount: paymentBank.trim() || undefined, checkNumber: paymentCheck.trim() || undefined,
        disbursementSource: paymentMethod === "Cash" ? "TreasuryFund" : "OperatingAccount",
      });
      setPaymentTarget(null);
      refresh();
      notify.success("Payment submitted for independent approval. No settlement has been asserted.");
    } catch (error) { notify.error(error instanceof Error ? error.message : "Payment recording failed."); }
    finally { setBusy(false); }
  };

  if (!canView) return <p role="alert" className="text-sm text-red-700">Access denied: Accounts Payable requires Payables.View.</p>;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-slate-200 pb-5">
        <div>
          <p className="text-xs font-semibold uppercase tracking-widest text-slate-500">Finance &amp; Analytics</p>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Accounts Payable</h1>
          <p className="mt-1 text-sm text-slate-600">Supplier invoices, document matching, liabilities, aging and controlled disbursements.</p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={refresh}>Refresh</Button>
          {canIntake && <Button onClick={() => setNewInvoiceOpen(true)}>Intake Supplier Invoice</Button>}
        </div>
      </header>

      <section aria-label="AP summary" className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <Card><CardBody><p className="text-xs text-slate-500">Outstanding AP</p><p className="text-2xl font-bold">{money(outstanding)}</p></CardBody></Card>
        <Card><CardBody><p className="text-xs text-slate-500">Overdue liability</p><p className="text-2xl font-bold">{money(overdue)}</p></CardBody></Card>
        <Card><CardBody><p className="text-xs text-slate-500">Awaiting documents</p><p className="text-2xl font-bold">{awaitingDocuments}</p></CardBody></Card>
      </section>

      <Card>
        <CardBody>
          <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
            <div><h2 className="text-lg font-semibold">Supplier Invoice Register</h2><p className="text-xs text-slate-500">Unmatched documents are pending verification, not automatically posted liabilities.</p></div>
            <select aria-label="Invoice status" className={inputClass} style={{ width: 190 }} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
              {["All", "PendingVerification", "VarianceFlagged", "Matched", "Paid", "Cancelled"].map((status) => <option key={status}>{status}</option>)}
            </select>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b bg-slate-50 text-xs uppercase text-slate-600"><tr>
                <th className="p-3">APV / Invoice</th><th className="p-3">Supplier</th><th className="p-3">Due</th>
                <th className="p-3 text-right">Amount</th><th className="p-3 text-right">Balance</th>
                <th className="p-3">Match / Posting</th><th className="p-3">Actions</th>
              </tr></thead>
              <tbody className="divide-y">
                {listedInvoices.map((inv) => <tr key={inv.id}>
                  <td className="p-3 font-mono">{inv.apvNumber}<div className="text-xs text-slate-500">{inv.invoiceNumber}</div></td>
                  <td className="p-3">{inv.supplierName}</td><td className="p-3">{inv.dueDate}</td>
                  <td className="p-3 text-right">{money(inv.totalCents)}</td>
                  <td className="p-3 text-right font-semibold">{money(inv.balanceDueCents)}</td>
                  <td className="p-3">{inv.matchStatus}<div className="text-xs text-slate-500">{inv.status}</div></td>
                  <td className="p-3">
                    <div className="flex flex-wrap gap-2">
                      {canApproveVariance && ["PriceVariance", "QtyVariance", "QuantityVariance", "QtyAndPriceVariance"].includes(inv.matchStatus) &&
                        <Button variant="secondary" onClick={() => { setVarianceTarget(inv); setVarianceReason(""); }}>Review variance</Button>}
                      {canRecordPayment && inv.balanceDueCents > 0 && inv.totalCents > 0 && ["Matched", "VarianceApproved"].includes(inv.matchStatus) &&
                        <Button onClick={() => { setPaymentTarget(inv); setPaymentAmount((inv.balanceDueCents / 100).toFixed(2)); setPaymentMethod("BankTransfer"); setPaymentReference(""); setPaymentBank(""); setPaymentCheck(""); }}>Prepare payment</Button>}
                    </div>
                  </td>
                </tr>)}
                {listedInvoices.length === 0 && <tr><td colSpan={7} className="p-6 text-center text-slate-500">No supplier invoices match this filter.</td></tr>}
              </tbody>
            </table>
          </div>
        </CardBody>
      </Card>

      <Card><CardBody><h2 className="mb-3 text-lg font-semibold">Accounts Payable Aging</h2>
        <div className="overflow-x-auto"><table className="w-full text-left text-sm">
          <thead className="border-b bg-slate-50 text-xs uppercase"><tr><th className="p-2">Supplier</th><th className="p-2 text-right">Current</th><th className="p-2 text-right">1–30</th><th className="p-2 text-right">31–60</th><th className="p-2 text-right">61–90</th><th className="p-2 text-right">90+</th><th className="p-2 text-right">Total</th></tr></thead>
          <tbody>{(aging.data ?? []).map((row) => <tr key={row.supplierId} className="border-b">
            <td className="p-2">{row.supplierName}</td><td className="p-2 text-right">{money(row.currentCents)}</td>
            <td className="p-2 text-right">{money(row.days1To30Cents)}</td><td className="p-2 text-right">{money(row.days31To60Cents)}</td>
            <td className="p-2 text-right">{money(row.days61To90Cents)}</td><td className="p-2 text-right">{money(row.over90DaysCents)}</td>
            <td className="p-2 text-right font-semibold">{money(row.totalOutstandingCents)}</td>
          </tr>)}</tbody>
        </table></div>
      </CardBody></Card>

      <Card><CardBody><h2 className="mb-3 text-lg font-semibold">Supplier Payments &amp; Disbursements</h2>
        <div className="overflow-x-auto"><table className="w-full text-left text-sm">
          <thead className="border-b bg-slate-50 text-xs uppercase"><tr><th className="p-2">Payment</th><th className="p-2">Supplier</th><th className="p-2">Method / Reference</th><th className="p-2 text-right">Amount</th><th className="p-2">Status</th><th className="p-2">Actions</th></tr></thead>
          <tbody>{(payments.data ?? []).map((pay) => <tr key={pay.id} className="border-b">
            <td className="p-2 font-mono">{pay.paymentNumber}</td><td className="p-2">{pay.supplierName}</td>
            <td className="p-2">{pay.paymentMethod}<div className="text-xs text-slate-500">{pay.referenceNumber || "Reference not entered"}</div></td>
            <td className="p-2 text-right">{money(pay.amountCents)}</td><td className="p-2">{pay.status}</td>
            <td className="p-2"><div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => setVoucher(pay)}>Print voucher</Button>
              {pay.status === "PendingApproval" && canApprovePayment && actorId !== pay.recordedByUserId &&
                <Button onClick={() => { setApprovalTarget(pay); setApprovalReason(""); }}>Approve settlement</Button>}
            </div></td>
          </tr>)}</tbody>
        </table></div>
      </CardBody></Card>

      <Dialog open={newInvoiceOpen} onClose={() => setNewInvoiceOpen(false)} title="Supplier Invoice Intake — 3-Way Match">
        <div className="space-y-4">
          <p className="text-sm text-slate-600">Select the supplier, approved PO and posted receiving report. An invoice is not payable without matching supporting documents.</p>
          <label className={labelClass}>Supplier</label>
          <select className={inputClass} value={invoiceDraft.supplierId} onChange={(e) => { setInvoiceDraft({ ...invoiceDraft, supplierId: e.target.value, purchaseOrderId: "", receivingReportId: "" }); setLineCosts({}); setLineQuantities({}); }}>
            <option value="">Select supplier</option>{(suppliers.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
          <label className={labelClass}>Approved Purchase Order</label>
          <select className={inputClass} value={invoiceDraft.purchaseOrderId} onChange={(e) => { setInvoiceDraft({ ...invoiceDraft, purchaseOrderId: e.target.value, receivingReportId: "" }); setLineCosts({}); setLineQuantities({}); }}>
            <option value="">Select PO</option>{(purchaseOrders.data ?? []).filter((po) => po.supplierId === invoiceDraft.supplierId && !["Draft", "Cancelled"].includes(po.status)).map((po) => <option key={po.id} value={po.id}>{po.poNumber} ({po.status})</option>)}
          </select>
          <label className={labelClass}>Posted Receiving Report</label>
          <select className={inputClass} value={invoiceDraft.receivingReportId} onChange={(e) => { setInvoiceDraft({ ...invoiceDraft, receivingReportId: e.target.value }); setLineCosts({}); setLineQuantities({}); }}>
            <option value="">Select RR</option>{(receivingReports.data ?? []).filter((rr) => rr.status === "Posted" && rr.purchaseOrderId === invoiceDraft.purchaseOrderId && rr.supplierId === invoiceDraft.supplierId).map((rr) => <option key={rr.id} value={rr.id}>{rr.rrNumber}</option>)}
          </select>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <div><label className={labelClass}>Supplier Invoice #</label><input className={inputClass} value={invoiceDraft.invoiceNumber} onChange={(e) => setInvoiceDraft({ ...invoiceDraft, invoiceNumber: e.target.value })} /></div>
            <div><label className={labelClass}>Invoice Date</label><input type="date" className={inputClass} value={invoiceDraft.invoiceDate} onChange={(e) => setInvoiceDraft({ ...invoiceDraft, invoiceDate: e.target.value })} /></div>
            <div><label className={labelClass}>Due Date</label><input type="date" className={inputClass} value={invoiceDraft.dueDate} onChange={(e) => setInvoiceDraft({ ...invoiceDraft, dueDate: e.target.value })} /></div>
          </div>
          {linkedRr && <div className="space-y-2"><h3 className="text-sm font-semibold">Received lines</h3>
            {invoiceLines.map((line) => <div key={line.id} className="grid grid-cols-1 gap-2 rounded border border-slate-200 p-3 sm:grid-cols-3">
              <p className="text-sm">{line.productName}</p>
              <div><label className={labelClass}>Invoiced quantity (0 to skip)</label><input type="number" min="0" step="0.001" className={inputClass} value={lineQuantities[line.id] ?? (line.acceptedQuantityX1000 / 1000).toString()} onChange={(e) => setLineQuantities({ ...lineQuantities, [line.id]: e.target.value })} /></div>
              <div><label className={labelClass}>Unit cost (PHP)</label><input type="number" min="0.01" step="0.01" className={inputClass} value={lineCosts[line.id] ?? (line.unitCostCents / 100).toFixed(2)} onChange={(e) => setLineCosts({ ...lineCosts, [line.id]: e.target.value })} /></div>
            </div>)}
          </div>}
          <div><label className={labelClass}>Notes</label><textarea className={inputClass} rows={2} value={invoiceDraft.notes} onChange={(e) => setInvoiceDraft({ ...invoiceDraft, notes: e.target.value })} /></div>
          <div className="flex justify-end gap-2"><Button variant="secondary" onClick={() => setNewInvoiceOpen(false)}>Cancel</Button><Button disabled={busy || !linkedRr || !linkedPo} onClick={() => void submitInvoice()}>Record &amp; Match Invoice</Button></div>
        </div>
      </Dialog>

      <Dialog open={Boolean(varianceTarget)} onClose={() => setVarianceTarget(null)} title="Variance Authorization">
        <div className="space-y-3"><p className="text-sm">Approving a variance authorizes its AP posting. Provide a documented justification.</p>
          <textarea className={inputClass} rows={3} value={varianceReason} onChange={(e) => setVarianceReason(e.target.value)} placeholder="Reason for the variance approval" />
          <div className="flex justify-end gap-2"><Button variant="secondary" onClick={() => setVarianceTarget(null)}>Cancel</Button>
            <Button disabled={busy || !varianceReason.trim()} onClick={async () => { if (!varianceTarget) return; setBusy(true); try { await approveInvoiceVariance(varianceTarget.id, varianceReason.trim()); refresh(); setVarianceTarget(null); notify.success("Variance authorization recorded."); } catch (error) { notify.error(error instanceof Error ? error.message : "Authorization failed."); } finally { setBusy(false); } }}>Authorize variance</Button>
          </div>
        </div>
      </Dialog>

      <Dialog open={Boolean(paymentTarget)} onClose={() => setPaymentTarget(null)} title="Prepare Supplier Disbursement">
        <div className="space-y-3">
          <p className="text-sm">This creates a pending payment request. Settlement requires a different authorized operator.</p>
          <div><label className={labelClass}>Amount (PHP)</label><input type="number" min="0.01" step="0.01" className={inputClass} value={paymentAmount} onChange={(e) => setPaymentAmount(e.target.value)} /></div>
          <div><label className={labelClass}>Method</label><select className={inputClass} value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)}><option>BankTransfer</option><option>Check</option><option>Cash</option></select></div>
          <div><label className={labelClass}>Actual payment reference</label><input className={inputClass} value={paymentReference} onChange={(e) => setPaymentReference(e.target.value)} /></div>
          {paymentMethod !== "Cash" && <div><label className={labelClass}>Bank / Account (if applicable)</label><input className={inputClass} value={paymentBank} onChange={(e) => setPaymentBank(e.target.value)} /></div>}
          {paymentMethod === "Check" && <div><label className={labelClass}>Check number</label><input className={inputClass} value={paymentCheck} onChange={(e) => setPaymentCheck(e.target.value)} /></div>}
          <div className="flex justify-end gap-2"><Button variant="secondary" onClick={() => setPaymentTarget(null)}>Cancel</Button><Button disabled={busy} onClick={() => void submitPayment()}>Submit for Approval</Button></div>
        </div>
      </Dialog>

      <Dialog open={Boolean(approvalTarget)} onClose={() => setApprovalTarget(null)} title="Independent Payment Approval">
        <div className="space-y-3">
          <p className="text-sm">{approvalTarget?.paymentNumber} — {approvalTarget ? money(approvalTarget.amountCents) : ""}</p>
          <div><label className={labelClass}>Approval remarks</label><textarea rows={2} className={inputClass} value={approvalReason} onChange={(e) => setApprovalReason(e.target.value)} /></div>
          <div className="flex justify-end gap-2"><Button variant="secondary" onClick={() => setApprovalTarget(null)}>Cancel</Button>
            <Button disabled={busy || !approvalReason.trim() || approvalTarget?.recordedByUserId === actorId} onClick={async () => { if (!approvalTarget) return; setBusy(true); try { await approveSupplierPayment(approvalTarget.id, approvalReason.trim()); refresh(); setApprovalTarget(null); notify.success("Settlement approved by independent operator."); } catch (error) { notify.error(error instanceof Error ? error.message : "Settlement failed."); } finally { setBusy(false); } }}>Authorize settlement</Button>
          </div>
        </div>
      </Dialog>
      <PrintableChequeDisbursementModal open={Boolean(voucher)} onClose={() => setVoucher(null)} payment={voucher} storeSettings={storeSettings.data} />
    </div>
  );
}
