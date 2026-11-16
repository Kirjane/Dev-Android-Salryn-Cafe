import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchCustomerOrders,
  fetchCustomerOrder,
  createCustomerOrder,
  updateCustomerOrderStatus,
  fulfillCustomerOrder,
  type CustomerOrder,
  type CreateCustomerOrderLineRequest,
} from "../api/customerOrdersAndQuotes";
import { fetchProducts } from "../api/products";
import { fetchReceivableCustomers } from "../api/receivables";
import { fetchStoreSettings } from "../api/storeSettings";
import { PrintableCustomerOrderModal } from "../components/documents/PrintableCustomerOrderModal";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  Dialog,
  EmptyState,
  FormField,
  LoadingState,
  Select,
  StatusPill,
  TextInput,
  useNotifications,
} from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";
import { safeNotificationDescription } from "../utils/notificationError";
import { customerOrderDisplayItemCount, customerOrderIntegrityIssue } from "../utils/customerOrderIntegrity";

type DraftOrderLine = {
  productId: string;
  productName: string;
  barcode: string | null;
  unitName: string;
  quantity: number;
  unitPriceCents: number;
};

export function CustomerOrdersPage() {
  const queryClient = useQueryClient();
  const { notify } = useNotifications();

  // Filters
  const [statusFilter, setStatusFilter] = useState<string>("All");

  // Queries
  const { data: allOrders = [], isLoading, error } = useQuery({
    queryKey: ["customer-orders"],
    queryFn: () => fetchCustomerOrders(),
  });
  const orders = allOrders.filter((order) => statusFilter === "All" || order.status === statusFilter);
  const activeOrders = allOrders.filter((order) => ["Confirmed", "Preparing", "Ready"].includes(order.status));
  const dueOrders = activeOrders.filter((order) => order.promisedDate && order.promisedDate.slice(0, 10) < new Date().toISOString().slice(0, 10));

  const { data: productsData } = useQuery({
    queryKey: ["products-for-orders"],
    queryFn: () => fetchProducts({ pageSize: 250 }),
  });

  const { data: storeSettings } = useQuery({
    queryKey: ["store-settings"],
    queryFn: fetchStoreSettings,
  });

  const { data: customerAccounts = [] } = useQuery({
    queryKey: ["receivables-customers-for-orders"],
    queryFn: () => fetchReceivableCustomers(),
  });

  const products = productsData?.items ?? [];

  // Modals state
  const [isNewModalOpen, setIsNewModalOpen] = useState(false);
  const [viewingOrder, setViewingOrder] = useState<CustomerOrder | null>(null);
  const [loadingSlipId, setLoadingSlipId] = useState<string | null>(null);
  const [fulfillOrderTarget, setFulfillOrderTarget] = useState<CustomerOrder | null>(null);
  const [fulfillSaleId, setFulfillSaleId] = useState("");

  // New order form state
  const [selectedCustomerId, setSelectedCustomerId] = useState<string>("");
  const [customerName, setCustomerName] = useState("");
  const [customerPhone, setCustomerPhone] = useState("");
  const [promisedDate, setPromisedDate] = useState("");
  const [notes, setNotes] = useState("");
  const [draftLines, setDraftLines] = useState<DraftOrderLine[]>([]);

  // Current line item input
  const [selectedProductId, setSelectedProductId] = useState("");
  const [lineQty, setLineQty] = useState("1");
  const [linePrice, setLinePrice] = useState("");

  const resetForm = () => {
    setSelectedCustomerId("");
    setCustomerName("");
    setCustomerPhone("");
    setPromisedDate("");
    setNotes("");
    setDraftLines([]);
    setSelectedProductId("");
    setLineQty("1");
    setLinePrice("");
  };

  const handleProductSelect = (productId: string) => {
    setSelectedProductId(productId);
    const prod = products.find((p) => p.id === productId);
    if (prod) {
      setLinePrice(prod.sellingPrice.toFixed(2));
    }
  };

  const handleAddLine = () => {
    const prod = products.find((p) => p.id === selectedProductId);
    if (!prod) return;

    const qty = parseFloat(lineQty) || 1;
    const priceCents = Math.round((parseFloat(linePrice) || 0) * 100);

    const existingIndex = draftLines.findIndex((l) => l.productId === prod.id);
    if (existingIndex >= 0) {
      const updated = [...draftLines];
      updated[existingIndex].quantity += qty;
      setDraftLines(updated);
    } else {
      setDraftLines([
        ...draftLines,
        {
          productId: prod.id,
          productName: prod.name,
          barcode: prod.barcode ?? null,
          unitName: prod.unitName || "pcs",
          quantity: qty,
          unitPriceCents: priceCents,
        },
      ]);
    }

    setSelectedProductId("");
    setLineQty("1");
    setLinePrice("");
  };

  const handleRemoveLine = (index: number) => {
    setDraftLines(draftLines.filter((_, i) => i !== index));
  };

  const totalCents = draftLines.reduce((acc, l) => acc + Math.round(l.quantity * l.unitPriceCents), 0);

  const handleOpenSlip = async (orderId: string) => {
    setLoadingSlipId(orderId);
    try {
      // An order register row is not an authoritative printable document.
      // Load the actual persisted detail even after a list refresh or creation.
      const detail = await fetchCustomerOrder(orderId);
      if (detail.id !== orderId) throw new Error("Fetched order ID does not match the requested order.");
      const issue = customerOrderIntegrityIssue(detail);
      if (issue) {
        notify.error("Order Lines Require Review", issue);
        return;
      }
      setViewingOrder(detail);
    } catch (err) {
      notify.error("Cannot Load Order Slip", safeNotificationDescription(err, "Order detail could not be verified."));
    } finally {
      setLoadingSlipId(null);
    }
  };

  const handleCreateOrder = async () => {
    if (!customerName.trim()) {
      notify.error("Customer Name Required", "Please enter customer or buyer name.");
      return;
    }
    if (draftLines.length === 0) {
      notify.error("Lines Required", "Please add at least one product to the order.");
      return;
    }

    try {
      const lines: CreateCustomerOrderLineRequest[] = draftLines.map((l) => ({
        productId: l.productId,
        productNameSnapshot: l.productName,
        barcodeSnapshot: l.barcode,
        unitNameSnapshot: l.unitName,
        quantityX1000: Math.round(l.quantity * 1000),
        unitPriceCents: l.unitPriceCents,
      }));

      const created = await createCustomerOrder({
        customerId: selectedCustomerId || undefined,
        customerName: customerName.trim(),
        customerPhone: customerPhone.trim() || undefined,
        promisedDate: promisedDate || undefined,
        notes: notes.trim() || undefined,
        lines,
      });

      queryClient.invalidateQueries({ queryKey: ["customer-orders"] });
      notify.success("Order Created", `Order ${created.orderNumber} placed successfully.`);
      setIsNewModalOpen(false);
      resetForm();
      void handleOpenSlip(created.id);
    } catch (err) {
      notify.error("Failed to Create Order", safeNotificationDescription(err, "Could not save customer order."));
    }
  };

  const handleAdvanceOrder = async (id: string, nextStatus: "Preparing" | "Ready") => {
    try {
      await updateCustomerOrderStatus(id, nextStatus);
      void queryClient.invalidateQueries({ queryKey: ["customer-orders"] });
      notify.success("Order updated", `Order moved to ${nextStatus}. No sale or stock movement was created.`);
    } catch (err) {
      notify.error("Transition Failed", safeNotificationDescription(err, "Could not update order state."));
    }
  };

  const handleCancelOrder = async (id: string) => {
    if (!confirm("Are you sure you want to cancel this customer order?")) return;
    try {
      await updateCustomerOrderStatus(id, "Cancelled");
      queryClient.invalidateQueries({ queryKey: ["customer-orders"] });
      notify.info("Order Cancelled", "Customer order marked as Cancelled.");
    } catch (err) {
      notify.error("Cancellation Failed", safeNotificationDescription(err, "Could not cancel order."));
    }
  };

  const handleFulfillOrder = async () => {
    if (!fulfillOrderTarget) return;
    const saleRef = fulfillSaleId.trim();
    if (!saleRef) {
      notify.error("Sale Reference Required", "Please enter the completed POS Checkout Sale ID or Receipt Number.");
      return;
    }
    try {
      await fulfillCustomerOrder(fulfillOrderTarget.id, saleRef);
      queryClient.invalidateQueries({ queryKey: ["customer-orders"] });
      notify.success("Order Fulfilled", `Order ${fulfillOrderTarget.orderNumber} marked as Fulfilled.`);
      setFulfillOrderTarget(null);
      setFulfillSaleId("");
    } catch (err) {
      notify.error("Fulfillment Failed", safeNotificationDescription(err, "Could not fulfill order."));
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 border-b border-slate-200 pb-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Customer Orders</h1>
          <p className="text-sm text-slate-500">
            Manager order queue: Confirmed → Preparing → Ready → Fulfilled by verified POS sale. No layaway balance is implied.
          </p>
        </div>
        <div className="flex gap-2">
          <Select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="w-36 text-sm"
          >
            <option value="All">All Statuses</option>
            <option value="Confirmed">Confirmed</option>
            <option value="Preparing">Preparing</option>
            <option value="Ready">Ready</option>
            <option value="Fulfilled">Fulfilled</option>
            <option value="Cancelled">Cancelled</option>
          </Select>
          <Button variant="primary" onClick={() => { resetForm(); setIsNewModalOpen(true); }}>
            + New Order
          </Button>
        </div>
      </div>

      <section aria-label="Order lifecycle summary" className="grid grid-cols-1 gap-3 sm:grid-cols-4">
        <div className="rounded-lg border bg-white p-4"><div className="text-xs text-slate-500">Active orders</div><div className="text-2xl font-bold">{activeOrders.length}</div></div>
        <div className="rounded-lg border bg-white p-4"><div className="text-xs text-slate-500">In preparation</div><div className="text-2xl font-bold">{allOrders.filter((o) => o.status === "Preparing").length}</div></div>
        <div className="rounded-lg border bg-white p-4"><div className="text-xs text-slate-500">Ready to fulfill</div><div className="text-2xl font-bold">{allOrders.filter((o) => o.status === "Ready").length}</div></div>
        <div className="rounded-lg border bg-white p-4"><div className="text-xs text-slate-500">Past promised date</div><div className="text-2xl font-bold">{dueOrders.length}</div></div>
      </section>

      {/* Orders Table */}
      <Card>
        <CardHeader>
          <CardTitle>Customer Orders ({orders.length})</CardTitle>
        </CardHeader>
        <CardBody className="p-0">
          {isLoading ? (
            <div className="p-8"><LoadingState label="Loading orders..." /></div>
          ) : error ? (
            <div className="p-8"><p className="text-rose-600 text-sm">Failed to load customer orders.</p></div>
          ) : orders.length === 0 ? (
            <div className="p-8">
              <EmptyState
                title="No Orders Found"
                description="Create a customer order to record reservations and future deliveries."
              />
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm text-slate-700">
                <thead className="bg-slate-50 border-b border-slate-200 text-xs uppercase font-semibold text-slate-600">
                  <tr>
                    <th className="px-4 py-3">Order #</th>
                    <th className="px-4 py-3">Date</th>
                    <th className="px-4 py-3">Customer</th>
                    <th className="px-4 py-3">Promised Date</th>
                    <th className="px-4 py-3 text-right">Items</th>
                    <th className="px-4 py-3 text-right">Total</th>
                    <th className="px-4 py-3 text-center">Status</th>
                    <th className="px-4 py-3 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {orders.map((o) => (
                    <tr key={o.id} className="hover:bg-slate-50/80 transition-colors">
                      <td className="px-4 py-3 font-mono font-medium text-slate-900">{o.orderNumber}</td>
                      <td className="px-4 py-3 text-xs text-slate-500">{formatDateTime(o.createdAt)}</td>
                      <td className="px-4 py-3">
                        <div className="font-medium text-slate-900 flex items-center gap-1.5">
                          {o.customerName}
                          {o.customerId && (
                            <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-semibold bg-blue-100 text-blue-800">
                              Account
                            </span>
                          )}
                        </div>
                        {o.customerPhone && <div className="text-xs text-slate-500">{o.customerPhone}</div>}
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-500">
                        {o.promisedDate ? formatDateTime(o.promisedDate) : "—"}
                      </td>
                      <td className="px-4 py-3 text-right text-xs text-slate-600">
                        {customerOrderDisplayItemCount(o) === null
                          ? <span className="text-rose-700" title={customerOrderIntegrityIssue(o) ?? undefined}>Verify line data</span>
                          : <>{customerOrderDisplayItemCount(o)} {customerOrderDisplayItemCount(o) === 1 ? "item" : "items"}</>}
                      </td>
                      <td className="px-4 py-3 text-right font-semibold text-slate-900">
                        {formatMoney(o.totalCents / 100)}
                      </td>
                      <td className="px-4 py-3 text-center">
                        <StatusPill
                          tone={
                            o.status === "Fulfilled"
                              ? "success"
                              : o.status === "Cancelled"
                              ? "danger"
                              : "primary"
                          }
                        >
                          {o.status === "Ready" ? "Ready for fulfillment" : o.status}
                        </StatusPill>
                      </td>
                      <td className="px-4 py-3 text-right space-x-2">
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() => void handleOpenSlip(o.id)}
                          disabled={loadingSlipId !== null}
                        >
                          {loadingSlipId === o.id ? "Loading detail..." : "🖨️ Slip"}
                        </Button>
                        {o.status === "Confirmed" && customerOrderDisplayItemCount(o) !== null && (
                          <Button variant="primary" size="sm" onClick={() => void handleAdvanceOrder(o.id, "Preparing")}>Start preparation</Button>
                        )}
                        {o.status === "Preparing" && customerOrderDisplayItemCount(o) !== null && (
                          <Button variant="primary" size="sm" onClick={() => void handleAdvanceOrder(o.id, "Ready")}>Mark ready</Button>
                        )}
                        {o.status === "Ready" && customerOrderDisplayItemCount(o) !== null && (
                          <Button variant="primary" size="sm" onClick={() => { setFulfillOrderTarget(o); setFulfillSaleId(""); }}>
                            Fulfill with POS sale
                          </Button>
                        )}
                        {["Confirmed", "Preparing", "Ready"].includes(o.status) && customerOrderDisplayItemCount(o) !== null && (
                          <Button variant="ghost" size="sm" onClick={() => void handleCancelOrder(o.id)}>Cancel order</Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardBody>
      </Card>

      {/* New Order Dialog */}
      <Dialog
        open={isNewModalOpen}
        onClose={() => setIsNewModalOpen(false)}
        title="Create Customer Order"
      >
        <div className="space-y-6 max-h-[80vh] overflow-y-auto pr-1">
          <FormField label="Customer Account (Optional for Cash, Required for AR Fulfillment)">
            <Select
              value={selectedCustomerId}
              onChange={(e) => {
                const val = e.target.value;
                setSelectedCustomerId(val);
                if (val) {
                  const acc = customerAccounts.find((c) => c.customer.id === val);
                  if (acc) {
                    setCustomerName(acc.customer.fullName);
                    setCustomerPhone(acc.customer.phone || "");
                  }
                }
              }}
            >
              <option value="">-- Walk-in / Guest (No Customer Account) --</option>
              {customerAccounts.map((c) => (
                <option key={c.customer.id} value={c.customer.id}>
                  {c.customer.fullName} {c.customer.phone ? `(${c.customer.phone})` : ""} - Balance: {formatMoney(c.balanceCents / 100)}
                </option>
              ))}
            </Select>
          </FormField>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <FormField label="Customer Name *">
              <TextInput
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
                placeholder="e.g. Commercial Client / Buyer Name"
              />
            </FormField>
            <FormField label="Contact Phone">
              <TextInput
                value={customerPhone}
                onChange={(e) => setCustomerPhone(e.target.value)}
                placeholder="e.g. 0918-765-4321"
              />
            </FormField>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <FormField label="Promised Pickup / Delivery Date">
              <TextInput
                type="date"
                value={promisedDate}
                onChange={(e) => setPromisedDate(e.target.value)}
              />
            </FormField>
            <FormField label="Notes / Specifications">
              <TextInput
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="e.g. Call before delivery; requested specific brand"
              />
            </FormField>
          </div>

          {/* Add Product Line */}
          <div className="border-t border-slate-200 pt-4 space-y-3">
            <h3 className="font-semibold text-sm text-slate-800">Add Order Line</h3>
            <div className="grid grid-cols-1 sm:grid-cols-12 gap-3 items-end">
              <div className="sm:col-span-5">
                <FormField label="Product">
                  <Select
                    value={selectedProductId}
                    onChange={(e) => handleProductSelect(e.target.value)}
                  >
                    <option value="">Select a product...</option>
                    {products.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name} {p.barcode ? `(${p.barcode})` : ""} - {formatMoney(p.sellingPrice)}
                      </option>
                    ))}
                  </Select>
                </FormField>
              </div>
              <div className="sm:col-span-2">
                <FormField label="Qty">
                  <TextInput
                    type="number"
                    min="1"
                    value={lineQty}
                    onChange={(e) => setLineQty(e.target.value)}
                  />
                </FormField>
              </div>
              <div className="sm:col-span-3">
                <FormField label="Price (₱)">
                  <TextInput
                    type="number"
                    step="0.01"
                    value={linePrice}
                    onChange={(e) => setLinePrice(e.target.value)}
                  />
                </FormField>
              </div>
              <div className="sm:col-span-2">
                <Button
                  variant="primary"
                  className="w-full"
                  disabled={!selectedProductId}
                  onClick={handleAddLine}
                >
                  + Add
                </Button>
              </div>
            </div>
          </div>

          {/* Lines Table */}
          {draftLines.length > 0 && (
            <div className="border border-slate-200 rounded overflow-hidden">
              <table className="w-full text-xs text-left text-slate-700">
                <thead className="bg-slate-100 font-semibold text-slate-600">
                  <tr>
                    <th className="p-2">Product</th>
                    <th className="p-2 text-right">Qty</th>
                    <th className="p-2 text-right">Price</th>
                    <th className="p-2 text-right">Total</th>
                    <th className="p-2 text-center">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {draftLines.map((line, idx) => {
                    const lineTotal = Math.round(line.quantity * line.unitPriceCents);
                    return (
                      <tr key={idx}>
                        <td className="p-2 font-medium">{line.productName}</td>
                        <td className="p-2 text-right">{formatQuantity(line.quantity)} {line.unitName}</td>
                        <td className="p-2 text-right">{formatMoney(line.unitPriceCents / 100)}</td>
                        <td className="p-2 text-right font-semibold">{formatMoney(lineTotal / 100)}</td>
                        <td className="p-2 text-center">
                          <button
                            type="button"
                            className="text-rose-600 hover:text-rose-800 text-xs font-medium"
                            onClick={() => handleRemoveLine(idx)}
                          >
                            Remove
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
                <tfoot className="bg-slate-50 font-semibold text-slate-900 border-t border-slate-200">
                  <tr>
                    <td colSpan={3} className="p-2 text-right">Order Total:</td>
                    <td className="p-2 text-right font-bold text-sm text-emerald-700">
                      {formatMoney(totalCents / 100)}
                    </td>
                    <td></td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}

          {/* Dialog Action Buttons */}
          <div className="flex justify-end gap-3 pt-4 border-t border-slate-200">
            <Button variant="secondary" onClick={() => setIsNewModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              disabled={!customerName.trim() || draftLines.length === 0}
              onClick={handleCreateOrder}
            >
              Place Customer Order
            </Button>
          </div>
        </div>
      </Dialog>

      {/* Fulfill Order Modal */}
      <Dialog
        open={Boolean(fulfillOrderTarget)}
        onClose={() => setFulfillOrderTarget(null)}
        title={`Fulfill Order ${fulfillOrderTarget?.orderNumber}`}
      >
        <div className="space-y-4">
          <p className="text-sm text-slate-600">
            Only a completed, non-voided POS sale with matching customer, items, quantities and total can close this order. Enter its receipt number or sale ID.
          </p>
          <FormField label="Checkout Sale ID / Receipt # (Required)">
            <TextInput
              value={fulfillSaleId}
              onChange={(e) => setFulfillSaleId(e.target.value)}
              placeholder="e.g. SL-20261111-0001 or Sale GUID"
            />
          </FormField>
          <div className="flex justify-end gap-3 pt-4 border-t">
            <Button variant="secondary" onClick={() => setFulfillOrderTarget(null)}>
              Cancel
            </Button>
            <Button variant="primary" onClick={handleFulfillOrder}>
              Confirm Fulfillment
            </Button>
          </div>
        </div>
      </Dialog>

      {/* Printable Slip Modal */}
      <PrintableCustomerOrderModal
        open={Boolean(viewingOrder)}
        onClose={() => setViewingOrder(null)}
        order={viewingOrder}
        storeSettings={storeSettings}
      />
    </div>
  );
}
