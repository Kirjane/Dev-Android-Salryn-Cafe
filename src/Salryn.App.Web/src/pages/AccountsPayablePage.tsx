import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  approveInvoiceVariance,
  approveSupplierPayment,
  createSupplierInvoice,
  fetchApAging,
  fetchPurchaseOrders,
  fetchReceivingReports,
  fetchSupplierInvoices,
  fetchSupplierPayableLedger,
  fetchSupplierPayments,
  fetchSuppliers,
  recordSupplierPayment,
  type SupplierInvoice,
  type SupplierPayableEntry,
  type SupplierPayment,
} from "../api/procureToPay";
import { fetchStoreSettings } from "../api/storeSettings";
import { PrintableChequeDisbursementModal } from "../components/documents/PrintableChequeDisbursementModal";
import { Button, Card, CardBody, Dialog, useNotifications } from "../components/ui";
import { useAppSession } from "../hooks/useAppSession";
import { Permissions, hasPermission } from "../security/permissions";
import { formatMoney } from "../utils/format";
import { philippineBusinessToday, isValidActualFundsTransferDate } from "../utils/supplierPaymentBusinessDate";

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

  const invoices = useQuery({ queryKey: ["lpi-invoices"], queryFn: () => fetchSupplierInvoices(), enabled: canView });
  const aging = useQuery({ queryKey: ["lpi-ap-aging"], queryFn: () => fetchApAging(), enabled: canView });
  const payments = useQuery({ queryKey: ["lpi-payments"], queryFn: () => fetchSupplierPayments(), enabled: canView });
  const suppliers = useQuery({ queryKey: ["ap-suppliers"], queryFn: () => fetchSuppliers("", true), enabled: canIntake });
  const purchaseOrders = useQuery({ queryKey: ["ap-pos"], queryFn: () => fetchPurchaseOrders(), enabled: canIntake });
  const receivingReports = useQuery({ queryKey: ["ap-rrs"], queryFn: () => fetchReceivingReports(), enabled: canIntake });
  const storeSettings = useQuery({ queryKey: ["store-settings"], queryFn: fetchStoreSettings, enabled: canView });

  const [statusFilter, setStatusFilter] = useState("All");
  const [ledgerSupplierFilter, setLedgerSupplierFilter] = useState("");
  const [ledgerTypeFilter, setLedgerTypeFilter] = useState("All");
  const [reconcileTarget, setReconcileTarget] = useState<SupplierInvoice | null>(null);
  const ledger = useQuery({
    queryKey: ["lpi-payables-ledger", ledgerSupplierFilter],
    queryFn: () => fetchSupplierPayableLedger(ledgerSupplierFilter || undefined),
    enabled: canView,
  });
  const reconcileLedger = useQuery({
    queryKey: ["lpi-payables-ledger", reconcileTarget?.supplierId ?? "none"],
    queryFn: () => fetchSupplierPayableLedger(reconcileTarget!.supplierId),
    enabled: canView && Boolean(reconcileTarget),
  });
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
  const [settlementConfirmation, setSettlementConfirmation] = useState("");
  const [actualDisbursementDate, setActualDisbursementDate] = useState("");
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

  const ledgerSupplierOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const inv of invoices.data ?? []) map.set(inv.supplierId, inv.supplierName);
    for (const row of aging.data ?? []) map.set(row.supplierId, row.supplierName);
    for (const entry of ledger.data ?? []) map.set(entry.supplierId, entry.supplierName);
    for (const pay of payments.data ?? []) map.set(pay.supplierId, pay.supplierName);
    return Array.from(map.entries())
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [invoices.data, aging.data, ledger.data, payments.data]);

  const ledgerEntries = ledger.data ?? [];
  const filteredLedgerEntries = ledgerEntries.filter(
    (entry) => ledgerTypeFilter === "All" || entry.entryType === ledgerTypeFilter,
  );
  const ledgerOpeningCents = 0;
  const ledgerCreditsCents = ledgerEntries.reduce((sum, entry) => sum + entry.creditCents, 0);
  const ledgerDebitsCents = ledgerEntries.reduce((sum, entry) => sum + entry.debitCents, 0);
  const returnCreditsAppliedCents = ledgerEntries
    .filter((entry) => entry.entryType === "SupplierReturnCredit")
    .reduce((sum, entry) => sum + entry.debitCents, 0);
  const settledPaymentDebitsCents = ledgerEntries
    .filter((entry) => entry.entryType === "PaymentApplied")
    .reduce((sum, entry) => sum + entry.debitCents, 0);
  const ledgerClosingCents = ledgerOpeningCents + ledgerCreditsCents - ledgerDebitsCents;
  const pendingApprovalCents = (payments.data ?? [])
    .filter((pay) => pay.status === "PendingApproval" && (!ledgerSupplierFilter || pay.supplierId === ledgerSupplierFilter))
    .reduce((sum, pay) => sum + pay.amountCents, 0);

  const invoiceReconcileEntries: SupplierPayableEntry[] = useMemo(() => {
    if (!reconcileTarget) return [];
    const source = reconcileLedger.data ?? [];
    return source
      .filter((entry) => entry.supplierInvoiceId === reconcileTarget.id)
      .slice()
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id));
  }, [reconcileLedger.data, reconcileTarget]);

  const invoiceReconcilePayments = useMemo(() => {
    if (!reconcileTarget) return [];
    return (payments.data ?? []).filter((pay) => pay.invoiceId === reconcileTarget.id);
  }, [payments.data, reconcileTarget]);

  const refresh = () => {
    for (const key of ["lpi-invoices", "lpi-ap-aging", "lpi-payments", "lpi-overview", "lpi-payables-ledger"]) {
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
        bankName: paymentBank.trim() || undefined, checkNumber: paymentCheck.trim() || undefined,
        disbursementSource: paymentMethod === "Cash" ? "TreasuryFund" : "OperatingAccount",
      });
      setPaymentTarget(null);
      refresh();
      notify.success("Payment request recorded as pending. No actual settlement has been asserted.");
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
              {["All", "PendingVerification", "VarianceFlagged", "PostedOpen", "PartiallyPaid", "Paid", "Cancelled"].map((status) => <option key={status}>{status}</option>)}
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
                      <Button variant="secondary" onClick={() => setReconcileTarget(inv)}>Reconcile</Button>
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

      <Card><CardBody>
        <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Supplier Payable Subledger</h2>
            <p className="text-xs text-slate-500">Posted invoice liabilities (credits) minus applied supplier-return credits and operator-confirmed settlements (debits). Pending payment requests do not reduce AP.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <select aria-label="Subledger supplier filter" className={inputClass} style={{ width: 210 }} value={ledgerSupplierFilter} onChange={(e) => setLedgerSupplierFilter(e.target.value)}>
              <option value="">All Suppliers</option>
              {ledgerSupplierOptions.map((sup) => <option key={sup.id} value={sup.id}>{sup.name}</option>)}
            </select>
            <select aria-label="Subledger entry type filter" className={inputClass} style={{ width: 200 }} value={ledgerTypeFilter} onChange={(e) => setLedgerTypeFilter(e.target.value)}>
              {["All", "InvoicePosted", "SupplierReturnCredit", "PaymentApplied"].map((t) => <option key={t}>{t}</option>)}
            </select>
          </div>
        </div>

        <div className="mb-4 grid grid-cols-1 gap-2 rounded-md border border-slate-200 bg-slate-50 p-3 text-xs sm:grid-cols-5">
          <div><p className="text-slate-500">Opening AP</p><p className="text-sm font-semibold tabular-nums">{money(ledgerOpeningCents)}</p></div>
          <div><p className="text-slate-500">+ Posted Credits (APV)</p><p className="text-sm font-semibold tabular-nums text-slate-900">{money(ledgerCreditsCents)}</p></div>
          <div>
            <p className="text-slate-500">− Applied Debits</p>
            <p className="text-sm font-semibold tabular-nums text-emerald-700">{money(ledgerDebitsCents)}</p>
            <p className="text-[11px] text-slate-500">Returns {money(returnCreditsAppliedCents)} · Paid {money(settledPaymentDebitsCents)}</p>
          </div>
          <div><p className="text-slate-500">= Net Ledger Closing AP</p><p className="text-sm font-bold tabular-nums text-slate-950">{money(ledgerClosingCents)}</p></div>
          <div><p className="text-slate-500">Pending Approval (Not Deducted)</p><p className="text-sm font-semibold tabular-nums text-amber-700">{money(pendingApprovalCents)}</p></div>
        </div>

        <div className="overflow-x-auto"><table className="w-full text-left text-sm">
          <thead className="border-b bg-slate-50 text-xs uppercase text-slate-600"><tr>
            <th className="p-2">Posted At</th><th className="p-2">Supplier</th><th className="p-2">Entry / Source</th><th className="p-2">APV #</th>
            <th className="p-2 text-right">Credit (+AP)</th><th className="p-2 text-right">Debit (−AP)</th><th className="p-2 text-right">Supplier Bal After</th><th className="p-2">Actor / Notes</th>
          </tr></thead>
          <tbody className="divide-y">{filteredLedgerEntries.map((entry) => {
            const matchedInv = (invoices.data ?? []).find((inv) => inv.id === entry.supplierInvoiceId);
            return <tr key={entry.id}>
              <td className="p-2 text-xs whitespace-nowrap">{entry.createdAt ? new Date(entry.createdAt).toLocaleString("en-PH", { timeZone: "Asia/Manila" }) : "—"}</td>
              <td className="p-2">{entry.supplierName}</td>
              <td className="p-2">
                <span className="inline-block rounded border px-1.5 py-0.5 text-xs font-semibold">{entry.entryType}</span>
                <div className="mt-0.5 font-mono text-xs text-slate-600">{entry.referenceNumber}</div>
              </td>
              <td className="p-2 font-mono text-xs">
                {matchedInv ? (
                  <button type="button" className="font-mono text-blue-700 underline hover:text-blue-900" onClick={() => setReconcileTarget(matchedInv)}>
                    {entry.apVoucherNumber || matchedInv.apvNumber}
                  </button>
                ) : (entry.apVoucherNumber || "—")}
              </td>
              <td className="p-2 text-right tabular-nums">{entry.creditCents > 0 ? money(entry.creditCents) : "—"}</td>
              <td className="p-2 text-right tabular-nums text-emerald-700">{entry.debitCents > 0 ? money(entry.debitCents) : "—"}</td>
              <td className="p-2 text-right font-semibold tabular-nums">{money(entry.balanceAfterCents)}</td>
              <td className="p-2 text-xs text-slate-600">
                <div className="font-mono text-[11px] text-slate-400">Actor: {entry.actorUserId}</div>
                <div>{entry.note || "—"}</div>
              </td>
            </tr>;
          })}
          {filteredLedgerEntries.length === 0 && <tr><td colSpan={8} className="p-6 text-center text-slate-500">No supplier payable subledger entries match this filter.</td></tr>}
          </tbody>
        </table></div>
      </CardBody></Card>

      <Card><CardBody><h2 className="mb-3 text-lg font-semibold">Supplier Payments &amp; Disbursements</h2>
        <div className="overflow-x-auto"><table className="w-full text-left text-sm">
          <thead className="border-b bg-slate-50 text-xs uppercase"><tr><th className="p-2">Payment</th><th className="p-2">Supplier</th><th className="p-2">Method / Reference</th><th className="p-2 text-right">Amount</th><th className="p-2">Status</th><th className="p-2">Actions</th></tr></thead>
          <tbody>{(payments.data ?? []).map((pay) => <tr key={pay.id} className="border-b">
            <td className="p-2 font-mono">{pay.paymentNumber}<div className="text-xs text-slate-500">{pay.invoiceNumber}</div></td><td className="p-2">{pay.supplierName}</td>
            <td className="p-2">{pay.paymentMethod}<div className="text-xs text-slate-500">{pay.referenceNumber || "Reference not entered"}</div></td>
            <td className="p-2 text-right tabular-nums">{money(pay.amountCents)}</td>
            <td className="p-2 text-xs">
              <div className="font-semibold">{pay.status === "PendingApproval" ? "PendingApproval (Unsettled — AP unchanged)" : pay.status}</div>
              {pay.status === "Settled" && <div className="text-slate-500">Actual transfer: {pay.actualDisbursementDate ?? "Legacy / unrecorded"}</div>}
            </td>
            <td className="p-2"><div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => setVoucher(pay)}>Print voucher</Button>
              {pay.status === "PendingApproval" && canApprovePayment &&
                <Button onClick={() => { setApprovalTarget(pay); setSettlementConfirmation(""); setActualDisbursementDate(""); setApprovalReason(""); }}>Confirm actual payment</Button>}
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
          <p className="text-sm">This creates a pending payment request only. It does not reduce Accounts Payable until an authorized Admin or Manager confirms the money was actually paid or transferred.</p>
          <div><label className={labelClass}>Amount (PHP)</label><input type="number" min="0.01" step="0.01" className={inputClass} value={paymentAmount} onChange={(e) => setPaymentAmount(e.target.value)} /></div>
          <div><label className={labelClass}>Method</label><select className={inputClass} value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)}><option>BankTransfer</option><option>Check</option><option>Cash</option></select></div>
          <div><label className={labelClass}>Actual payment reference</label><input className={inputClass} value={paymentReference} onChange={(e) => setPaymentReference(e.target.value)} /></div>
          {paymentMethod !== "Cash" && <div><label className={labelClass}>Bank Name (if applicable; not an account number)</label><input className={inputClass} value={paymentBank} onChange={(e) => setPaymentBank(e.target.value)} /></div>}
          {paymentMethod === "Check" && <div><label className={labelClass}>Check number</label><input className={inputClass} value={paymentCheck} onChange={(e) => setPaymentCheck(e.target.value)} /></div>}
          <div className="flex justify-end gap-2"><Button variant="secondary" onClick={() => setPaymentTarget(null)}>Cancel</Button><Button disabled={busy} onClick={() => void submitPayment()}>Submit for Approval</Button></div>
        </div>
      </Dialog>

      <Dialog open={Boolean(approvalTarget)} onClose={() => { if (!busy) setApprovalTarget(null); }} title="Confirm Actual Supplier Payment">
        <div className="space-y-3">
          <p className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-950">Confirm only after the money has actually been paid or transferred. Salryn records this affirmation; it does not verify bank or payment-provider clearing.</p>
          {approvalTarget && <dl className="grid grid-cols-1 gap-2 rounded-md border border-slate-200 p-3 text-sm sm:grid-cols-2">
            <div><dt className="text-xs text-slate-500">Supplier</dt><dd className="font-medium">{approvalTarget.supplierName}</dd></div>
            <div><dt className="text-xs text-slate-500">Invoice</dt><dd className="font-mono">{approvalTarget.invoiceNumber}</dd></div>
            <div><dt className="text-xs text-slate-500">Payment / voucher</dt><dd className="font-mono">{approvalTarget.paymentNumber}</dd></div>
            <div><dt className="text-xs text-slate-500">Amount</dt><dd className="font-semibold">{money(approvalTarget.amountCents)}</dd></div>
            <div><dt className="text-xs text-slate-500">Method / source</dt><dd>{approvalTarget.paymentMethod} / {approvalTarget.disbursementSource}</dd></div>
            <div><dt className="text-xs text-slate-500">Reference</dt><dd className="font-mono">{approvalTarget.referenceNumber}</dd></div>
            <div><dt className="text-xs text-slate-500">Recorded / requested date (not transfer proof)</dt><dd>{approvalTarget.paymentDate}</dd></div>
            <div><dt className="text-xs text-slate-500">Current status</dt><dd>{approvalTarget.status}</dd></div>
          </dl>}
          <div><label htmlFor="ap-actual-transfer-date" className={labelClass}>Actual funds-transfer date (Asia/Manila) — required</label>
            <input id="ap-actual-transfer-date" aria-label="Actual funds-transfer date" type="date" required max={philippineBusinessToday()} className={inputClass} value={actualDisbursementDate} onChange={(e) => setActualDisbursementDate(e.target.value)} />
            <p className="text-xs text-slate-500 mt-1">Enter the date funds actually moved. This is your declaration, not bank-clearing verification. Past dates are allowed; future dates are not.</p>
          </div>
          <div><label className={labelClass}>Type YES to confirm actual payment</label><input autoComplete="off" className={inputClass} value={settlementConfirmation} onChange={(e) => setSettlementConfirmation(e.target.value)} /></div>
          <div><label className={labelClass}>Settlement remarks (optional)</label><textarea rows={2} className={inputClass} value={approvalReason} onChange={(e) => setApprovalReason(e.target.value)} /></div>
          <div className="flex justify-end gap-2"><Button variant="secondary" disabled={busy} onClick={() => setApprovalTarget(null)}>Cancel</Button>
            <Button disabled={busy || settlementConfirmation.trim() !== "YES" || !isValidActualFundsTransferDate(actualDisbursementDate)} onClick={async () => { if (!approvalTarget || !isValidActualFundsTransferDate(actualDisbursementDate)) return; setBusy(true); try { await approveSupplierPayment(approvalTarget.id, settlementConfirmation, actualDisbursementDate, approvalReason.trim() || undefined); refresh(); setApprovalTarget(null); notify.success("Actual supplier payment confirmed and Accounts Payable settled."); } catch (error) { notify.error(error instanceof Error ? error.message : "Settlement failed."); } finally { setBusy(false); } }}>{busy ? "Settling..." : "Confirm Actual Payment & Settle"}</Button>
          </div>
        </div>
      </Dialog>
      <PrintableChequeDisbursementModal open={Boolean(voucher)} onClose={() => setVoucher(null)} payment={voucher} storeSettings={storeSettings.data} />

      <Dialog open={Boolean(reconcileTarget)} onClose={() => setReconcileTarget(null)} title={reconcileTarget ? `Invoice Reconciliation: ${reconcileTarget.apvNumber}` : "Invoice Reconciliation"}>
        {reconcileTarget && (() => {
          const isPostedLiability = ["PostedOpen", "PartiallyPaid", "Paid"].includes(reconcileTarget.status);
          const postedCredits = invoiceReconcileEntries.reduce((sum, e) => sum + e.creditCents, 0);
          const appliedDebits = invoiceReconcileEntries.reduce((sum, e) => sum + e.debitCents, 0);
          const ledgerBalance = postedCredits - appliedDebits;
          const pendingForInvoice = invoiceReconcilePayments
            .filter((p) => p.status === "PendingApproval")
            .reduce((sum, p) => sum + p.amountCents, 0);
          return (
            <div className="space-y-4 text-sm">
              {!isPostedLiability && (
                <p className="rounded-md border border-amber-300 bg-amber-50 p-3 text-xs font-medium text-amber-950">
                  Unposted / non-payable status ({reconcileTarget.status} · {reconcileTarget.matchStatus}) — PHP 0.00 is posted as settled AP liability in the subledger until variance authorization or 3-way match posting completes.
                </p>
              )}
              <dl className="grid grid-cols-1 gap-2 rounded-md border border-slate-200 p-3 text-xs sm:grid-cols-3">
                <div><dt className="text-slate-500">Supplier</dt><dd className="font-semibold">{reconcileTarget.supplierName}</dd></div>
                <div><dt className="text-slate-500">APV / Supplier Inv #</dt><dd className="font-mono">{reconcileTarget.apvNumber} / {reconcileTarget.invoiceNumber}</dd></div>
                <div><dt className="text-slate-500">Match / Posting</dt><dd className="font-semibold">{reconcileTarget.matchStatus} / {reconcileTarget.status}</dd></div>
                <div><dt className="text-slate-500">Purchase Order Lineage</dt><dd className="font-mono">{reconcileTarget.poNumber || (reconcileTarget.purchaseOrderId ? reconcileTarget.purchaseOrderId : "Missing / Unlinked PO")}</dd></div>
                <div><dt className="text-slate-500">Receiving Report Lineage</dt><dd className="font-mono">{reconcileTarget.rrNumber || (reconcileTarget.receivingReportId ? reconcileTarget.receivingReportId : "Missing / Unlinked RR")}</dd></div>
                <div><dt className="text-slate-500">Invoice / Due Date</dt><dd>{reconcileTarget.invoiceDate} / {reconcileTarget.dueDate}</dd></div>
                <div><dt className="text-slate-500">Recorded By User</dt><dd className="font-mono">{reconcileTarget.recordedByUserId || "—"}</dd></div>
                <div><dt className="text-slate-500">Posted By / At</dt><dd className="font-mono">{reconcileTarget.postedByUserId ? `${reconcileTarget.postedByUserId} (${reconcileTarget.postedAt ?? ""})` : "Not yet posted"}</dd></div>
                <div><dt className="text-slate-500">Variance Review</dt><dd>{reconcileTarget.varianceApprovalReason || reconcileTarget.varianceNotes || "None"}</dd></div>
              </dl>

              <div className="grid grid-cols-2 gap-2 rounded-md border border-slate-200 bg-slate-50 p-3 text-xs sm:grid-cols-6">
                <div><p className="text-slate-500">Invoice Total</p><p className="font-semibold tabular-nums">{money(reconcileTarget.totalCents)}</p></div>
                <div><p className="text-slate-500">Posted AP Credit</p><p className="font-semibold tabular-nums">{money(postedCredits)}</p></div>
                <div><p className="text-slate-500">Return Credits</p><p className="font-semibold tabular-nums text-emerald-700">{money(reconcileTarget.creditMemoAppliedCents)}</p></div>
                <div><p className="text-slate-500">Settled Paid</p><p className="font-semibold tabular-nums text-emerald-700">{money(reconcileTarget.amountPaidCents)}</p></div>
                <div><p className="text-slate-500">Open Balance (Subledger)</p><p className="font-bold tabular-nums">{money(reconcileTarget.balanceDueCents)} <span className="text-[11px] font-normal text-slate-500">(Ledger {money(ledgerBalance)})</span></p></div>
                <div><p className="text-slate-500">Pending (Unsettled)</p><p className="font-semibold tabular-nums text-amber-700">{money(pendingForInvoice)}</p></div>
              </div>

              <div>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-600">3-Way Match Line Breakdown</h3>
                <div className="overflow-x-auto"><table className="w-full text-left text-xs">
                  <thead className="border-b bg-slate-50 uppercase text-slate-600"><tr>
                    <th className="p-2">Item</th><th className="p-2 text-right">PO Ord</th><th className="p-2 text-right">RR Acc</th><th className="p-2 text-right">Invoiced</th>
                    <th className="p-2 text-right">PO Cost</th><th className="p-2 text-right">Inv Cost</th><th className="p-2 text-right">Total</th><th className="p-2">Match</th>
                  </tr></thead>
                  <tbody className="divide-y">{reconcileTarget.items.map((line) => (
                    <tr key={line.id}>
                      <td className="p-2">{line.productName}</td>
                      <td className="p-2 text-right tabular-nums">{(line.poOrderedQuantityX1000 / 1000).toFixed(3)}</td>
                      <td className="p-2 text-right tabular-nums">{(line.rrDeliveredQuantityX1000 / 1000).toFixed(3)}</td>
                      <td className="p-2 text-right tabular-nums">{(line.invoicedQuantityX1000 / 1000).toFixed(3)}</td>
                      <td className="p-2 text-right tabular-nums">{money(line.poUnitCostCents)}</td>
                      <td className="p-2 text-right tabular-nums">{money(line.unitCostCents)}</td>
                      <td className="p-2 text-right tabular-nums font-semibold">{money(line.totalCents)}</td>
                      <td className="p-2">{line.matchStatus}</td>
                    </tr>
                  ))}
                  {reconcileTarget.items.length === 0 && <tr><td colSpan={8} className="p-4 text-center text-slate-500">No invoice line items loaded.</td></tr>}
                  </tbody>
                </table></div>
              </div>

              <div>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-600">Subledger Journal Entries (Chronological)</h3>
                <div className="overflow-x-auto"><table className="w-full text-left text-xs">
                  <thead className="border-b bg-slate-50 uppercase text-slate-600"><tr>
                    <th className="p-2">Timestamp</th><th className="p-2">Type</th><th className="p-2">Reference</th>
                    <th className="p-2 text-right">Credit (+AP)</th><th className="p-2 text-right">Debit (−AP)</th><th className="p-2 text-right">Supplier Bal After</th><th className="p-2">Note / Actor</th>
                  </tr></thead>
                  <tbody className="divide-y">{invoiceReconcileEntries.map((entry) => (
                    <tr key={entry.id}>
                      <td className="p-2 whitespace-nowrap">{entry.createdAt ? new Date(entry.createdAt).toLocaleString("en-PH", { timeZone: "Asia/Manila" }) : "—"}</td>
                      <td className="p-2 font-semibold">{entry.entryType}</td>
                      <td className="p-2 font-mono">{entry.referenceNumber}</td>
                      <td className="p-2 text-right tabular-nums">{entry.creditCents > 0 ? money(entry.creditCents) : "—"}</td>
                      <td className="p-2 text-right tabular-nums text-emerald-700">{entry.debitCents > 0 ? money(entry.debitCents) : "—"}</td>
                      <td className="p-2 text-right tabular-nums font-semibold">{money(entry.balanceAfterCents)}</td>
                      <td className="p-2">{entry.note || "—"} <span className="font-mono text-[10px] text-slate-400">({entry.actorUserId})</span></td>
                    </tr>
                  ))}
                  {invoiceReconcileEntries.length === 0 && <tr><td colSpan={7} className="p-4 text-center text-slate-500">No posted subledger entries recorded for this invoice.</td></tr>}
                  </tbody>
                </table></div>
              </div>

              {invoiceReconcilePayments.length > 0 && (
                <div>
                  <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-600">Linked Disbursement Requests &amp; Settlements</h3>
                  <div className="overflow-x-auto"><table className="w-full text-left text-xs">
                    <thead className="border-b bg-slate-50 uppercase text-slate-600"><tr>
                      <th className="p-2">Payment #</th><th className="p-2">Method / Ref</th><th className="p-2 text-right">Amount</th><th className="p-2">Status / Actual Date</th>
                    </tr></thead>
                    <tbody className="divide-y">{invoiceReconcilePayments.map((pay) => (
                      <tr key={pay.id}>
                        <td className="p-2 font-mono">{pay.paymentNumber}</td>
                        <td className="p-2">{pay.paymentMethod} · <span className="font-mono">{pay.referenceNumber || "—"}</span></td>
                        <td className="p-2 text-right tabular-nums">{money(pay.amountCents)}</td>
                        <td className="p-2">{pay.status === "PendingApproval" ? "PendingApproval (Not deducted from AP)" : `Settled (${pay.actualDisbursementDate ?? "Legacy / unrecorded"})`}</td>
                      </tr>
                    ))}</tbody>
                  </table></div>
                </div>
              )}

              <div className="flex justify-end"><Button variant="secondary" onClick={() => setReconcileTarget(null)}>Close</Button></div>
            </div>
          );
        })()}
      </Dialog>
    </div>
  );
}
