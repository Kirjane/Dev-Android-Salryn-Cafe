import { useState, useEffect } from "react";
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
  createQuotation,
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
  fetchSupplierPayments,
  approveSupplierPayment,
  approveOverdelivery,
  type SupplierPayment,
  type ReceivingReport,
} from "../api/procureToPay";
import { Permissions, hasPermission } from "../security/permissions";
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
  const canViewProcurement = hasPermission(session.data, Permissions.ProcurementView);
  const canViewReceiving = hasPermission(session.data, Permissions.ReceivingView);
  const canViewInventory = hasPermission(session.data, Permissions.InventoryView);
  const canViewPayables = hasPermission(session.data, Permissions.PayablesView);
  const canRequestPr = hasPermission(session.data, Permissions.ProcurementRequest);
  const canApprovePr = hasPermission(session.data, Permissions.ProcurementApproveRequisition);
  const canManagePo = hasPermission(session.data, Permissions.ProcurementManagePurchaseOrder);
  const canApprovePo = hasPermission(session.data, Permissions.ProcurementApprovePurchaseOrder);
  const canManageReceiving = hasPermission(session.data, Permissions.ReceivingManage);
  const canAdjustInventory = hasPermission(session.data, Permissions.InventoryAdjust);
  const canManageReturns = hasPermission(session.data, Permissions.InventoryManageReturns);
  const canManageInvoices = hasPermission(session.data, Permissions.PayablesManageInvoices);
  const canApproveVariance = hasPermission(session.data, Permissions.PayablesApproveVariance);
  const canRecordPayment = hasPermission(session.data, Permissions.PayablesRecordPayment);

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

  useEffect(() => {
    const syncTabFromUrl = () => {
      if (typeof window !== "undefined") {
        const t = new URLSearchParams(window.location.search).get("tab");
        if (t && ["overview", "suppliers", "pr", "quotations", "po", "rr", "inventory", "ap"].includes(t)) {
          setActiveTab(t as any);
        }
      }
    };
    syncTabFromUrl();
    window.addEventListener("popstate", syncTabFromUrl);
    return () => window.removeEventListener("popstate", syncTabFromUrl);
  }, []);

  // Queries
  const foundation = useQuery({ queryKey: ["lpi-foundation"], queryFn: () => fetchLpiFoundation(true), enabled: canViewProcurement });
  const overview = useQuery({ queryKey: ["lpi-overview"], queryFn: fetchLpiOverview, enabled: canViewProcurement });
  const suppliers = useQuery({ queryKey: ["lpi-suppliers"], queryFn: () => fetchSuppliers("", true), enabled: canViewProcurement });
  const requisitions = useQuery({ queryKey: ["lpi-requisitions"], queryFn: () => fetchRequisitions(), enabled: canViewProcurement });
  const purchaseOrders = useQuery({ queryKey: ["lpi-pos"], queryFn: () => fetchPurchaseOrders(), enabled: canViewProcurement });
  const receivingReports = useQuery({ queryKey: ["lpi-rrs"], queryFn: () => fetchReceivingReports(), enabled: canViewReceiving });
  const stockCards = useQuery({ queryKey: ["lpi-stock-cards"], queryFn: fetchStockCards, enabled: canViewInventory });
  const supplierReturns = useQuery({ queryKey: ["lpi-returns"], queryFn: () => fetchSupplierReturns(), enabled: canViewInventory });
  const invoices = useQuery({ queryKey: ["lpi-invoices"], queryFn: () => fetchSupplierInvoices(), enabled: canViewPayables });
  const payments = useQuery({ queryKey: ["lpi-payments"], queryFn: () => fetchSupplierPayments(), enabled: canViewPayables });

  const [paymentApproveModalOpen, setPaymentApproveModalOpen] = useState(false);
  const [paymentToApprove, setPaymentToApprove] = useState<SupplierPayment | null>(null);
  const [paymentApprovalRemarks, setPaymentApprovalRemarks] = useState("");

  const [overdeliveryModalOpen, setOverdeliveryModalOpen] = useState(false);
  const [rrToApprove, setRrToApprove] = useState<ReceivingReport | null>(null);
  const [overdeliveryReason, setOverdeliveryReason] = useState("");

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

  const [selectedPrForQuote, setSelectedPrForQuote] = useState<string>(() => {
    if (typeof window !== "undefined") {
      return new URLSearchParams(window.location.search).get("prId") || "";
    }
    return "";
  });

  useEffect(() => {
    if (!selectedPrForQuote && (requisitions.data ?? []).length > 0) {
      setSelectedPrForQuote(requisitions.data![0].id);
    }
  }, [requisitions.data, selectedPrForQuote]);

  const [splitPoModalOpen, setSplitPoModalOpen] = useState(false);
  const [splitPr, setSplitPr] = useState<PurchaseRequisition | null>(null);
  const [splitAllocations, setSplitAllocations] = useState<Array<{ supplierId: string; prItemIds: string[] }>>([]);

  const [rrModalOpen, setRrModalOpen] = useState(false);
  const [selectedPoForRr, setSelectedPoForRr] = useState<PurchaseOrder | null>(null);
  const [rrForm, setRrForm] = useState({
    deliveryReceiptNumber: "",
    carrier: "Direct Delivery",
    driverName: "",
    locationId: "",
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
    receivingReportId: "",
    receivingReportLineId: "",
    quantity: "1",
    reason: "Defective goods returned post-acceptance",
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
    void queryClient.invalidateQueries({ queryKey: ["lpi-payments"] });
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
              value={overview.data?.costCoverageIncomplete ? "Coverage Incomplete" : money(overview.data?.totalInventoryValuationCents ?? 0)}
              tone={overview.data?.costCoverageIncomplete ? "warning" : "success"}
            />
          </div>

          {overview.data?.costCoverageIncomplete && (
            <Alert tone="warning" title="Inventory Cost Coverage Incomplete">
              Valuation accounts only for inventory with established purchase costs.{" "}
              {overview.data.missingCostProductCount ?? 0} active product(s) hold positive stock without recorded unit costs.
              Salryn refuses to fabricate estimated costs to avoid corrupting fiscal records.
            </Alert>
          )}

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
            {canManagePo && (
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
            )}
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
                          {canManagePo && (
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

      {/* TAB 3: REQUISITIONS (PR) */}
      {activeTab === "pr" && (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h2 className="text-lg font-semibold text-slate-900">Purchase Requisitions (PR)</h2>
            {canRequestPr && (
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
            )}
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
                          {canRequestPr && pr.status === "Draft" && (
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
                          {canApprovePr && pr.status === "Submitted" && (
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
                          {canManagePo && pr.status === "Approved" && (
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
            <QuotationListView
              requisitionId={selectedPrForQuote}
              requisition={(requisitions.data ?? []).find((r) => r.id === selectedPrForQuote)}
              suppliers={suppliers.data ?? []}
              onRefresh={refreshAll}
            />
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
                          {canApprovePo && po.status === "Draft" && (
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
                          {canManagePo && po.status === "Approved" && (
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
                          {canManagePo && (po.status === "Draft" || po.status === "Approved" || po.status === "Sent") && (
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
              {canManageReceiving && (
                <Select
                  onChange={(e) => {
                    const po = purchaseOrders.data?.find((p) => p.id === e.target.value);
                    if (po) {
                      setSelectedPoForRr(po);
                      const poLineItems = ((po.items ?? (po as any).lines ?? []) as any[]);
                      const targetLocId = po.deliveryLocationId || mainStoreLocation?.id || foundation.data?.locations?.[0]?.id || "";
                      setRrForm({
                        deliveryReceiptNumber: "",
                        carrier: "Supplier Trucking",
                        driverName: "Carrier Dispatch Driver",
                        locationId: targetLocId,
                        notes: "Dock delivery inspection",
                        items: poLineItems.map((item) => {
                          const ordered = Number(item.orderedQuantityX1000 ?? item.orderedQtyX1000 ?? 0);
                          const accepted = Number(item.receivedAcceptedQtyX1000 ?? item.receivedQuantityX1000 ?? item.receivedQtyX1000 ?? 0);
                          const outstanding = item.outstandingQuantityX1000 != null
                            ? Number(item.outstandingQuantityX1000)
                            : Math.max(0, ordered - accepted);
                          const remaining = outstanding / 1000;
                          return {
                            poItemId: item.id ?? item.poItemId,
                            productId: item.productId,
                            productName: item.productName ?? item.itemDescription ?? "Item",
                            uomId: item.uomId ?? item.unitId ?? "",
                            orderedQty: ordered / 1000,
                            deliveredQty: remaining > 0 ? remaining.toString() : "0",
                            acceptedQty: remaining > 0 ? remaining.toString() : "0",
                            rejectedQty: "0",
                            rejectionReason: "",
                            targetLocationId: targetLocId,
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
              )}
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
                      <th className="p-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {(receivingReports.data ?? []).map((rr) => {
                      const isPreparer = currentUserId === rr.receivedByUserId;
                      const canApproveEx = hasPermission(session.data, Permissions.ReceivingApproveException);
                      return (
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
                              {rr.status} {rr.isOverdelivery ? "· Overdelivery" : ""}
                            </StatusPill>
                          </td>
                          <td className="p-3 text-right">
                            {rr.isOverdelivery && rr.status !== "Posted" && rr.status !== "Cancelled" && (
                              <Button
                                variant="secondary"
                                disabled={isPreparer || !canApproveEx}
                                title={isPreparer ? "Separation of duties: receiving clerk cannot approve own overdelivery exception" : !canApproveEx ? "Requires Receiving.ApproveException permission" : undefined}
                                onClick={() => {
                                  setRrToApprove(rr);
                                  setOverdeliveryReason("Authorized delivery tolerance variance");
                                  setOverdeliveryModalOpen(true);
                                }}
                              >
                                Approve Overdelivery
                              </Button>
                            )}
                          </td>
                        </tr>
                      );
                    })}
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
              {canAdjustInventory && (
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
              )}
              {canManageReturns && (
                <Button
                  variant="danger"
                  onClick={() => {
                    const report = (receivingReports.data ?? []).find((candidate) =>
                      candidate.status === "Posted" && candidate.items.some((line) => line.productId)
                    );
                    if (!report) {
                      notify.error("A posted receiving report with a product line is required before goods can be returned.");
                      return;
                    }
                    setReturnForm({
                      receivingReportId: report.id,
                      receivingReportLineId: report.items[0]?.id ?? "",
                      quantity: "1",
                      reason: "Post-acceptance latent defect discovered in quarantine",
                    });
                    setReturnModalOpen(true);
                  }}
                >
                  + Supplier Return (SRET)
                </Button>
              )}
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
              {canManageInvoices && (
                <Button
                  onClick={() => {
                    const sup = foundation.data?.suppliers[0];
                    const prod = foundation.data?.products[0];
                    const matchingPo = (purchaseOrders.data ?? []).find(
                      (po) => po.supplierId === sup?.id && (po.status === "Approved" || po.status === "Sent" || po.status === "Fulfilled")
                    );
                    const matchingRr = (receivingReports.data ?? []).find(
                      (rr) => rr.supplierId === sup?.id && rr.status === "Posted"
                    );
                    const poLine = matchingPo?.items.find((item) => item.productId === prod?.id) ?? matchingPo?.items[0];
                    const rrLine = matchingRr?.items.find((item) => item.productId === prod?.id) ?? matchingRr?.items[0];
                    setInvoiceForm({
                      supplierId: sup?.id ?? "",
                      invoiceNumber: `INV-${Date.now().toString().slice(-5)}`,
                      invoiceDate: new Date().toISOString().slice(0, 10),
                      dueDate: new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10),
                      purchaseOrderId: matchingPo?.id ?? "",
                      receivingReportId: matchingRr?.id ?? "",
                      notes: "Supplier accounts payable voucher",
                      items: [
                        {
                          productId: prod?.id ?? "",
                          productName: prod?.name ?? "Item",
                          invoicedQty: "10",
                          unitCost: prod ? (prod.costPriceCents / 100).toFixed(2) : "10",
                          poItemId: poLine?.id,
                          rrItemId: rrLine?.id,
                        },
                      ],
                    });
                    setInvoiceModalOpen(true);
                  }}
                >
                  + Intake Supplier Invoice
                </Button>
              )}
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
                                : inv.matchStatus === "PendingDocuments"
                                ? "warning"
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
                          {canApproveVariance && (inv.matchStatus === "PriceVariance" || inv.matchStatus === "QuantityVariance") && (
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
                          {canRecordPayment && inv.balanceDueCents > 0 &&
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

          {/* Supplier Payments & Disbursements Card */}
          <div className="space-y-4">
            <div className="flex justify-between items-center">
              <h2 className="text-lg font-semibold text-slate-900">Supplier Payments & Disbursements (Separation of Duties)</h2>
            </div>
            <Card>
              <CardBody className="p-0">
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="bg-slate-50 text-slate-700 uppercase font-semibold text-xs border-b">
                      <tr>
                        <th className="p-3">Payment #</th>
                        <th className="p-3">Date</th>
                        <th className="p-3">Supplier</th>
                        <th className="p-3">Method</th>
                        <th className="p-3">Reference / Bank</th>
                        <th className="p-3 text-right">Amount</th>
                        <th className="p-3">Status</th>
                        <th className="p-3 text-right">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {(payments.data ?? []).length === 0 ? (
                        <tr>
                          <td colSpan={8} className="p-4 text-center text-slate-500">
                            No supplier payments recorded.
                          </td>
                        </tr>
                      ) : (
                        (payments.data ?? []).map((pay) => {
                          const isPreparer = currentUserId === pay.recordedByUserId;
                          const canApprovePay = hasPermission(session.data, Permissions.PayablesApprovePayment);
                          return (
                            <tr key={pay.id} className="hover:bg-slate-50">
                              <td className="p-3 font-mono font-bold text-slate-900">{pay.paymentNumber}</td>
                              <td className="p-3 text-xs">{pay.paymentDate}</td>
                              <td className="p-3 font-medium">{pay.supplierName}</td>
                              <td className="p-3 text-xs">{pay.paymentMethod}</td>
                              <td className="p-3 font-mono text-xs">{pay.referenceNumber || pay.bankAccount || "—"}</td>
                              <td className="p-3 text-right font-bold text-slate-900">{money(pay.amountCents)}</td>
                              <td className="p-3">
                                <StatusPill tone={pay.status === "Settled" ? "success" : "warning"}>
                                  {pay.status === "PendingApproval" ? "Pending Approval" : pay.status}
                                </StatusPill>
                              </td>
                              <td className="p-3 text-right space-x-2">
                                {pay.status === "PendingApproval" && (
                                  <Button
                                    variant="primary"
                                    disabled={isPreparer || !canApprovePay}
                                    title={isPreparer ? "Separation of duties: payment preparer cannot approve settlement" : !canApprovePay ? "Requires Payables.ApprovePayment permission" : undefined}
                                    onClick={() => {
                                      setPaymentToApprove(pay);
                                      setPaymentApprovalRemarks("Verified against invoice and disbursement schedule");
                                      setPaymentApproveModalOpen(true);
                                    }}
                                  >
                                    Approve Settlement
                                  </Button>
                                )}
                              </td>
                            </tr>
                          );
                        })
                      )}
                    </tbody>
                  </table>
                </div>
              </CardBody>
            </Card>
          </div>
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
          <div className="grid grid-cols-3 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Delivery Receipt (DR) # *</label>
              <TextInput
                value={rrForm.deliveryReceiptNumber}
                onChange={(e) => setRrForm({ ...rrForm, deliveryReceiptNumber: e.target.value })}
                placeholder="e.g. DR-2026-9901"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Receiving Location</label>
              <Select
                value={rrForm.locationId}
                onChange={(e) => {
                  const locId = e.target.value;
                  const updatedItems = rrForm.items.map(it => ({ ...it, targetLocationId: locId }));
                  setRrForm({ ...rrForm, locationId: locId, items: updatedItems });
                }}
              >
                {(foundation.data?.locations ?? []).map((loc) => (
                  <option key={loc.id} value={loc.id}>{loc.name} ({loc.code})</option>
                ))}
              </Select>
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

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setRrModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                if (!selectedPoForRr) return;
                if (!rrForm.deliveryReceiptNumber.trim()) {
                  notify.error("Delivery receipt number is mandatory.");
                  return;
                }
                try {
                  await createReceivingReport({
                    purchaseOrderId: selectedPoForRr.id,
                    locationId: rrForm.locationId,
                    deliveryReceiptNumber: rrForm.deliveryReceiptNumber.trim(),
                    carrier: rrForm.carrier,
                    driverName: rrForm.driverName,
                    notes: rrForm.notes,
                    items: rrForm.items.map((it) => ({
                      poItemId: it.poItemId,
                      productId: it.productId,
                      uomId: it.uomId,
                      deliveredQuantityX1000: Math.round(parseFloat(it.deliveredQty || "0") * 1000),
                      acceptedQuantityX1000: Math.round(parseFloat(it.acceptedQty || "0") * 1000),
                      rejectedQuantityX1000: Math.round(parseFloat(it.rejectedQty || "0") * 1000),
                      targetLocationId: rrForm.locationId || it.targetLocationId,
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
            <label className="block text-xs font-medium text-slate-700">Posted Receiving Report</label>
            <Select
              value={returnForm.receivingReportId}
              onChange={(e) => {
                const report = (receivingReports.data ?? []).find((candidate) => candidate.id === e.target.value);
                setReturnForm({ ...returnForm, receivingReportId: e.target.value, receivingReportLineId: report?.items[0]?.id ?? "" });
              }}
            >
              {(receivingReports.data ?? []).filter((report) => report.status === "Posted").map((report) => (
                <option key={report.id} value={report.id}>
                  {report.rrNumber} · {report.supplierName}
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

          <div className="grid grid-cols-3 gap-3 border-t pt-3">
            <div className="col-span-2">
              <label className="block text-xs font-medium text-slate-700">Accepted Receiving Line</label>
              <Select value={returnForm.receivingReportLineId} onChange={(e) => setReturnForm({ ...returnForm, receivingReportLineId: e.target.value })}>
                {((receivingReports.data ?? []).find((report) => report.id === returnForm.receivingReportId)?.items ?? []).map((line) => (
                  <option key={line.id} value={line.id}>{line.productName} · accepted {line.acceptedQuantityX1000 / 1000}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Return Qty</label>
              <TextInput type="number" min="0.001" step="0.001" value={returnForm.quantity} onChange={(e) => setReturnForm({ ...returnForm, quantity: e.target.value })} />
            </div>
          </div>

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setReturnModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                try {
                  const returnQtyX1000 = Math.round(Number(returnForm.quantity) * 1000);
                  if (!returnForm.receivingReportId || !returnForm.receivingReportLineId || returnQtyX1000 <= 0) {
                    throw new Error("Choose a posted receiving line and enter a positive return quantity.");
                  }
                  await createSupplierReturn({
                    receivingReportId: returnForm.receivingReportId,
                    reason: returnForm.reason,
                    lines: [{ receivingReportLineId: returnForm.receivingReportLineId, returnQtyX1000, reason: returnForm.reason }],
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
                    purchaseOrderId: invoiceForm.purchaseOrderId || undefined,
                    receivingReportId: invoiceForm.receivingReportId || undefined,
                    subtotalCents,
                    totalCents: subtotalCents,
                    notes: invoiceForm.notes,
                    items: invoiceForm.items.map((it) => {
                      const poLine = (purchaseOrders.data ?? [])
                        .find((po) => po.id === invoiceForm.purchaseOrderId)
                        ?.items.find((item) => item.productId === it.productId);
                      const rrLine = (receivingReports.data ?? [])
                        .find((rr) => rr.id === invoiceForm.receivingReportId)
                        ?.items.find((item) => item.productId === it.productId);
                      return {
                        productId: it.productId,
                        invoicedQuantityX1000: Math.round(parseFloat(it.invoicedQty || "0") * 1000),
                        unitCostCents: Math.round(parseFloat(it.unitCost || "0") * 100),
                        poItemId: it.poItemId || poLine?.id,
                        rrItemId: it.rrItemId || rrLine?.id,
                      };
                    }),
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
              <label className="block text-xs font-medium text-slate-700">Reference # / Check # *</label>
              <TextInput
                value={paymentForm.referenceNumber}
                onChange={(e) => setPaymentForm({ ...paymentForm, referenceNumber: e.target.value })}
                placeholder="e.g. TR-2026-8801 / CHK-1092"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Disbursement Source</label>
              <TextInput
                value={paymentForm.paymentMethod === "Cash" ? "TreasuryFund (Corporate Cash - Non-POS)" : "OperatingAccount (Operating Bank Account)"}
                readOnly
                className="bg-slate-100 text-slate-600"
              />
            </div>
          </div>

          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setPaymentModalOpen(false)}>
              Cancel
            </Button>
            <Button
              disabled={!paymentForm.referenceNumber.trim() || !paymentForm.amount || parseFloat(paymentForm.amount) <= 0}
              onClick={async () => {
                if (!selectedInvoiceForPayment) return;
                if (!paymentForm.referenceNumber.trim()) {
                  notify.error("Payment reference number is mandatory.");
                  return;
                }
                try {
                  const source = paymentForm.paymentMethod === "Cash" ? "TreasuryFund" : "OperatingAccount";
                  await recordSupplierPayment({
                    supplierId: selectedInvoiceForPayment.supplierId,
                    invoiceId: selectedInvoiceForPayment.id,
                    amountCents: Math.round(parseFloat(paymentForm.amount) * 100),
                    paymentMethod: paymentForm.paymentMethod,
                    disbursementSource: source,
                    referenceNumber: paymentForm.referenceNumber.trim(),
                    bankAccount: paymentForm.bankAccount,
                    notes: paymentForm.notes,
                  });
                  notify.success("Supplier payment recorded in Pending Approval status! Awaiting independent manager authorization.");
                  setPaymentModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Submit for Approval
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Approve Supplier Payment (Separation of Duties) */}
      <Dialog
        open={paymentApproveModalOpen}
        onClose={() => setPaymentApproveModalOpen(false)}
        title="Authorize Supplier Payment Settlement"
      >
        <div className="space-y-4">
          <Alert tone="info">
            <strong>Dual-Control Authorization:</strong> Independent approval posts the payment to the AP ledger and deducts the outstanding invoice balance.
          </Alert>
          {paymentToApprove && (
            <div className="rounded-lg bg-slate-50 p-3 space-y-1 text-sm border">
              <div><strong>Payment #:</strong> {paymentToApprove.paymentNumber}</div>
              <div><strong>Supplier:</strong> {paymentToApprove.supplierName}</div>
              <div><strong>Amount:</strong> {money(paymentToApprove.amountCents)}</div>
              <div><strong>Method:</strong> {paymentToApprove.paymentMethod}</div>
              <div><strong>Disbursement Date:</strong> {paymentToApprove.paymentDate}</div>
            </div>
          )}
          <div>
            <label className="block text-xs font-medium text-slate-700">Approval Remarks / Authorization Audit Notes</label>
            <TextInput
              value={paymentApprovalRemarks}
              onChange={(e) => setPaymentApprovalRemarks(e.target.value)}
              placeholder="e.g., Authorized per disbursement schedule"
            />
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setPaymentApproveModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              onClick={async () => {
                if (!paymentToApprove) return;
                try {
                  await approveSupplierPayment(paymentToApprove.id, paymentApprovalRemarks);
                  notify.success("Supplier payment settled and posted to Accounts Payable!");
                  setPaymentApproveModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Authorize Settlement
            </Button>
          </div>
        </div>
      </Dialog>

      {/* DIALOG: Approve Goods Receiving Overdelivery */}
      <Dialog
        open={overdeliveryModalOpen}
        onClose={() => setOverdeliveryModalOpen(false)}
        title="Approve Goods Receiving Overdelivery Exception"
      >
        <div className="space-y-4">
          <Alert tone="warning">
            <strong>Overdelivery Exception:</strong> Explicit supervisory authorization is required before stock is received into warehouse inventory.
          </Alert>
          {rrToApprove && (
            <div className="rounded-lg bg-slate-50 p-3 space-y-1 text-sm border">
              <div><strong>Receiving Report #:</strong> {rrToApprove.rrNumber}</div>
              <div><strong>Purchase Order #:</strong> {rrToApprove.poNumber}</div>
              <div><strong>Supplier:</strong> {rrToApprove.supplierName}</div>
              <div><strong>Delivered Qty:</strong> {rrToApprove.totalDeliveredQuantityX1000 / 1000}</div>
              <div><strong>Accepted Qty:</strong> {rrToApprove.totalAcceptedQuantityX1000 / 1000}</div>
            </div>
          )}
          <div>
            <label className="block text-xs font-medium text-slate-700">Exception Approval Reason / Commercial Justification</label>
            <TextInput
              value={overdeliveryReason}
              onChange={(e) => setOverdeliveryReason(e.target.value)}
              placeholder="e.g., Supplier bulk packaging tolerance accepted"
            />
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setOverdeliveryModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="secondary"
              onClick={async () => {
                if (!rrToApprove) return;
                try {
                  await approveOverdelivery(rrToApprove.id, overdeliveryReason);
                  notify.success("Overdelivery approved! Receiving report posted to inventory.");
                  setOverdeliveryModalOpen(false);
                  refreshAll();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Approve & Post Exception
            </Button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}

// Subcomponent for Quotation view
function QuotationListView({
  requisitionId,
  requisition,
  suppliers,
  onRefresh,
}: {
  requisitionId: string;
  requisition?: PurchaseRequisition;
  suppliers?: LpiSupplier[];
  onRefresh: () => void;
}) {
  const { notify } = useNotifications();
  const session = useAppSession();
  const canManagePo = hasPermission(session.data, Permissions.ProcurementManagePurchaseOrder);
  const quotations = useQuery({
    queryKey: ["lpi-quotations", requisitionId],
    queryFn: () => fetchQuotations(requisitionId),
  });

  const safeSuppliers: LpiSupplier[] = suppliers ?? [];
  const [recordModalOpen, setRecordModalOpen] = useState(false);
  const [quoteForm, setQuoteForm] = useState({
    supplierId: safeSuppliers[0]?.id || "",
    requisitionLineId: requisition?.items[0]?.id || "",
    unitCost: "45.00",
    minQty: "1",
    leadTime: "5",
    terms: "30",
    isSelected: true,
    selectionReason: "Best value and delivery lead time",
  });

  return (
    <Card>
      <CardHeader className="flex justify-between items-center">
        <CardTitle>Received Supplier Quotations</CardTitle>
        {canManagePo && (
          <Button variant="secondary" onClick={() => {
            setQuoteForm({
              supplierId: safeSuppliers[0]?.id || "",
              requisitionLineId: requisition?.items[0]?.id || "",
              unitCost: "45.00",
              minQty: "1",
              leadTime: "5",
              terms: "30",
              isSelected: true,
              selectionReason: "Best value and delivery lead time",
            });
            setRecordModalOpen(true);
          }}>
            + Record Quotation
          </Button>
        )}
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
                <th className="p-3 text-right">Unit Price</th>
                <th className="p-3 text-right">Min Qty</th>
                <th className="p-3 text-right">Quote Extended Total</th>
                <th className="p-3">Status</th>
                <th className="p-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {(quotations.data ?? []).length === 0 ? (
                <tr>
                  <td colSpan={10} className="p-4 text-center text-slate-500">
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
                    <td className="p-3 text-right font-medium">{money(q.quotedUnitCostCents)}</td>
                    <td className="p-3 text-right font-medium">{(q.minimumOrderQtyX1000 / 1000).toLocaleString()}</td>
                    <td className="p-3 text-right font-bold">{money(q.totalQuotedCostCents)}</td>
                    <td className="p-3">
                      <StatusPill tone={q.isWinner ? "success" : "neutral"}>
                        {q.isWinner ? "🏆 Selected Winner" : q.status}
                      </StatusPill>
                    </td>
                    <td className="p-3 text-right">
                      {!q.isWinner && canManagePo && (
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
      <Dialog
        open={recordModalOpen}
        onClose={() => setRecordModalOpen(false)}
        title="Record Supplier Quotation"
      >
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Supplier</label>
              <Select
                value={quoteForm.supplierId}
                onChange={(e) => setQuoteForm({ ...quoteForm, supplierId: e.target.value })}
              >
                <option value="">Select Supplier...</option>
                {safeSuppliers.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name} ({s.code})
                  </option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Requisition Item</label>
              <Select
                value={quoteForm.requisitionLineId}
                onChange={(e) => setQuoteForm({ ...quoteForm, requisitionLineId: e.target.value })}
              >
                <option value="">Select PR Line...</option>
                {(requisition?.items ?? []).map((it) => (
                  <option key={it.id} value={it.id}>
                    {it.productName || it.notes || "Item"} (Req: {(it.requestedQuantityX1000 / 1000).toLocaleString()})
                  </option>
                ))}
              </Select>
            </div>
          </div>
          <div className="grid grid-cols-3 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700">Quoted Unit Price (₱)</label>
              <TextInput
                type="number"
                step="0.01"
                value={quoteForm.unitCost}
                onChange={(e) => setQuoteForm({ ...quoteForm, unitCost: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Lead Time (Days)</label>
              <TextInput
                type="number"
                value={quoteForm.leadTime}
                onChange={(e) => setQuoteForm({ ...quoteForm, leadTime: e.target.value })}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-700">Payment Terms (Days)</label>
              <TextInput
                type="number"
                value={quoteForm.terms}
                onChange={(e) => setQuoteForm({ ...quoteForm, terms: e.target.value })}
              />
            </div>
          </div>
          <div>
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={quoteForm.isSelected}
                onChange={(e) => setQuoteForm({ ...quoteForm, isSelected: e.target.checked })}
              />
              Select as winning quote immediately
            </label>
          </div>
          {quoteForm.isSelected && (
            <div>
              <label className="block text-xs font-medium text-slate-700">Selection Justification Reason</label>
              <TextInput
                value={quoteForm.selectionReason}
                onChange={(e) => setQuoteForm({ ...quoteForm, selectionReason: e.target.value })}
              />
            </div>
          )}
          <div className="flex justify-end gap-2 pt-4 border-t">
            <Button variant="secondary" onClick={() => setRecordModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={async () => {
                if (!quoteForm.supplierId) {
                  notify.error("Please select a supplier.");
                  return;
                }
                const selectedLine = (requisition?.items ?? []).find((it) => it.id === quoteForm.requisitionLineId);
                try {
                  await createQuotation({
                    requisitionId,
                    requisitionLineId: quoteForm.requisitionLineId || undefined,
                    supplierId: quoteForm.supplierId,
                    productId: selectedLine?.productId,
                    itemDescription: selectedLine?.productName || selectedLine?.notes || "Quoted Goods",
                    quotedUnitCostCents: Math.round(parseFloat(quoteForm.unitCost || "0") * 100),
                    minimumOrderQtyX1000: Math.round(parseFloat(quoteForm.minQty || "1") * 1000),
                    leadTimeDays: parseInt(quoteForm.leadTime || "0", 10),
                    paymentTermsDays: parseInt(quoteForm.terms || "0", 10),
                    isSelected: quoteForm.isSelected,
                    selectionReason: quoteForm.isSelected ? quoteForm.selectionReason : undefined,
                  });
                  notify.success("Supplier quotation recorded successfully.");
                  setRecordModalOpen(false);
                  quotations.refetch();
                  onRefresh();
                } catch (e) {
                  notify.error((e as Error).message);
                }
              }}
            >
              Save Quotation
            </Button>
          </div>
        </div>
      </Dialog>
    </Card>
  );
}
