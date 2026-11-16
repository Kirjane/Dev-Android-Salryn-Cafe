import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchCustomerQuotations,
  fetchCustomerQuotation,
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
  EmptyState,
  LoadingState,
  StatusPill,
  useNotifications,
} from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";
import { safeNotificationDescription } from "../utils/notificationError";
import { localDateAfterDays } from "../utils/customerDocumentDates";
import {
  customerQuotationDisplayItemCount,
  customerQuotationIntegrityIssue,
} from "../utils/customerQuotationIntegrity";

type DraftLine = {
  lineKey: string;
  productId: string;
  productName: string;
  barcode: string | null;
  unitName: string;
  quantity: number;
  unitPriceCents: number;
  discountCents: number;
};

const computeGrossLineCents = (quantity: number, unitPriceCents: number): number => {
  if (!Number.isFinite(quantity) || !Number.isFinite(unitPriceCents)) return 0;
  return Math.floor((Math.round(quantity * 1000) * unitPriceCents + 500) / 1000);
};

export function PriceCanvassPage() {
  const queryClient = useQueryClient();
  const { notify } = useNotifications();

  const [isNewModalOpen, setIsNewModalOpen] = useState(false);
  const [productSearch, setProductSearch] = useState("");
  const [productPage, setProductPage] = useState(1);
  const [savingQuote, setSavingQuote] = useState(false);

  // Queries
  const { data: quotations = [], isLoading, error } = useQuery({
    queryKey: ["customer-quotations"],
    queryFn: fetchCustomerQuotations,
  });

  const { data: productsData, isFetching: productsLoading } = useQuery({
    queryKey: ["products-for-canvass", productSearch, productPage],
    queryFn: () => fetchProducts({ search: productSearch, page: productPage, pageSize: 25, isActive: true }),
    enabled: isNewModalOpen,
  });

  const { data: storeSettings } = useQuery({
    queryKey: ["store-settings"],
    queryFn: fetchStoreSettings,
  });

  const products = productsData?.items ?? [];

  // Modals state
  const [viewingQuote, setViewingQuote] = useState<CustomerQuotation | null>(null);
  const [loadingQuoteId, setLoadingQuoteId] = useState<string | null>(null);

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

    const qty = Number(lineQty);
    const qtyX1000 = Math.round(qty * 1000);
    const priceCents = Math.round(Number(linePrice) * 100);
    const discountCents = Math.round(Number(lineDiscount) * 100);
    const grossCents = computeGrossLineCents(qty, priceCents);
    if (
      !Number.isFinite(qty) ||
      qty <= 0 ||
      !Number.isSafeInteger(qtyX1000) ||
      qtyX1000 <= 0 ||
      !Number.isSafeInteger(priceCents) ||
      priceCents < 0 ||
      !Number.isSafeInteger(discountCents) ||
      discountCents < 0 ||
      discountCents > grossCents
    ) {
      notify.error("Enter a positive quantity and valid price/discount values.");
      return;
    }

    // Keep same-SKU rows separate when unit price or per-line discount terms differ (#51).
    const existingIndex = draftLines.findIndex(
      (l) => l.productId === prod.id && l.unitPriceCents === priceCents && l.discountCents === 0 && discountCents === 0,
    );
    if (existingIndex >= 0) {
      const updated = [...draftLines];
      updated[existingIndex] = {
        ...updated[existingIndex],
        quantity: updated[existingIndex].quantity + qty,
      };
      setDraftLines(updated);
    } else {
      const lineKey =
        typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
          ? crypto.randomUUID()
          : `${prod.id}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
      setDraftLines([
        ...draftLines,
        {
          lineKey,
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
    const lineTotal = Math.max(0, computeGrossLineCents(line.quantity, line.unitPriceCents) - line.discountCents);
    return acc + lineTotal;
  }, 0);

  const handleOpenQuotationPrint = async (quoteId: string) => {
    setLoadingQuoteId(quoteId);
    try {
      const detail = await fetchCustomerQuotation(quoteId);
      if (detail.id !== quoteId) {
        throw new Error("Fetched quotation ID does not match the requested quotation.");
      }
      const issue = customerQuotationIntegrityIssue(detail);
      if (issue) {
        notify.error("Quotation Lines Require Review", issue);
        return;
      }
      setViewingQuote(detail);
    } catch (err) {
      notify.error(
        "Cannot Load Quotation",
        safeNotificationDescription(err, "Quotation detail could not be verified."),
      );
    } finally {
      setLoadingQuoteId(null);
    }
  };

  const handleCreateQuotation = async () => {
    if (!customerName.trim()) {
      notify.error("Customer Name Required", "Please enter customer or company name.");
      return;
    }
    if (draftLines.length === 0 || draftLines.some((line) =>
      !Number.isFinite(line.quantity) || line.quantity <= 0 ||
      !Number.isSafeInteger(Math.round(line.quantity * 1000)) ||
      Math.round(line.quantity * 1000) <= 0 ||
      !Number.isSafeInteger(line.unitPriceCents) || line.unitPriceCents < 0 ||
      !Number.isSafeInteger(line.discountCents) || line.discountCents < 0 ||
      line.discountCents > computeGrossLineCents(line.quantity, line.unitPriceCents)
    )) {
      notify.error("Valid Lines Required", "Each quote line needs a positive quantity and valid price/discount.");
      return;
    }

    setSavingQuote(true);
    try {
      const days = parseInt(validUntilDays) || 14;
      const validUntil = localDateAfterDays(days);

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
      void handleOpenQuotationPrint(created.id);
    } catch (err) {
      notify.error("Failed to Create Quotation", safeNotificationDescription(err, "Could not save quotation."));
    } finally { setSavingQuote(false); }
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
                  {quotations.map((q) => {
                    const displayCount = customerQuotationDisplayItemCount(q);
                    return (
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
                        {displayCount !== null ? (
                          `${displayCount} ${displayCount === 1 ? "item" : "items"}`
                        ) : (
                          <span className="font-semibold text-amber-700">Verify line data</span>
                        )}
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
                          disabled={loadingQuoteId === q.id}
                          onClick={() => handleOpenQuotationPrint(q.id)}
                        >
                          {loadingQuoteId === q.id ? "Loading..." : "🖨️ View & Print"}
                        </Button>
                        {q.status === "Issued" && (
                          <>
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
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </CardBody>
      </Card>

      {/* Quotation workstation: selection, draft quote and customer terms only.
          Deliberately never invokes POS checkout, shift, stock or sale endpoints. */}
      {isNewModalOpen && (
        <div role="dialog" aria-modal="true" aria-label="New customer quotation" className="fixed inset-0 z-[70] overflow-y-auto bg-slate-950/80 p-3 sm:p-6">
          <div className="mx-auto flex min-h-[90vh] w-full max-w-[1500px] flex-col overflow-hidden rounded-xl bg-slate-100 shadow-2xl">
            <header className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white px-5 py-4">
              <div>
                <p className="text-xs font-bold uppercase tracking-widest text-emerald-700">Quote workstation · No sale created</p>
                <h2 className="text-2xl font-bold text-slate-900">New Price Quotation</h2>
                <p className="text-sm text-slate-500">Select and price products as at Checkout; save a quotation, not a payment or sale.</p>
              </div>
              <Button variant="secondary" onClick={() => setIsNewModalOpen(false)}>Close workstation</Button>
            </header>
            <div className="grid flex-1 grid-cols-1 gap-4 p-4 lg:grid-cols-12">
              <section className="min-w-0 space-y-4 lg:col-span-7" aria-label="Product finder">
                <div className="rounded-lg border border-slate-200 bg-white p-4">
                  <h3 className="mb-3 text-base font-bold text-slate-900">Find products</h3>
                  <input
                    className="w-full rounded-md border border-slate-300 px-4 py-3 text-base focus:border-emerald-600 focus:outline-none"
                    placeholder="Search product name, SKU or scan barcode"
                    aria-label="Search products or scan barcode"
                    value={productSearch}
                    onChange={(e) => { setProductSearch(e.target.value); setProductPage(1); setSelectedProductId(""); }}
                    onKeyDown={(e) => { if (e.key === "Enter" && products.length === 1) handleProductSelect(products[0].id); }}
                    autoFocus
                  />
                  <div className="mt-3 flex items-center justify-between gap-3 text-xs text-slate-500">
                    <span>{productsLoading ? "Searching…" : (productsData?.totalCount ?? 0) + " catalog matches"}</span>
                    <span>Page {productPage} of {Math.max(1, productsData?.totalPages ?? 1)}</span>
                  </div>
                  <div className="mt-3 grid max-h-[48vh] grid-cols-1 gap-2 overflow-y-auto sm:grid-cols-2">
                    {products.map((product) => (
                      <button type="button" key={product.id}
                        className={"rounded-md border p-3 text-left transition-colors " + (selectedProductId === product.id ? "border-emerald-600 bg-emerald-50" : "border-slate-200 hover:border-slate-400 hover:bg-slate-50")}
                        onClick={() => handleProductSelect(product.id)}>
                        <p className="line-clamp-2 text-sm font-semibold text-slate-900">{product.name}</p>
                        <p className="mt-1 text-xs text-slate-500">{product.sku ?? "No SKU"} {product.barcode ? " · " + product.barcode : ""}</p>
                        <div className="mt-2 flex justify-between gap-2"><span className="text-sm font-bold text-slate-900">{formatMoney(product.sellingPrice)}</span><span className="text-xs text-slate-500">Stock {formatQuantity(product.stockOnHand)}</span></div>
                      </button>
                    ))}
                    {!productsLoading && products.length === 0 && <p className="col-span-full py-8 text-center text-sm text-slate-500">No matching products. Refine the search or scan a different barcode.</p>}
                  </div>
                  <div className="mt-3 flex justify-end gap-2">
                    <Button variant="secondary" disabled={productPage <= 1} onClick={() => { setProductPage((page) => Math.max(1, page - 1)); setSelectedProductId(""); }}>Previous</Button>
                    <Button variant="secondary" disabled={productPage >= (productsData?.totalPages ?? 1)} onClick={() => { setProductPage((page) => page + 1); setSelectedProductId(""); }}>Next</Button>
                  </div>
                </div>
                <div className="rounded-lg border border-slate-200 bg-white p-4">
                  <h3 className="mb-3 text-base font-semibold">Add selected product</h3>
                  <p className="mb-3 text-sm text-slate-600">{products.find((item) => item.id === selectedProductId)?.name ?? "Choose a product from the catalog above."}</p>
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                    <label className="text-xs font-semibold text-slate-600">Quantity<input type="number" min="0.001" step="0.001" value={lineQty} onChange={(e) => setLineQty(e.target.value)} className="mt-1 w-full rounded border px-3 py-2 text-sm" /></label>
                    <label className="text-xs font-semibold text-slate-600">Unit price (PHP)<input type="number" min="0" step="0.01" value={linePrice} onChange={(e) => setLinePrice(e.target.value)} className="mt-1 w-full rounded border px-3 py-2 text-sm" /></label>
                    <label className="text-xs font-semibold text-slate-600">Line discount (PHP)<input type="number" min="0" step="0.01" value={lineDiscount} onChange={(e) => setLineDiscount(e.target.value)} className="mt-1 w-full rounded border px-3 py-2 text-sm" /></label>
                    <div className="flex items-end"><Button disabled={!selectedProductId} onClick={handleAddLine}>Add to quote</Button></div>
                  </div>
                </div>
              </section>
              <section className="min-w-0 space-y-4 lg:col-span-5" aria-label="Quotation cart and customer">
                <div className="rounded-lg border border-slate-200 bg-white p-4">
                  <div className="mb-3 flex items-center justify-between">
                    <h3 className="text-lg font-bold text-slate-900">Quotation cart</h3>
                    <span className="rounded bg-slate-100 px-2 py-1 text-xs font-semibold text-slate-700">{draftLines.length} lines</span>
                  </div>
                  <div className="max-h-[40vh] space-y-3 overflow-y-auto">
                    {draftLines.length === 0 && <p className="rounded-md border border-dashed p-6 text-center text-sm text-slate-500">Select products to build a quotation.</p>}
                    {draftLines.map((line, index) => (
                      <div key={line.lineKey} className="rounded-md border border-slate-200 bg-slate-50 p-3">
                        <div className="flex items-start justify-between gap-2"><p className="text-sm font-semibold">{line.productName}</p><button type="button" className="text-xs font-semibold text-rose-600" onClick={() => handleRemoveLine(index)}>Remove</button></div>
                        <div className="mt-2 grid grid-cols-3 gap-2">
                          <label className="text-xs text-slate-600">Qty<input type="number" min="0.001" step="0.001" className="mt-1 w-full rounded border p-2 text-sm" value={line.quantity} onChange={(e) => setDraftLines((prev) => prev.map((item, i) => i === index ? { ...item, quantity: Number(e.target.value) } : item))} /></label>
                          <label className="text-xs text-slate-600">Unit (PHP)<input type="number" min="0" step="0.01" className="mt-1 w-full rounded border p-2 text-sm" value={line.unitPriceCents / 100} onChange={(e) => setDraftLines((prev) => prev.map((item, i) => i === index ? { ...item, unitPriceCents: Math.round(Number(e.target.value) * 100) } : item))} /></label>
                          <label className="text-xs text-slate-600">Discount (PHP)<input type="number" min="0" step="0.01" className="mt-1 w-full rounded border p-2 text-sm" value={line.discountCents / 100} onChange={(e) => setDraftLines((prev) => prev.map((item, i) => i === index ? { ...item, discountCents: Math.round(Number(e.target.value) * 100) } : item))} /></label>
                        </div>
                        <p className="mt-2 text-right text-sm font-semibold">{formatMoney(Math.max(0, computeGrossLineCents(line.quantity, line.unitPriceCents) - line.discountCents) / 100)}</p>
                      </div>
                    ))}
                  </div>
                  <div className="mt-4 flex items-center justify-between border-t pt-4"><span className="font-semibold">Quotation total</span><span className="text-xl font-bold text-emerald-700">{formatMoney(subtotalCents / 100)}</span></div>
                </div>
                <div className="rounded-lg border border-slate-200 bg-white p-4">
                  <h3 className="mb-3 text-base font-semibold">Customer &amp; terms</h3>
                  <div className="space-y-3">
                    <label className="block text-xs font-semibold text-slate-600">Customer / Company *<input value={customerName} onChange={(e) => setCustomerName(e.target.value)} className="mt-1 w-full rounded border p-2 text-sm" placeholder="Customer or organization" /></label>
                    <label className="block text-xs font-semibold text-slate-600">Phone / Email<input value={customerContact} onChange={(e) => setCustomerContact(e.target.value)} className="mt-1 w-full rounded border p-2 text-sm" /></label>
                    <label className="block text-xs font-semibold text-slate-600">Valid for<select value={validUntilDays} onChange={(e) => setValidUntilDays(e.target.value)} className="mt-1 w-full rounded border p-2 text-sm"><option value="7">7 days</option><option value="14">14 days</option><option value="30">30 days</option><option value="60">60 days</option></select></label>
                    <label className="block text-xs font-semibold text-slate-600">Notes / terms<textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} className="mt-1 w-full rounded border p-2 text-sm" /></label>
                  </div>
                </div>
                <div className="rounded-lg border border-slate-200 bg-white p-4">
                  <p className="mb-3 text-xs text-slate-600">Saving generates a quotation document only. It does not reserve stock, create a POS sale or collect payment.</p>
                  <div className="flex gap-2"><Button variant="secondary" onClick={() => setIsNewModalOpen(false)}>Cancel</Button><Button disabled={savingQuote || !customerName.trim() || draftLines.length === 0} onClick={() => void handleCreateQuotation()}>{savingQuote ? "Saving…" : "Save & print quotation"}</Button></div>
                </div>
              </section>
            </div>
          </div>
        </div>
      )}

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
