import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchLpiFoundation,
  fetchLpiOverview,
  fetchSuppliers,
  createSupplier,
  updateSupplier,
  fetchRequisitions,
  createRequisition,
  submitRequisition,
  approveRequisition,
  rejectRequisition,
  convertRequisitionToPo,
  fetchQuotations,
  selectWinningQuotation,
  fetchPurchaseOrders,
  approvePurchaseOrder,
  markPurchaseOrderSent,
  cancelPurchaseOrder,
  fetchReceivingReports,
  createReceivingReport,
  fetchStockCards,
  createStockAdjustment,
  fetchSupplierReturns,
  createSupplierReturn,
  fetchSupplierInvoices,
  createSupplierInvoice,
  approveInvoiceVariance,
  recordSupplierPayment,
  type LpiSupplier,
  type PurchaseRequisition,
  type PurchaseOrder,
  type SupplierInvoice,
} from "../api/procureToPay";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  Dialog,
  MetricCard,
  Select,
  StatusPill,
  TextInput,
  useNotifications,
} from "../components/ui";
import { useAppSession } from "../hooks/useAppSession";
import { formatDateTime, formatMoney } from "../utils/format";

const money = (cents: number) => formatMoney(cents / 100);

export function ProcureToPayPage() {
  const queryClient = useQueryClient();
  const { notify } = useNotifications();
  const session = useAppSession();
  const currentUserId = session.data?.user?.userId ?? "";

  const [activeTab, setActiveTab] = useState<
    "overview" | "suppliers" | "pr" | "quotations" | "po" | "rr" | "inventory" | "ap"
  >(() => {
    if (typeof window !== "undefined") {
      const t = new URLSearchParams(window.location.search).get("tab");
      if (t && ["overview", "suppliers", "pr", "quotations", "po", "rr", "inventory", "ap"].includes(t)) {
        return t as any;
      }
    }
    return "overview";
  });

  // Queries
  const foundation = useQuery({ queryKey: ["lpi-foundation"], queryFn: () => fetchLpiFoundation(true) });
  const overview = useQuery({ queryKey: ["lpi-overview"], queryFn: fetchLpiOverview });
  const suppliers = useQuery({ queryKey: ["lpi-suppliers"], queryFn: () => fetchSuppliers("", true) });
  const requisitions = useQuery({ queryKey: ["lpi-requisitions"], queryFn: () => fetchRequisitions() });
  const purchaseOrders = useQuery({ queryKey: ["lpi-pos"], queryFn: () => fetchPurchaseOrders() });
  const receivingReports = useQuery({ queryKey: ["lpi-rrs"], queryFn: () => fetchReceivingReports() });
  const stockCards = useQuery({ queryKey: ["lpi-stock-cards"], queryFn: fetchStockCards });
  const supplierReturns = useQuery({ queryKey: ["lpi-returns"], queryFn: () => fetchSupplierReturns() });
  const invoices = useQuery({ queryKey: ["lpi-invoices"], queryFn: () => fetchSupplierInvoices() });

  // Dialog States
  const [supplierModalOpen, setSupplierModalOpen] = useState(false);
  const [editingSupplier, setEditingSupplier] = useState<LpiSupplier | null>(null);
  const [supplierForm, setSupplierForm] = useState({
    code: "",
    name: "",
    contactPerson: "",
    contactNumber: "",
    email: "",
    address: "",
    tin: "",
    paymentTermsDays: "30",
    leadTimeDays: "3",
    notes: "",
    isActive: true,
  });

  const [prModalOpen, setPrModalOpen] = useState(false);
  const [prForm, setPrForm] = useState({
    department: "Store Operations",
    purpose: "Restock store inventory",
    requiredDate: "",
    notes: "",
    items: [{ productId: "", uomId: "", quantity: "1", estimatedCost: "0", preferredSupplierId: "" }],
  });

  const [selectedPrForQuote, setSelectedPrForQuote] = useState<string>("");

  const [splitPoModalOpen, setSplitPoModalOpen] = useState(false);
  const [splitPr, setSplitPr] = useState<PurchaseRequisition | null>(null);
  const [splitAllocations, setSplitAllocations] = useState<Array<{ supplierId: string; prItemIds: string[] }>>([]);

  const [rrModalOpen, setRrModalOpen] = useState(false);
  const [selectedPoForRr, setSelectedPoForRr] = useState<PurchaseOrder | null>(null);
  const [rrForm, setRrForm] = useState({
    deliveryReceiptNumber: "",
    carrier: "Direct Delivery",
    driverName: "",
    isOverdeliveryApproved: false,
    notes: "",
    items: [] as Array<{
      poItemId: string;
      productId: string;
      productName: string;
      uomId: string;
      orderedQty: number;
      deliveredQty: string;
      acceptedQty: string;
      rejectedQty: string;
      rejectionReason: string;
      targetLocationId: string;
      quarantineLocationId: string;
    }>,
  });

  const [stockAdjModalOpen, setStockAdjModalOpen] = useState(false);
  const [stockAdjForm, setStockAdjForm] = useState({
    productId: "",
    locationId: "",
    systemQty: "0",
    actualQty: "0",
    unitCost: "0",
    reason: "Monthly Physical Inventory Cycle Count",
  });

  const [returnModalOpen, setReturnModalOpen] = useState(false);
  const [returnForm, setReturnForm] = useState({
    supplierId: "",
    purchaseOrderId: "",
    receivingReportId: "",
    reason: "Defective goods returned post-acceptance",
    items: [{ productId: "", uomId: "", quantity: "1", unitCost: "0", sourceLocationId: "" }],
  });

  const [invoiceModalOpen, setInvoiceModalOpen] = useState(false);
  const [invoiceForm, setInvoiceForm] = useState({
    supplierId: "",
    invoiceNumber: "",
    invoiceDate: new Date().toISOString().slice(0, 10),
    dueDate: new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10),
    purchaseOrderId: "",
    receivingReportId: "",
    notes: "",
    items: [] as Array<{
      productId: string;
      productName: string;
      invoicedQty: string;
      unitCost: string;
      poItemId?: string;
      rrItemId?: string;
    }>,
  });

  const [paymentModalOpen, setPaymentModalOpen] = useState(false);
  const [selectedInvoiceForPayment, setSelectedInvoiceForPayment] = useState<SupplierInvoice | null>(null);
  const [paymentForm, setPaymentForm] = useState({
    amount: "0",
    paymentMethod: "BankTransfer",
    referenceNumber: "",
    bankAccount: "BDO-0987-6543-21",
    checkNumber: "",
    notes: "Supplier accounts payable settlement",
  });

  const [rejectReasonModalOpen, setRejectReasonModalOpen] = useState(false);
  const [rejectTargetPrId, setRejectTargetPrId] = useState("");
  const [rejectReasonText, setRejectReasonText] = useState("");

  const [varianceModalOpen, setVarianceModalOpen] = useState(false);
  const [varianceTargetInvoice, setVarianceTargetInvoice] = useState<SupplierInvoice | null>(null);
  const [varianceApprovalNotes, setVarianceApprovalNotes] = useState("");

  // Helpers
  const mainStoreLocation = foundation.data?.locations.find((l) => l.code === "MAIN-STORE");
  const quarantineLocation = foundation.data?.locations.find((l) => l.code === "QUARANTINE-HOLD");

  // Invalidate all
  const refreshAll = () => {
    void queryClient.invalidateQueries({ queryKey: ["lpi-foundation"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-overview"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-suppliers"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-requisitions"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-pos"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-rrs"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-stock-cards"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-returns"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-invoices"] });
    void queryClient.invalidateQueries({ queryKey: ["lpi-ap-aging"] });
  };

  return (
    <div className="space-y-6">
      {/* Header & Navigation */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between border-b pb-4 border-slate-200">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Logistics, Procurement & Inventory</h1>
          <p className="text-sm text-slate-500">
            End-to-end Procure-to-Pay vertical: Purchase Requisitions, Quotations, Purchase Orders, Receiving Reports,
            3-Way Match, and AP Aging.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={refreshAll}>
            Refresh Data
          </Button>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex flex-wrap gap-2 border-b border-slate-200 pb-2">
        {(
          [
            { id: "overview", label: "📊 Overview" },
            { id: "suppliers", label: "🏢 Suppliers" },
            { id: "pr", label: "📝 Requisitions (PR)" },
            { id: "quotations", label: "📋 Quotations" },
            { id: "po", label: "📦 Purchase Orders" },
            { id: "rr", label: "🚚 Receiving (RR)" },
            { id: "inventory", label: "📈 Stock & Returns" },
            { id: "ap", label: "💳 Accounts Payable" },
          ] as const
        ).map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => setActiveTab(tab.id)}
            className={`px-3 py-1.5 text-sm font-medium rounded-md transition-colors ${
              activeTab === tab.id
                ? "bg-slate-900 text-white shadow-sm"
                : "text-slate-600 hover:bg-slate-100 hover:text-slate-900"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* TAB 1: OVERVIEW */}
      {activeTab === "overview" && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <MetricCard
              title="Active Suppliers"
              value={(overview.data?.totalActiveSuppliers ?? 0).toString()}
              tone="info"
            />
            <MetricCard
              title="Pending Requisitions"
              value={(overview.data?.pendingPurchaseRequisitions ?? 0).toString()}
              tone={overview.data?.pendingPurchaseRequisitions ? "warning" : "neutral"}
            />
            <MetricCard
              title="Open Purchase Orders"
              value={(overview.data?.openPurchaseOrders ?? 0).toString()}
              tone="info"
            />
            <MetricCard
              title="Unmatched Invoices"
              value={(overview.data?.unmatchedInvoices ?? 0).toString()}
              tone={overview.data?.unmatchedInvoices ? "danger" : "success"}
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <MetricCard
              title="Total Accounts Payable"
              value={money(overview.data?.totalAccountsPayableCents ?? 0)}
              tone="neutral"
            />
            <MetricCard
              title="Overdue Payables"
              value={money(overview.data?.overduePayablesCents ?? 0)}
              tone={overview.data?.overduePayablesCents ? "danger" : "success"}
            />
            <MetricCard
              title="Total Stock Valuation"
              value={money(overview.data?.totalInventoryValuationCents ?? 0)}
              tone="success"
            />
          </div>

          {/* Aging Schedule Table */}
          <Card>
            <CardHeader>
              <CardTitle>Accounts Payable Aging Schedule</CardTitle>
            </CardHeader>
            <CardBody>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">Supplier</th>
                      <th className="p-3 text-right">Current</th>
                      <th className="p-3 text-right">1-30 Days</th>
                      <th className="p-3 text-right">31-60 Days</th>
                      <th className="p-3 text-right">61-90 Days</th>
                      <th className="p-3 text-right">&gt; 90 Days</th>
                      <th className="p-3 text-right font-bold">Total Due</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(overview.data?.agingSummary ?? []).length === 0 ? (
                      <tr>
                        <td colSpan={7} className="p-4 text-center text-slate-500">
                          No outstanding accounts payable.
                        </td>
                      </tr>
                    ) : (
                      overview.data?.agingSummary.map((age) => (
                        <tr key={age.supplierId} className="hover:bg-slate-50">
                          <td className="p-3 font-medium text-slate-900">{age.supplierName}</td>
                          <td className="p-3 text-right">{money(age.currentCents)}</td>
                          <td className="p-3 text-right">{money(age.days1To30Cents)}</td>
                          <td className="p-3 text-right">{money(age.days31To60Cents)}</td>
                          <td className="p-3 text-right">{money(age.days61To90Cents)}</td>
                          <td className="p-3 text-right text-red-600 font-semibold">{money(age.over90DaysCents)}</td>
                          <td className="p-3 text-right font-bold text-slate-900">{money(age.totalOutstandingCents)}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>
        </div>
      )}

      {/* TAB 2: SUPPLIERS */}
      {activeTab === "suppliers" && (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Supplier Directory</h2>
            <Button
              onClick={() => {
                setEditingSupplier(null);
                setSupplierForm({
                  code: `SUP-${Date.now().toString().slice(-4)}`,
                  name: "",
                  contactPerson: "",
                  contactNumber: "",
                  email: "",
                  address: "",
                  tin: "",
                  paymentTermsDays: "30",
                  leadTimeDays: "3",
                  notes: "",
                  isActive: true,
                });
                setSupplierModalOpen(true);
              }}
            >
              + Add Supplier
            </Button>
          </div>

          <Card>
            <CardBody className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">Code</th>
                      <th className="p-3">Name</th>
                      <th className="p-3">Contact</th>
                      <th className="p-3">TIN</th>
                      <th className="p-3">Terms</th>
                      <th className="p-3">Lead Time</th>
                      <th className="p-3">Status</th>
                      <th className="p-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(suppliers.data ?? []).map((sup) => (
                      <tr key={sup.id} className="hover:bg-slate-50">
                        <td className="p-3 font-mono font-medium text-slate-900">{sup.code}</td>
                        <td className="p-3 font-medium text-slate-900">{sup.name}</td>
                        <td className="p-3 text-slate-600">
                          {sup.contactPerson}
                          {sup.contactNumber && <div className="text-xs text-slate-400">{sup.contactNumber}</div>}
                        </td>
                        <td className="p-3 font-mono text-xs">{sup.tin || "—"}</td>
                        <td className="p-3">{sup.paymentTermsDays} days</td>
                        <td className="p-3">{sup.leadTimeDays} days</td>
                        <td className="p-3">
                          <StatusPill tone={sup.isActive ? "success" : "neutral"}>
                            {sup.isActive ? "Active" : "Inactive"}
                          </StatusPill>
                        </td>
                        <td className="p-3 text-right">
                          <Button
                            variant="secondary"
                            onClick={() => {
                              setEditingSupplier(sup);
                              setSupplierForm({
                                code: sup.code,
                                name: sup.name,
                                contactPerson: sup.contactPerson || "",
                                contactNumber: sup.contactNumber || "",
                                email: sup.email || "",
                                address: sup.address || "",
                                tin: sup.tin || "",
                                paymentTermsDays: sup.paymentTermsDays.toString(),
                                leadTimeDays: sup.leadTimeDays.toString(),
                                notes: sup.notes || "",
                                isActive: sup.isActive,
                              });
                              setSupplierModalOpen(true);
                            }}
                          >
                            Edit
                          </Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>
        </div>
      )}

      {/* TAB 3: REQUISITIONS (PR) */}
      {activeTab === "pr" && (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Purchase Requisitions (PR)</h2>
            <Button
              onClick={() => {
                const prod = foundation.data?.products[0];
                const uom = foundation.data?.unitsOfMeasure[0];
                setPrForm({
                  department: "Store Operations",
                  purpose: "Restock fast-moving grocery inventory",
                  requiredDate: new Date(Date.now() + 5 * 86400000).toISOString().slice(0, 10),
                  notes: "Urgent store replenishment",
                  items: [
                    {
                      productId: prod?.id ?? "",
                      uomId: uom?.id ?? "",
                      quantity: "100",
                      estimatedCost: prod ? (prod.costPriceCents / 100).toFixed(2) : "10",
                      preferredSupplierId: "",
                    },
                  ],
                });
                setPrModalOpen(true);
              }}
            >
              + Create Requisition
            </Button>
          </div>

          <Card>
            <CardBody className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">PR #</th>
                      <th className="p-3">Date</th>
                      <th className="p-3">Department</th>
                      <th className="p-3">Requested By</th>
                      <th className="p-3">Estimated Total</th>
                      <th className="p-3">Status</th>
                      <th className="p-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(requisitions.data ?? []).map((pr) => (
                      <tr key={pr.id} className="hover:bg-slate-50">
                        <td className="p-3 font-mono font-bold text-slate-900">{pr.prNumber}</td>
                        <td className="p-3 text-xs">{formatDateTime(pr.requisitionDate)}</td>
                        <td className="p-3 font-medium">{pr.department}</td>
                        <td className="p-3">{pr.requestedByUserName || "User"}</td>
                        <td className="p-3 font-semibold">{money(pr.totalEstimatedCostCents)}</td>
                        <td className="p-3">
                          <StatusPill
                            tone={
                              pr.status === "Approved" || pr.status === "ConvertedToPo"
                                ? "success"
                                : pr.status === "Submitted"
                                ? "info"
                                : pr.status === "Rejected"
                                ? "danger"
                                : "neutral"
                            }
                          >
                            {pr.status}
                          </StatusPill>
                        </td>
                        <td className="p-3 text-right space-x-2">
                          {pr.status === "Draft" && (
                            <Button
                              variant="secondary"
                              onClick={async () => {
                                try {
                                  await submitRequisition(pr.id);
                                  notify.success(`PR ${pr.prNumber} submitted for approval.`);
                                  refreshAll();
                                } catch (e) {
                                  notify.error((e as Error).message);
                                }
                              }}
                            >
                              Submit
                            </Button>
                          )}
                          {pr.status === "Submitted" && (
                            <>
                              <Button
                                variant="primary"
                                onClick={async () => {
                                  if (pr.requestedByUserId === currentUserId) {
                                    notify.warning("Self-approval denial: A requester cannot approve their own requisition.");
                                    return;
                                  }
                                  try {
                                    await approveRequisition(pr.id);
                                    notify.success(`PR ${pr.prNumber} approved!`);
                                    refreshAll();
                                  } catch (e) {
                                    notify.error((e as Error).message);
                                  }
                                }}
                              >
                                Approve
                              </Button>
                              <Button
                                variant="danger"
                                onClick={() => {
                                  setRejectTargetPrId(pr.id);
                                  setRejectReasonText("");
                                  setRejectReasonModalOpen(true);
                                }}
                              >
                                Reject
                              </Button>
                            </>
                          )}
                          {pr.status === "Approved" && (
                            <Button
                              variant="secondary"
                              onClick={() => {
                                setSplitPr(pr);
                                const defaultSupplier = foundation.data?.suppliers[0]?.id ?? "";
                                setSplitAllocations([
                                  {
                                    supplierId: defaultSupplier,
                                    prItemIds: pr.items.map((i) => i.id),
                                  },
                                ]);
                                setSplitPoModalOpen(true);
                              }}
                            >
                              Generate POs
                            </Button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>
        </div>
      )}

      {/* TAB 4: QUOTATIONS */}
      {activeTab === "quotations" && (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Supplier Quotations & Quote Comparison</h2>
            <div className="flex items-center gap-2">
              <Select
                value={selectedPrForQuote}
                onChange={(e) => setSelectedPrForQuote(e.target.value)}
              >
                <option value="">Select Requisition to view quotes...</option>
                {(requisitions.data ?? []).map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.prNumber} ({r.department} - {money(r.totalEstimatedCostCents)})
                  </option>
                ))}
              </Select>
            </div>
          </div>

          {!selectedPrForQuote ? (
            <Alert tone="info">Please select a Purchase Requisition above to view or compare supplier quotations.</Alert>
          ) : (
            <QuotationListView requisitionId={selectedPrForQuote} onRefresh={refreshAll} />
          )}
        </div>
      )}

      {/* TAB 5: PURCHASE ORDERS (PO) */}
      {activeTab === "po" && (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Purchase Orders (PO)</h2>
          </div>

          <Card>
            <CardBody className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">PO #</th>
                      <th className="p-3">Date</th>
                      <th className="p-3">Supplier</th>
                      <th className="p-3">Expected</th>
                      <th className="p-3">Total Cost</th>
                      <th className="p-3">Status</th>
                      <th className="p-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(purchaseOrders.data ?? []).map((po) => (
                      <tr key={po.id} className="hover:bg-slate-50">
                        <td className="p-3 font-mono font-bold text-slate-900">{po.poNumber}</td>
                        <td className="p-3 text-xs">{formatDateTime(po.poDate)}</td>
                        <td className="p-3 font-medium">{po.supplierName}</td>
                        <td className="p-3 text-xs">{po.expectedDeliveryDate || "—"}</td>
                        <td className="p-3 font-semibold">{money(po.totalCents)}</td>
                        <td className="p-3">
                          <StatusPill
                            tone={
                              po.status === "FullyReceived"
                                ? "success"
                                : po.status === "Approved" || po.status === "Sent"
                                ? "info"
                                : po.status === "PartiallyReceived"
                                ? "warning"
                                : po.status === "Cancelled"
                                ? "danger"
                                : "neutral"
                            }
                          >
                            {po.status}
                          </StatusPill>
                        </td>
                        <td className="p-3 text-right space-x-2">
                          {po.status === "Draft" && (
                            <Button
                              variant="primary"
                              onClick={async () => {
                                try {
                                  await approvePurchaseOrder(po.id);
                                  notify.success(`PO ${po.poNumber} approved.`);
                                  refreshAll();
                                } catch (e) {
                                  notify.error((e as Error).message);
                                }
                              }}
                            >
                              Approve
                            </Button>
                          )}
                          {po.status === "Approved" && (
                            <Button
                              variant="secondary"
                              onClick={async () => {
                                try {
                                  await markPurchaseOrderSent(po.id);
                                  notify.success(`PO ${po.poNumber} marked as sent to supplier.`);
                                  refreshAll();
                                } catch (e) {
                                  notify.error((e as Error).message);
                                }
                              }}
                            >
                              Mark Sent
                            </Button>
                          )}
                          {(po.status === "Draft" || po.status === "Approved" || po.status === "Sent") && (
                            <Button
                              variant="danger"
                              onClick={async () => {
                                const reason = window.prompt("Reason for cancelling purchase order:");
                                if (!reason) return;
                                try {
                                  await cancelPurchaseOrder(po.id, reason);
                                  notify.success(`PO ${po.poNumber} cancelled.`);
                                  refreshAll();
                                } catch (e) {
                                  notify.error((e as Error).message);
                                }
                              }}
                            >
                              Cancel
                            </Button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>
        </div>
      )}

      {/* TAB 6: RECEIVING REPORTS (RR) */}
      {activeTab === "rr" && (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Goods Receiving Reports (RR)</h2>
            <div className="flex items-center gap-2">
              <Select
                onChange={(e) => {
                  const po = purchaseOrders.data?.find((p) => p.id === e.target.value);
                  if (po) {
                    setSelectedPoForRr(po);
                    setRrForm({
                      deliveryReceiptNumber: `DR-${Date.now().toString().slice(-4)}`,
                      carrier: "Supplier Trucking",
                      driverName: "Carrier Dispatch Driver",
                      isOverdeliveryApproved: false,
                      notes: "Dock delivery inspection",
                      items: po.items.map((item) => {
                        const remaining = (item.orderedQuantityX1000 - item.receivedQuantityX1000) / 1000;
                        return {
                          poItemId: item.id,
                          productId: item.productId,
                          productName: item.productName,
                          uomId: item.uomId,
                          orderedQty: item.orderedQuantityX1000 / 1000,
                          deliveredQty: remaining > 0 ? remaining.toString() : "0",
                          acceptedQty: remaining > 0 ? remaining.toString() : "0",
                          rejectedQty: "0",
                          rejectionReason: "",
                          targetLocationId: mainStoreLocation?.id ?? "",
                          quarantineLocationId: quarantineLocation?.id ?? "",
                        };
                      }),
                    });
                    setRrModalOpen(true);
                  }
                }}
              >
                <option value="">Receive Goods Against PO...</option>
                {(purchaseOrders.data ?? [])
                  .filter((p) => p.status === "Approved" || p.status === "Sent" || p.status === "PartiallyReceived")
                  .map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.poNumber} ({p.supplierName} - {p.status})
                    </option>
                  ))}
              </Select>
            </div>
          </div>

          <Card>
            <CardBody className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">RR #</th>
                      <th className="p-3">Date</th>
                      <th className="p-3">PO #</th>
                      <th className="p-3">Supplier</th>
                      <th className="p-3">Delivered Qty</th>
                      <th className="p-3">Accepted Qty</th>
                      <th className="p-3">Rejected Qty</th>
                      <th className="p-3">Accepted Cost</th>
                      <th className="p-3">Status</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(receivingReports.data ?? []).map((rr) => (
                      <tr key={rr.id} className="hover:bg-slate-50">
                        <td className="p-3 font-mono font-bold text-slate-900">{rr.rrNumber}</td>
                        <td className="p-3 text-xs">{formatDateTime(rr.receivedDate)}</td>
                        <td className="p-3 font-mono text-xs">{rr.poNumber}</td>
                        <td className="p-3 font-medium">{rr.supplierName}</td>
                        <td className="p-3 font-mono">{rr.totalDeliveredQuantityX1000 / 1000}</td>
                        <td className="p-3 font-mono text-emerald-600 font-semibold">{rr.totalAcceptedQuantityX1000 / 1000}</td>
                        <td className="p-3 font-mono text-amber-600">{rr.totalRejectedQuantityX1000 / 1000}</td>
                        <td className="p-3 font-semibold">{money(rr.totalAcceptedCostCents)}</td>
                        <td className="p-3">
                          <StatusPill tone={rr.isOverdelivery ? "warning" : "success"}>
                            {rr.status} {rr.isOverdelivery ? "· Overdelivered" : ""}
                          </StatusPill>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>
        </div>
      )}

      {/* TAB 7: STOCK & RETURNS */}
      {activeTab === "inventory" && (
        <div className="space-y-6">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Multi-Location Stock Cards & Valuation</h2>
            <div className="flex gap-2">
              <Button
                variant="secondary"
                onClick={() => {
                  const card = stockCards.data?.[0];
                  setStockAdjForm({
                    productId: card?.productId ?? "",
                    locationId: mainStoreLocation?.id ?? "",
                    systemQty: card ? (card.totalQuantityX1000 / 1000).toString() : "0",
                    actualQty: card ? (card.totalQuantityX1000 / 1000).toString() : "0",
                    unitCost: card ? (card.averageUnitCostCents / 100).toFixed(2) : "0",
                    reason: "Monthly Physical Inventory Cycle Count",
                  });
                  setStockAdjModalOpen(true);
                }}
              >
                + Count Adjustment (ADJ)
              </Button>
              <Button
                variant="danger"
                onClick={() => {
                  const sup = foundation.data?.suppliers[0];
                  const prod = foundation.data?.products[0];
                  const uom = foundation.data?.unitsOfMeasure[0];
                  setReturnForm({
                    supplierId: sup?.id ?? "",
                    purchaseOrderId: "",
                    receivingReportId: "",
                    reason: "Post-acceptance latent defect discovered in quarantine",
                    items: [
                      {
                        productId: prod?.id ?? "",
                        uomId: uom?.id ?? "",
                        quantity: "5",
                        unitCost: prod ? (prod.costPriceCents / 100).toFixed(2) : "10",
                        sourceLocationId: quarantineLocation?.id ?? mainStoreLocation?.id ?? "",
                      },
                    ],
                  });
                  setReturnModalOpen(true);
                }}
              >
                + Supplier Return (SRET)
              </Button>
            </div>
          </div>

          <Card>
            <CardHeader>
              <CardTitle>Per-Product Stock Cards & Location Buckets</CardTitle>
            </CardHeader>
            <CardBody className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">Product</th>
                      <th className="p-3">Barcode</th>
                      <th className="p-3 text-right">Main Store</th>
                      <th className="p-3 text-right">Quarantine</th>
                      <th className="p-3 text-right">Damaged</th>
                      <th className="p-3 text-right font-bold">Total Stock</th>
                      <th className="p-3 text-right">Avg Unit Cost</th>
                      <th className="p-3 text-right font-bold">Valuation</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(stockCards.data ?? []).map((card) => {
                      const mainBal = card.locationBalances.find((b) => b.locationCode === "MAIN-STORE")?.quantityX1000 ?? 0;
                      const quarBal = card.locationBalances.find((b) => b.locationCode === "QUARANTINE-HOLD")?.quantityX1000 ?? 0;
                      const damBal = card.locationBalances.find((b) => b.locationCode === "DAMAGED-RETURN")?.quantityX1000 ?? 0;

                      return (
                        <tr key={card.productId} className="hover:bg-slate-50">
                          <td className="p-3 font-medium text-slate-900">{card.productName}</td>
                          <td className="p-3 font-mono text-xs text-slate-500">{card.productBarcode}</td>
                          <td className="p-3 text-right font-mono">{mainBal / 1000}</td>
                          <td className="p-3 text-right font-mono text-amber-600">{quarBal / 1000}</td>
                          <td className="p-3 text-right font-mono text-red-600">{damBal / 1000}</td>
                          <td className="p-3 text-right font-mono font-bold">{card.totalQuantityX1000 / 1000}</td>
                          <td className="p-3 text-right">{money(card.averageUnitCostCents)}</td>
                          <td className="p-3 text-right font-bold text-slate-900">{money(card.totalValuationCents)}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>

          {/* Supplier Returns History */}
          <Card>
            <CardHeader>
              <CardTitle>Supplier Returns History (SRET Debit Memos)</CardTitle>
            </CardHeader>
            <CardBody className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">Return #</th>
                      <th className="p-3">Date</th>
                      <th className="p-3">Supplier</th>
                      <th className="p-3">Reason</th>
                      <th className="p-3 text-right">Debit Amount</th>
                      <th className="p-3">Status</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(supplierReturns.data ?? []).length === 0 ? (
                      <tr>
                        <td colSpan={6} className="p-4 text-center text-slate-500">
                          No supplier returns recorded.
                        </td>
                      </tr>
                    ) : (
                      supplierReturns.data?.map((ret) => (
                        <tr key={ret.id} className="hover:bg-slate-50">
                          <td className="p-3 font-mono font-bold text-slate-900">{ret.returnNumber}</td>
                          <td className="p-3 text-xs">{formatDateTime(ret.returnDate)}</td>
                          <td className="p-3 font-medium">{ret.supplierName}</td>
                          <td className="p-3 text-xs">{ret.reason}</td>
                          <td className="p-3 text-right font-bold text-red-600">{money(ret.totalCostCents)}</td>
                          <td className="p-3">
                            <StatusPill tone="warning">{ret.status}</StatusPill>
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>
        </div>
      )}

      {/* TAB 8: ACCOUNTS PAYABLE (AP) & 3-WAY MATCH */}
      {activeTab === "ap" && (
        <div className="space-y-6">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Supplier Invoices & 3-Way Match</h2>
            <div className="flex items-center gap-2">
              <Button
                onClick={() => {
                  const sup = foundation.data?.suppliers[0];
                  const prod = foundation.data?.products[0];
                  setInvoiceForm({
                    supplierId: sup?.id ?? "",
                    invoiceNumber: `INV-${Date.now().toString().slice(-5)}`,
                    invoiceDate: new Date().toISOString().slice(0, 10),
                    dueDate: new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10),
                    purchaseOrderId: "",
                    receivingReportId: "",
                    notes: "Supplier accounts payable voucher",
                    items: [
                      {
                        productId: prod?.id ?? "",
                        productName: prod?.name ?? "Item",
                        invoicedQty: "10",
                        unitCost: prod ? (prod.costPriceCents / 100).toFixed(2) : "10",
                      },
                    ],
                  });
                  setInvoiceModalOpen(true);
                }}
              >
                + Intake Supplier Invoice
              </Button>
            </div>
          </div>

          <Card>
            <CardBody className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                    <tr>
                      <th className="p-3">APV #</th>
                      <th className="p-3">Invoice #</th>
                      <th className="p-3">Supplier</th>
                      <th className="p-3">Due Date</th>
                      <th className="p-3">Total Cost</th>
                      <th className="p-3">Balance Due</th>
                      <th className="p-3">3-Way Match</th>
                      <th className="p-3">Status</th>
                      <th className="p-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(invoices.data ?? []).map((inv) => (
                      <tr key={inv.id} className="hover:bg-slate-50">
                        <td className="p-3 font-mono font-bold text-slate-900">{inv.apvNumber}</td>
                        <td className="p-3 font-mono">{inv.invoiceNumber}</td>
                        <td className="p-3 font-medium">{inv.supplierName}</td>
                        <td className="p-3 text-xs">{inv.dueDate}</td>
                        <td className="p-3 font-semibold">{money(inv.totalCents)}</td>
                        <td className="p-3 font-bold text-red-600">{money(inv.balanceDueCents)}</td>
                        <td className="p-3">
                          <StatusPill
                            tone={
                              inv.matchStatus === "Matched" || inv.matchStatus === "VarianceApproved"
                                ? "success"
                                : "danger"
                            }
                          >
                            {inv.matchStatus}
                          </StatusPill>
                        </td>
                        <td className="p-3">
                          <StatusPill tone={inv.status === "Paid" ? "success" : "neutral"}>{inv.status}</StatusPill>
                        </td>
                        <td className="p-3 text-right space-x-2">
                          {(inv.matchStatus === "PriceVariance" || inv.matchStatus === "QuantityVariance") && (
                            <Button
                              variant="danger"
                              onClick={() => {
                                setVarianceTargetInvoice(inv);
                                setVarianceApprovalNotes("");
                                setVarianceModalOpen(true);
                              }}
                            >
                              Approve Variance
                            </Button>
                          )}
                          {inv.balanceDueCents > 0 &&
                            (inv.matchStatus === "Matched" || inv.matchStatus === "VarianceApproved") && (
                              <Button
                                variant="primary"
                                onClick={() => {
                                  setSelectedInvoiceForPayment(inv);
                                  setPaymentForm({
                                    amount: (inv.balanceDueCents / 100).toFixed(2),
                                    paymentMethod: "BankTransfer",
                                    referenceNumber: `TXN-${Date.now().toString().slice(-6)}`,
                                    bankAccount: "BDO-0987-6543-21",
                                    checkNumber: "",
                                    notes: `Settlement for Invoice ${inv.invoiceNumber}`,
                                  });
                                  setPaymentModalOpen(true);
                                }}
                              >
                                Record Payment
                              </Button>
                            )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardBody>
          </Card>
        </div>
      )}

      {/* DIALOG: Add / Edit Supplier */}
      <Dialog
        open={supplierModalOpen}
        onClose={() => setSupplierModalOpen(false)}
        title={editingSupplier ? `Edit Supplier: ${editingSupplier.name}` : "Create New Supplier"}
      >
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Code</label>
              <TextInput
                value={supplierForm.code}
                onChange={(e) => setSupplierForm({ ...supplierForm, code: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Supplier Name</label>
              <TextInput
                value={supplierForm.name}
                onChange={(e) => setSupplierForm({ ...supplierForm, name: e.target.value })}
              />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Contact Person</label>
              <TextInput
                value={supplierForm.contactPerson}
                onChange={(e) => setSupplierForm({ ...supplierForm, contactPerson: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Phone</label>
              <TextInput
                value={supplierForm.contactNumber}
                onChange={(e) => setSupplierForm({ ...supplierForm, contactNumber: e.target.value })}
              />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Email</label>
              <TextInput
                value={supplierForm.email}
                onChange={(e) => setSupplierForm({ ...supplierForm, email: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">TIN</label>
              <TextInput
                value={supplierForm.tin}
                onChange={(e) => setSupplierForm({ ...supplierForm, tin: e.target.value })}
              />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Payment Terms (Days)</label>
              <TextInput
                type="number"
                value={supplierForm.paymentTermsDays}
                onChange={(e) => setSupplierForm({ ...supplierForm, paymentTermsDays: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Lead Time (Days)</label>
              <TextInput
                type="number"
                value={supplierForm.leadTimeDays}
                onChange={(e) => setSupplierForm({ ...supplierForm, leadTimeDays: e.target.value })}
              />
            </div>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-700">Address</label>
            <TextInput
              value={supplierForm.address}
              onChange={(e) => setSupplierForm({ ...supplierForm, address: e.target.value })}
            />
          </div>
          <div className="flex justify-end gap-2 pt-4">
            <Button variant="secondary" onClick={() => setSupplierModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                try {
                  if (editingSupplier) {
                    await updateSupplier(editingSupplier.id, {
                      code: supplierForm.code,
                      name: supplierForm.name,
                      contactPerson: supplierForm.contactPerson,
                      contactNumber: supplierForm.contactNumber,
                      email: supplierForm.email,
                      address: supplierForm.address,
                      tin: supplierForm.tin,
                      paymentTermsDays: parseInt(supplierForm.paymentTermsDays, 10),
                      leadTimeDays: parseInt(supplierForm.leadTimeDays, 10),
                      notes: supplierForm.notes,
                      isActive: supplierForm.isActive,
                    });
                    notify.success("Supplier updated successfully.");
                  } else {
                    await createSupplier({
                      code: supplierForm.code,
                      name: supplierForm.name,
                      contactPerson: supplierForm.contactPerson,
                      contactNumber: supplierForm.contactNumber,
                      email: supplierForm.email,
                      address: supplierForm.address,
                      tin: supplierForm.tin,
                      paymentTermsDays: parseInt(supplierForm.paymentTermsDays, 10),
                      leadTimeDays: parseInt(supplierForm.leadTimeDays, 10),
                      notes: supplierForm.notes,
                    });
                    notify.success("Supplier registered successfully.");
                  }
                  setSupplierModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Save Supplier
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Create Purchase Requisition */}
      <Dialog
        open={prModalOpen}
        onClose={() => setPrModalOpen(false)}
        title="Create Purchase Requisition (PR)"
      >
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Department</label>
              <TextInput
                value={prForm.department}
                onChange={(e) => setPrForm({ ...prForm, department: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Required By Date</label>
              <TextInput
                type="date"
                value={prForm.requiredDate}
                onChange={(e) => setPrForm({ ...prForm, requiredDate: e.target.value })}
              />
            </div>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-700">Purpose</label>
            <TextInput
              value={prForm.purpose}
              onChange={(e) => setPrForm({ ...prForm, purpose: e.target.value })}
            />
          </div>

          <div className="border-t pt-3">
            <h4 className="text-sm font-semibold mb-2">Requisition Items</h4>
            {prForm.items.map((item, idx) => (
              <div key={idx} className="grid grid-cols-4 gap-2 mb-2 items-end">
                <div className="col-span-2">
                  <label className="block text-xs font-medium text-slate-700">Product</label>
                  <Select
                    value={item.productId}
                    onChange={(e) => {
                      const prod = foundation.data?.products.find((p) => p.id === e.target.value);
                      const updated = [...prForm.items];
                      updated[idx].productId = e.target.value;
                      if (prod) {
                        updated[idx].uomId = prod.unitId;
                        updated[idx].estimatedCost = (prod.costPriceCents / 100).toFixed(2);
                      }
                      setPrForm({ ...prForm, items: updated });
                    }}
                  >
                    <option value="">Select product...</option>
                    {(foundation.data?.products ?? []).map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name} ({money(p.costPriceCents)})
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-700">Qty</label>
                  <TextInput
                    type="number"
                    value={item.quantity}
                    onChange={(e) => {
                      const updated = [...prForm.items];
                      updated[idx].quantity = e.target.value;
                      setPrForm({ ...prForm, items: updated });
                    }}
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-700">Est. Unit Cost (₱)</label>
                  <TextInput
                    type="number"
                    step="0.01"
                    value={item.estimatedCost}
                    onChange={(e) => {
                      const updated = [...prForm.items];
                      updated[idx].estimatedCost = e.target.value;
                      setPrForm({ ...prForm, items: updated });
                    }}
                  />
                </div>
              </div>
            ))}
          </div>

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setPrModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                try {
                  const defaultUom = foundation.data?.unitsOfMeasure[0]?.id ?? "";
                  await createRequisition({
                    department: prForm.department,
                    purpose: prForm.purpose,
                    requiredDate: prForm.requiredDate || undefined,
                    notes: prForm.notes,
                    items: prForm.items.map((it) => ({
                      productId: it.productId,
                      uomId: it.uomId || defaultUom,
                      requestedQuantityX1000: Math.round(parseFloat(it.quantity) * 1000),
                      estimatedCostCents: Math.round(parseFloat(it.estimatedCost) * 100),
                      preferredSupplierId: it.preferredSupplierId || undefined,
                    })),
                  });
                  notify.success("Purchase Requisition created.");
                  setPrModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Save Requisition
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Reject Requisition */}
      <Dialog
        open={rejectReasonModalOpen}
        onClose={() => setRejectReasonModalOpen(false)}
        title="Reject Purchase Requisition"
      >
        <div className="space-y-4">
          <p className="text-sm text-slate-600">Provide an audit reason for rejecting this requisition:</p>
          <TextInput
            placeholder="Reason for rejection (required)..."
            value={rejectReasonText}
            onChange={(e) => setRejectReasonText(e.target.value)}
          />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setRejectReasonModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              disabled={!rejectReasonText.trim()}
              onClick={async () => {
                try {
                  await rejectRequisition(rejectTargetPrId, rejectReasonText.trim());
                  notify.success("Requisition rejected.");
                  setRejectReasonModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Confirm Rejection
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Convert PR to Split POs */}
      <Dialog
        open={splitPoModalOpen}
        onClose={() => setSplitPoModalOpen(false)}
        title={`Generate Purchase Orders for PR ${splitPr?.prNumber}`}
      >
        <div className="space-y-4">
          <p className="text-xs text-slate-500">
            Split PR into multiple Purchase Orders or assign all items to a selected supplier.
          </p>
          {splitAllocations.map((alloc, idx) => (
            <div key={idx} className="p-3 border rounded-md space-y-2 bg-slate-50">
              <div>
                <label className="block text-xs font-medium text-slate-700">Target Supplier</label>
                <Select
                  value={alloc.supplierId}
                  onChange={(e) => {
                    const updated = [...splitAllocations];
                    updated[idx].supplierId = e.target.value;
                    setSplitAllocations(updated);
                  }}
                >
                  {(foundation.data?.suppliers ?? []).map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name} ({s.code})
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-slate-700">Allocated Items</label>
                <div className="text-xs text-slate-600 space-y-1">
                  {splitPr?.items.map((item) => (
                    <div key={item.id} className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={alloc.prItemIds.includes(item.id)}
                        onChange={(e) => {
                          const updated = [...splitAllocations];
                          if (e.target.checked) {
                            updated[idx].prItemIds.push(item.id);
                          } else {
                            updated[idx].prItemIds = updated[idx].prItemIds.filter((id) => id !== item.id);
                          }
                          setSplitAllocations(updated);
                        }}
                      />
                      <span>
                        {item.productName} ({item.requestedQuantityX1000 / 1000} units)
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          ))}

          <div className="flex justify-end gap-2 pt-4">
            <Button variant="secondary" onClick={() => setSplitPoModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                if (!splitPr) return;
                try {
                  const created = await convertRequisitionToPo(splitPr.id, splitAllocations);
                  notify.success(`Successfully generated ${created.length} Purchase Order(s)!`);
                  setSplitPoModalOpen(false);
                  refreshAll();
                  setActiveTab("po");
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Generate PO(s)
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Goods Receiving against PO */}
      <Dialog
        open={rrModalOpen}
        onClose={() => setRrModalOpen(false)}
        title={`Receive Goods against PO: ${selectedPoForRr?.poNumber}`}
      >
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Delivery Receipt (DR) #</label>
              <TextInput
                value={rrForm.deliveryReceiptNumber}
                onChange={(e) => setRrForm({ ...rrForm, deliveryReceiptNumber: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Carrier / Trucker</label>
              <TextInput
                value={rrForm.carrier}
                onChange={(e) => setRrForm({ ...rrForm, carrier: e.target.value })}
              />
            </div>
          </div>

          <div className="border-t pt-3 space-y-3">
            <h4 className="text-sm font-semibold">Delivery Inspection Lines</h4>
            {rrForm.items.map((item, idx) => (
              <div key={idx} className="p-3 border rounded-md bg-slate-50 space-y-2">
                <div className="font-medium text-slate-900 text-sm">
                  {item.productName} (Ordered: {item.orderedQty})
                </div>
                <div className="grid grid-cols-3 gap-2">
                  <div>
                    <label className="block text-xs font-medium text-slate-700">Delivered</label>
                    <TextInput
                      type="number"
                      value={item.deliveredQty}
                      onChange={(e) => {
                        const updated = [...rrForm.items];
                        updated[idx].deliveredQty = e.target.value;
                        updated[idx].acceptedQty = e.target.value;
                        setRrForm({ ...rrForm, items: updated });
                      }}
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-emerald-700">Accepted</label>
                    <TextInput
                      type="number"
                      value={item.acceptedQty}
                      onChange={(e) => {
                        const updated = [...rrForm.items];
                        updated[idx].acceptedQty = e.target.value;
                        setRrForm({ ...rrForm, items: updated });
                      }}
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-amber-700">Rejected</label>
                    <TextInput
                      type="number"
                      value={item.rejectedQty}
                      onChange={(e) => {
                        const updated = [...rrForm.items];
                        updated[idx].rejectedQty = e.target.value;
                        setRrForm({ ...rrForm, items: updated });
                      }}
                    />
                  </div>
                </div>
                {parseFloat(item.rejectedQty) > 0 && (
                  <div>
                    <label className="block text-xs font-medium text-amber-700">
                      Rejection Reason (Quarantine Hold Routing)
                    </label>
                    <TextInput
                      placeholder="e.g. Damaged packaging, expired seal, leak"
                      value={item.rejectionReason}
                      onChange={(e) => {
                        const updated = [...rrForm.items];
                        updated[idx].rejectionReason = e.target.value;
                        setRrForm({ ...rrForm, items: updated });
                      }}
                    />
                  </div>
                )}
              </div>
            ))}
          </div>

          <div className="flex items-center gap-2 pt-2">
            <input
              type="checkbox"
              id="overdelivery"
              checked={rrForm.isOverdeliveryApproved}
              onChange={(e) => setRrForm({ ...rrForm, isOverdeliveryApproved: e.target.checked })}
            />
            <label htmlFor="overdelivery" className="text-xs text-slate-700">
              Approve overdelivery exception if delivered quantity exceeds ordered PO quantity
            </label>
          </div>

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setRrModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                if (!selectedPoForRr) return;
                try {
                  await createReceivingReport({
                    purchaseOrderId: selectedPoForRr.id,
                    deliveryReceiptNumber: rrForm.deliveryReceiptNumber,
                    carrier: rrForm.carrier,
                    driverName: rrForm.driverName,
                    isOverdeliveryApproved: rrForm.isOverdeliveryApproved,
                    notes: rrForm.notes,
                    items: rrForm.items.map((it) => ({
                      poItemId: it.poItemId,
                      productId: it.productId,
                      uomId: it.uomId,
                      deliveredQuantityX1000: Math.round(parseFloat(it.deliveredQty || "0") * 1000),
                      acceptedQuantityX1000: Math.round(parseFloat(it.acceptedQty || "0") * 1000),
                      rejectedQuantityX1000: Math.round(parseFloat(it.rejectedQty || "0") * 1000),
                      targetLocationId: it.targetLocationId,
                      quarantineLocationId: it.quarantineLocationId,
                      rejectionReason: it.rejectionReason || undefined,
                    })),
                  });
                  notify.success("Goods Receiving Report posted and inventory credited!");
                  setRrModalOpen(false);
                  refreshAll();
                  setActiveTab("rr");
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Post Receiving Report
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Physical Count Adjustment (ADJ) */}
      <Dialog
        open={stockAdjModalOpen}
        onClose={() => setStockAdjModalOpen(false)}
        title="Physical Inventory Count Variance Adjustment (ADJ)"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-700">Product</label>
            <Select
              value={stockAdjForm.productId}
              onChange={(e) => {
                const card = stockCards.data?.find((c) => c.productId === e.target.value);
                setStockAdjForm({
                  ...stockAdjForm,
                  productId: e.target.value,
                  systemQty: card ? (card.totalQuantityX1000 / 1000).toString() : "0",
                  actualQty: card ? (card.totalQuantityX1000 / 1000).toString() : "0",
                  unitCost: card ? (card.averageUnitCostCents / 100).toFixed(2) : "0",
                });
              }}
            >
              {(stockCards.data ?? []).map((c) => (
                <option key={c.productId} value={c.productId}>
                  {c.productName} (Sys: {c.totalQuantityX1000 / 1000})
                </option>
              ))}
            </Select>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Location Bucket</label>
              <Select
                value={stockAdjForm.locationId}
                onChange={(e) => setStockAdjForm({ ...stockAdjForm, locationId: e.target.value })}
              >
                {(foundation.data?.locations ?? []).map((loc) => (
                  <option key={loc.id} value={loc.id}>
                    {loc.name} ({loc.code})
                  </option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Avg Unit Cost (₱)</label>
              <TextInput
                type="number"
                step="0.01"
                value={stockAdjForm.unitCost}
                onChange={(e) => setStockAdjForm({ ...stockAdjForm, unitCost: e.target.value })}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">System Count</label>
              <TextInput value={stockAdjForm.systemQty} disabled />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Physical Actual Count</label>
              <TextInput
                type="number"
                value={stockAdjForm.actualQty}
                onChange={(e) => setStockAdjForm({ ...stockAdjForm, actualQty: e.target.value })}
              />
            </div>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-700">Adjustment Justification</label>
            <TextInput
              value={stockAdjForm.reason}
              onChange={(e) => setStockAdjForm({ ...stockAdjForm, reason: e.target.value })}
            />
          </div>

          <div className="flex justify-end gap-2 pt-4">
            <Button variant="secondary" onClick={() => setStockAdjModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                try {
                  const res = await createStockAdjustment({
                    productId: stockAdjForm.productId,
                    locationId: stockAdjForm.locationId,
                    systemQuantityX1000: Math.round(parseFloat(stockAdjForm.systemQty) * 1000),
                    actualQuantityX1000: Math.round(parseFloat(stockAdjForm.actualQty) * 1000),
                    unitCostCents: Math.round(parseFloat(stockAdjForm.unitCost) * 100),
                    reason: stockAdjForm.reason,
                  });
                  notify.success(
                    `Inventory adjustment posted! Variance: ${res.varianceQuantityX1000 / 1000} units (${money(
                      res.varianceValuationCents
                    )})`
                  );
                  setStockAdjModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Post Adjustment
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Supplier Return (SRET) */}
      <Dialog
        open={returnModalOpen}
        onClose={() => setReturnModalOpen(false)}
        title="Supplier Return & Debit Memo (SRET)"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-700">Supplier</label>
            <Select
              value={returnForm.supplierId}
              onChange={(e) => setReturnForm({ ...returnForm, supplierId: e.target.value })}
            >
              {(foundation.data?.suppliers ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name} ({s.code})
                </option>
              ))}
            </Select>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-700">Return Reason</label>
            <TextInput
              value={returnForm.reason}
              onChange={(e) => setReturnForm({ ...returnForm, reason: e.target.value })}
            />
          </div>

          <div className="border-t pt-3 space-y-2">
            <h4 className="text-xs font-semibold uppercase text-slate-500">Return Items</h4>
            {returnForm.items.map((item, idx) => (
              <div key={idx} className="grid grid-cols-4 gap-2 items-end">
                <div className="col-span-2">
                  <label className="block text-xs font-medium text-slate-700">Product</label>
                  <Select
                    value={item.productId}
                    onChange={(e) => {
                      const prod = foundation.data?.products.find((p) => p.id === e.target.value);
                      const updated = [...returnForm.items];
                      updated[idx].productId = e.target.value;
                      if (prod) {
                        updated[idx].uomId = prod.unitId;
                        updated[idx].unitCost = (prod.costPriceCents / 100).toFixed(2);
                      }
                      setReturnForm({ ...returnForm, items: updated });
                    }}
                  >
                    {(foundation.data?.products ?? []).map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-700">Qty</label>
                  <TextInput
                    type="number"
                    value={item.quantity}
                    onChange={(e) => {
                      const updated = [...returnForm.items];
                      updated[idx].quantity = e.target.value;
                      setReturnForm({ ...returnForm, items: updated });
                    }}
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-700">Source Bucket</label>
                  <Select
                    value={item.sourceLocationId}
                    onChange={(e) => {
                      const updated = [...returnForm.items];
                      updated[idx].sourceLocationId = e.target.value;
                      setReturnForm({ ...returnForm, items: updated });
                    }}
                  >
                    {(foundation.data?.locations ?? []).map((loc) => (
                      <option key={loc.id} value={loc.id}>
                        {loc.name}
                      </option>
                    ))}
                  </Select>
                </div>
              </div>
            ))}
          </div>

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setReturnModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                try {
                  const defaultUom = foundation.data?.unitsOfMeasure[0]?.id ?? "";
                  const defaultLoc = quarantineLocation?.id ?? mainStoreLocation?.id ?? "";
                  await createSupplierReturn({
                    supplierId: returnForm.supplierId,
                    purchaseOrderId: returnForm.purchaseOrderId || undefined,
                    receivingReportId: returnForm.receivingReportId || undefined,
                    reason: returnForm.reason,
                    items: returnForm.items.map((it) => ({
                      productId: it.productId,
                      uomId: it.uomId || defaultUom,
                      quantityX1000: Math.round(parseFloat(it.quantity) * 1000),
                      unitCostCents: Math.round(parseFloat(it.unitCost) * 100),
                      sourceLocationId: it.sourceLocationId || defaultLoc,
                      reason: returnForm.reason,
                    })),
                  });
                  notify.success("Supplier return processed and debit memo issued!");
                  setReturnModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Post Return & Issue Debit Memo
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Intake Supplier Invoice */}
      <Dialog
        open={invoiceModalOpen}
        onClose={() => setInvoiceModalOpen(false)}
        title="Supplier Invoice Intake (Accounts Payable Voucher)"
      >
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Supplier</label>
              <Select
                value={invoiceForm.supplierId}
                onChange={(e) => setInvoiceForm({ ...invoiceForm, supplierId: e.target.value })}
              >
                {(foundation.data?.suppliers ?? []).map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name} ({s.code})
                  </option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Supplier Invoice #</label>
              <TextInput
                value={invoiceForm.invoiceNumber}
                onChange={(e) => setInvoiceForm({ ...invoiceForm, invoiceNumber: e.target.value })}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Invoice Date</label>
              <TextInput
                type="date"
                value={invoiceForm.invoiceDate}
                onChange={(e) => setInvoiceForm({ ...invoiceForm, invoiceDate: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Due Date</label>
              <TextInput
                type="date"
                value={invoiceForm.dueDate}
                onChange={(e) => setInvoiceForm({ ...invoiceForm, dueDate: e.target.value })}
              />
            </div>
          </div>

          <div className="border-t pt-3 space-y-2">
            <h4 className="text-xs font-semibold uppercase text-slate-500">Invoice Lines</h4>
            {invoiceForm.items.map((item, idx) => (
              <div key={idx} className="grid grid-cols-4 gap-2 items-end">
                <div className="col-span-2">
                  <label className="block text-xs font-medium text-slate-700">Product</label>
                  <Select
                    value={item.productId}
                    onChange={(e) => {
                      const prod = foundation.data?.products.find((p) => p.id === e.target.value);
                      const updated = [...invoiceForm.items];
                      updated[idx].productId = e.target.value;
                      if (prod) {
                        updated[idx].productName = prod.name;
                        updated[idx].unitCost = (prod.costPriceCents / 100).toFixed(2);
                      }
                      setInvoiceForm({ ...invoiceForm, items: updated });
                    }}
                  >
                    {(foundation.data?.products ?? []).map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-700">Qty</label>
                  <TextInput
                    type="number"
                    value={item.invoicedQty}
                    onChange={(e) => {
                      const updated = [...invoiceForm.items];
                      updated[idx].invoicedQty = e.target.value;
                      setInvoiceForm({ ...invoiceForm, items: updated });
                    }}
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-700">Unit Cost (₱)</label>
                  <TextInput
                    type="number"
                    step="0.01"
                    value={item.unitCost}
                    onChange={(e) => {
                      const updated = [...invoiceForm.items];
                      updated[idx].unitCost = e.target.value;
                      setInvoiceForm({ ...invoiceForm, items: updated });
                    }}
                  />
                </div>
              </div>
            ))}
          </div>

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setInvoiceModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                try {
                  const subtotalCents = invoiceForm.items.reduce(
                    (sum, it) => sum + Math.round(parseFloat(it.invoicedQty || "0") * parseFloat(it.unitCost || "0") * 100),
                    0
                  );
                  await createSupplierInvoice({
                    supplierId: invoiceForm.supplierId,
                    invoiceNumber: invoiceForm.invoiceNumber,
                    invoiceDate: invoiceForm.invoiceDate,
                    dueDate: invoiceForm.dueDate,
                    subtotalCents,
                    totalCents: subtotalCents,
                    notes: invoiceForm.notes,
                    items: invoiceForm.items.map((it) => ({
                      productId: it.productId,
                      invoicedQuantityX1000: Math.round(parseFloat(it.invoicedQty || "0") * 1000),
                      unitCostCents: Math.round(parseFloat(it.unitCost || "0") * 100),
                    })),
                  });
                  notify.success("Supplier Invoice recorded in AP Ledger!");
                  setInvoiceModalOpen(false);
                  refreshAll();
                  setActiveTab("ap");
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Post Invoice & Run 3-Way Match
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Approve Variance */}
      <Dialog
        open={varianceModalOpen}
        onClose={() => setVarianceModalOpen(false)}
        title={`Approve 3-Way Match Variance: ${varianceTargetInvoice?.apvNumber}`}
      >
        <div className="space-y-4">
          <Alert tone="warning">
            This invoice has a price or quantity variance compared against PO/RR records. Approving variance will post
            the invoice to the open AP aging ledger.
          </Alert>
          <div>
            <label className="block text-xs font-medium text-slate-700">Supervisor Justification Notes</label>
            <TextInput
              placeholder="e.g. Authorized supplier fuel surcharge price increase"
              value={varianceApprovalNotes}
              onChange={(e) => setVarianceApprovalNotes(e.target.value)}
            />
          </div>
          <div className="flex justify-end gap-2 pt-4">
            <Button variant="secondary" onClick={() => setVarianceModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              disabled={!varianceApprovalNotes.trim()}
              onClick={async () => {
                if (!varianceTargetInvoice) return;
                try {
                  await approveInvoiceVariance(varianceTargetInvoice.id, varianceApprovalNotes.trim());
                  notify.success("Invoice variance approved. Posted to AP aging ledger.");
                  setVarianceModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Approve Variance
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Record Supplier Payment */}
      <Dialog
        open={paymentModalOpen}
        onClose={() => setPaymentModalOpen(false)}
        title={`Record Payment for ${selectedInvoiceForPayment?.invoiceNumber}`}
      >
        <div className="space-y-4">
          <Alert tone="info">
            Notice: Supplier disbursements are isolated from the POS cashier cash drawer reconciliation and posted
            strictly to the accounts payable settlement ledger.
          </Alert>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Payment Amount (₱)</label>
              <TextInput
                type="number"
                step="0.01"
                value={paymentForm.amount}
                onChange={(e) => setPaymentForm({ ...paymentForm, amount: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Payment Method</label>
              <Select
                value={paymentForm.paymentMethod}
                onChange={(e) => setPaymentForm({ ...paymentForm, paymentMethod: e.target.value })}
              >
                <option value="BankTransfer">Bank Transfer</option>
                <option value="Check">Check</option>
                <option value="Cash">Cash (Treasury Fund)</option>
              </Select>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Reference # / Check #</label>
              <TextInput
                value={paymentForm.referenceNumber}
                onChange={(e) => setPaymentForm({ ...paymentForm, referenceNumber: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Bank Account</label>
              <TextInput
                value={paymentForm.bankAccount}
                onChange={(e) => setPaymentForm({ ...paymentForm, bankAccount: e.target.value })}
              />
            </div>
          </div>

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setPaymentModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                if (!selectedInvoiceForPayment) return;
                try {
                  await recordSupplierPayment({
                    supplierId: selectedInvoiceForPayment.supplierId,
                    invoiceId: selectedInvoiceForPayment.id,
                    amountCents: Math.round(parseFloat(paymentForm.amount) * 100),
                    paymentMethod: paymentForm.paymentMethod,
                    referenceNumber: paymentForm.referenceNumber,
                    bankAccount: paymentForm.bankAccount,
                    notes: paymentForm.notes,
                  });
                  notify.success("Supplier payment posted! AP ledger balance updated.");
                  setPaymentModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Post Payment
            </Button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}

// Subcomponent for Quotation view
function QuotationListView({ requisitionId, onRefresh }: { requisitionId: string; onRefresh: () => void }) {
  const { notify } = useNotifications();
  const quotations = useQuery({
    queryKey: ["lpi-quotations", requisitionId],
    queryFn: () => fetchQuotations(requisitionId),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Received Supplier Quotations</CardTitle>
      </CardHeader>
      <CardBody className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
              <tr>
                <th className="p-3">Supplier</th>
                <th className="p-3">Quote Ref</th>
                <th className="p-3">Valid Until</th>
                <th className="p-3">Lead Time</th>
                <th className="p-3">Terms</th>
                <th className="p-3 text-right">Quoted Total</th>
                <th className="p-3">Status</th>
                <th className="p-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {(quotations.data ?? []).length === 0 ? (
                <tr>
                  <td colSpan={8} className="p-4 text-center text-slate-500">
                    No supplier quotations recorded for this requisition.
                  </td>
                </tr>
              ) : (
                quotations.data?.map((q) => (
                  <tr key={q.id} className="hover:bg-slate-50">
                    <td className="p-3 font-medium text-slate-900">{q.supplierName}</td>
                    <td className="p-3 font-mono">{q.quoteReference}</td>
                    <td className="p-3 text-xs">{q.validUntil || "—"}</td>
                    <td className="p-3">{q.leadTimeDays} days</td>
                    <td className="p-3">{q.paymentTermsDays} days</td>
                    <td className="p-3 text-right font-bold">{money(q.totalQuotedCostCents)}</td>
                    <td className="p-3">
                      <StatusPill tone={q.isWinner ? "success" : "neutral"}>
                        {q.isWinner ? "🏆 Selected Winner" : q.status}
                      </StatusPill>
                    </td>
                    <td className="p-3 text-right">
                      {!q.isWinner && (
                        <Button
                          variant="secondary"
                          onClick={async () => {
                            const just = window.prompt("Selection justification notes:", "Best value and lead time");
                            if (!just) return;
                            try {
                              await selectWinningQuotation(q.id, just);
                              notify.success(`${q.supplierName} selected as winning quote!`);
                              onRefresh();
                            } catch (e) {
                              notify.error((e as Error).message);
                            }
                          }}
                        >
                          Select Winner
                        </Button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </CardBody>
    </Card>
  );
}
