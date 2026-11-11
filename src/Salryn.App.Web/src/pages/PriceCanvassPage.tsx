import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchCustomerQuotations,
  createCustomerQuotation,
  updateCustomerQuotationStatus,
  type CustomerQuotation,
  type CreateCustomerQuotationLineRequest,
} from "../api/customerOrdersAndQuotes";
import { fetchProducts } from "../api/products";
import { fetchStoreSettings } from "../api/storeSettings";
import { PrintableCustomerQuotationModal } from "../components/documents/PrintableCustomerQuotationModal";
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

type DraftLine = {
  productId: string;
  productName: string;
  barcode: string | null;
  unitName: string;
  quantity: number;
  unitPriceCents: number;
  discountCents: number;
};

export function PriceCanvassPage() {
  const queryClient = useQueryClient();
  const { notify } = useNotifications();

  // Queries
  const { data: quotations = [], isLoading, error } = useQuery({
    queryKey: ["customer-quotations"],
    queryFn: fetchCustomerQuotations,
  });

  const { data: productsData } = useQuery({
    queryKey: ["products-for-canvass"],
    queryFn: () => fetchProducts({ pageSize: 250 }),
  });

  const { data: storeSettings } = useQuery({
    queryKey: ["store-settings"],
    queryFn: fetchStoreSettings,
  });

  const products = productsData?.items ?? [];

  // Modals state
  const [isNewModalOpen, setIsNewModalOpen] = useState(false);
  const [viewingQuote, setViewingQuote] = useState<CustomerQuotation | null>(null);

  // New quote form state
  const [customerName, setCustomerName] = useState("");
  const [customerContact, setCustomerContact] = useState("");
  const [notes, setNotes] = useState("");
  const [validUntilDays, setValidUntilDays] = useState("14");
  const [draftLines, setDraftLines] = useState<DraftLine[]>([]);

  // Current line item input
  const [selectedProductId, setSelectedProductId] = useState("");
  const [lineQty, setLineQty] = useState("1");
  const [linePrice, setLinePrice] = useState("");
  const [lineDiscount, setLineDiscount] = useState("0");

  const resetForm = () => {
    setCustomerName("");
    setCustomerContact("");
    setNotes("");
    setValidUntilDays("14");
    setDraftLines([]);
    setSelectedProductId("");
    setLineQty("1");
    setLinePrice("");
    setLineDiscount("0");
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
    const discountCents = Math.round((parseFloat(lineDiscount) || 0) * 100);

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
          discountCents,
        },
      ]);
    }

    // Reset line fields
    setSelectedProductId("");
    setLineQty("1");
    setLinePrice("");
    setLineDiscount("0");
  };

  const handleRemoveLine = (index: number) => {
    setDraftLines(draftLines.filter((_, i) => i !== index));
  };

  const subtotalCents = draftLines.reduce((acc, line) => {
    const lineTotal = Math.max(0, Math.round(line.quantity * line.unitPriceCents) - line.discountCents);
    return acc + lineTotal;
  }, 0);

  const handleCreateQuotation = async () => {
    if (!customerName.trim()) {
      notify.error("Customer Name Required", "Please enter customer or company name.");
      return;
    }
    if (draftLines.length === 0) {
      notify.error("Lines Required", "Please add at least one product line item.");
      return;
    }

    try {
      const days = parseInt(validUntilDays) || 14;
      const validUntil = new Date(Date.now() + days * 86400 * 1000).toISOString();

      const lines: CreateCustomerQuotationLineRequest[] = draftLines.map((l) => ({
        productId: l.productId,
        productNameSnapshot: l.productName,
        barcodeSnapshot: l.barcode,
        unitNameSnapshot: l.unitName,
        quantityX1000: Math.round(l.quantity * 1000),
        unitPriceCents: l.unitPriceCents,
        discountCents: l.discountCents,
      }));

      const created = await createCustomerQuotation({
        customerName: customerName.trim(),
        customerContact: customerContact.trim() || undefined,
        notes: notes.trim() || undefined,
        discountCents: 0,
        validUntil,
        lines,
      });

      queryClient.invalidateQueries({ queryKey: ["customer-quotations"] });
      notify.success("Quotation Created", `Quotation ${created.quoteNumber} recorded successfully.`);
      setIsNewModalOpen(false);
      resetForm();
      setViewingQuote(created);
    } catch (err) {
      notify.error("Failed to Create Quotation", safeNotificationDescription(err, "Could not save quotation."));
    }
  };

  const handleStatusUpdate = async (id: string, newStatus: string) => {
    try {
      await updateCustomerQuotationStatus(id, newStatus);
      queryClient.invalidateQueries({ queryKey: ["customer-quotations"] });
      notify.success("Status Updated", `Quotation marked as ${newStatus}.`);
    } catch (err) {
      notify.error("Status Update Failed", safeNotificationDescription(err, "Could not update status."));
    }
  };

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 border-b border-slate-200 pb-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Price Canvass / Quotations</h1>
          <p className="text-sm text-slate-500">
            Generate formal commercial price quotations without mutating inventory, opening shifts, or affecting financial ledgers.
          </p>
        </div>
        <Button variant="primary" onClick={() => { resetForm(); setIsNewModalOpen(true); }}>
          + New Quotation
        </Button>
      </div>

      {/* Main Quotations Card */}
      <Card>
        <CardHeader>
          <CardTitle>Issued Quotations ({quotations.length})</CardTitle>
        </CardHeader>
        <CardBody className="p-0">
          {isLoading ? (
            <div className="p-8"><LoadingState label="Loading quotations..." /></div>
          ) : error ? (
            <div className="p-8"><p className="text-rose-600 text-sm">Failed to load quotations.</p></div>
          ) : quotations.length === 0 ? (
            <div className="p-8">
              <EmptyState
                title="No Quotations Yet"
                description="Create a price quotation to provide formal pricing estimates to prospective customers."
              />
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm text-slate-700">
                <thead className="bg-slate-50 border-b border-slate-200 text-xs uppercase font-semibold text-slate-600">
                  <tr>
                    <th className="px-4 py-3">Quote #</th>
                    <th className="px-4 py-3">Date</th>
                    <th className="px-4 py-3">Customer</th>
                    <th className="px-4 py-3">Valid Until</th>
                    <th className="px-4 py-3 text-right">Items</th>
                    <th className="px-4 py-3 text-right">Total</th>
                    <th className="px-4 py-3 text-center">Status</th>
                    <th className="px-4 py-3 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {quotations.map((q) => (
                    <tr key={q.id} className="hover:bg-slate-50/80 transition-colors">
                      <td className="px-4 py-3 font-mono font-medium text-slate-900">{q.quoteNumber}</td>
                      <td className="px-4 py-3 text-xs text-slate-500">{formatDateTime(q.createdAt)}</td>
                      <td className="px-4 py-3">
                        <div className="font-medium text-slate-900">{q.customerName}</div>
                        {q.customerContact && <div className="text-xs text-slate-500">{q.customerContact}</div>}
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-500">
                        {q.validUntil ? formatDateTime(q.validUntil) : "—"}
                      </td>
                      <td className="px-4 py-3 text-right text-xs text-slate-600">
                        {q.lines.length} {q.lines.length === 1 ? "item" : "items"}
                      </td>
                      <td className="px-4 py-3 text-right font-semibold text-slate-900">
                        {formatMoney(q.totalCents / 100)}
                      </td>
                      <td className="px-4 py-3 text-center">
                        <StatusPill
                          tone={
                            q.status === "Converted"
                              ? "success"
                              : q.status === "Expired"
                              ? "danger"
                              : "primary"
                          }
                        >
                          {q.status}
                        </StatusPill>
                      </td>
                      <td className="px-4 py-3 text-right space-x-2">
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() => setViewingQuote(q)}
                        >
                          🖨️ View & Print
                        </Button>
                        {q.status === "Issued" && (
                          <>
                            <Button
                              variant="secondary"
                              size="sm"
                              onClick={() => handleStatusUpdate(q.id, "Converted")}
                            >
                              ✓ Converted
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => handleStatusUpdate(q.id, "Expired")}
                            >
                              Mark Expired
                            </Button>
                          </>
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

      {/* New Quotation Dialog */}
      <Dialog
        open={isNewModalOpen}
        onClose={() => setIsNewModalOpen(false)}
        title="Create Customer Price Quotation"
      >
        <div className="space-y-6 max-h-[80vh] overflow-y-auto pr-1">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <FormField label="Customer / Company Name *">
              <TextInput
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
                placeholder="e.g. Commercial Client / Walk-in Buyer"
              />
            </FormField>
            <FormField label="Contact Info (Phone / Email)">
              <TextInput
                value={customerContact}
                onChange={(e) => setCustomerContact(e.target.value)}
                placeholder="e.g. 0917-123-4567"
              />
            </FormField>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <FormField label="Validity Period">
              <Select
                value={validUntilDays}
                onChange={(e) => setValidUntilDays(e.target.value)}
              >
                <option value="7">7 Days</option>
                <option value="14">14 Days (Standard)</option>
                <option value="30">30 Days</option>
                <option value="60">60 Days</option>
              </Select>
            </FormField>
            <FormField label="Notes / Special Terms">
              <TextInput
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="e.g. Prices valid while supplies last. Cash on delivery."
              />
            </FormField>
          </div>

          {/* Add Line Items Section */}
          <div className="border-t border-slate-200 pt-4 space-y-3">
            <h3 className="font-semibold text-sm text-slate-800">Add Product Line</h3>
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
                <FormField label="Unit Price (₱)">
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
                    const lineTotal = Math.max(0, Math.round(line.quantity * line.unitPriceCents) - line.discountCents);
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
                    <td colSpan={3} className="p-2 text-right">Quotation Total:</td>
                    <td className="p-2 text-right font-bold text-sm text-emerald-700">
                      {formatMoney(subtotalCents / 100)}
                    </td>
                    <td></td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}

          {/* Action Buttons */}
          <div className="flex justify-end gap-3 pt-4 border-t border-slate-200">
            <Button variant="secondary" onClick={() => setIsNewModalOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              disabled={!customerName.trim() || draftLines.length === 0}
              onClick={handleCreateQuotation}
            >
              Generate & Save Quotation
            </Button>
          </div>
        </div>
      </Dialog>

      {/* Printable Document Modal */}
      <PrintableCustomerQuotationModal
        open={Boolean(viewingQuote)}
        onClose={() => setViewingQuote(null)}
        quotation={viewingQuote}
        storeSettings={storeSettings}
      />
    </div>
  );
}
