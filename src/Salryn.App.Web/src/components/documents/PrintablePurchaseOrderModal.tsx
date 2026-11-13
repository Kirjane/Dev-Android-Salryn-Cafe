import { Dialog, Button } from "../ui";
import { PdfDownloadButton } from "./PdfDownloadButton";
import type { PurchaseOrder } from "../../api/procureToPay";
import type { StoreSettings } from "../../api/storeSettings";
import { formatMoney } from "../../utils/format";
import { purchaseOrderPrintIssue } from "../../utils/purchaseOrderPrint";

interface Props {
  open: boolean;
  onClose: () => void;
  po: PurchaseOrder | null;
  storeSettings?: StoreSettings | null;
}

// Do not print a fictitious amount when the backend supplied invalid financial data.
const money = (cents: number) => Number.isFinite(cents) ? formatMoney(cents / 100) : "Amount unavailable";

export function PrintablePurchaseOrderModal({ open, onClose, po, storeSettings }: Props) {
  if (!po) return null;
  const printIssue = purchaseOrderPrintIssue(po);
  const hasValidAmounts = printIssue === null;

  const handlePrint = () => {
    if (!hasValidAmounts) return;
    window.print();
  };

  return (
    <Dialog open={open} onClose={onClose} title={`Purchase Order: ${po.poNumber}`}>
      <div className="space-y-6 text-slate-800">
        <div className="flex justify-between items-center border-b pb-3 print:hidden">
          <span className="text-xs text-slate-500">Official External Vendor Purchase Order</span>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
            <PdfDownloadButton
              url={`/api/lpi/purchase-orders/${po.id}/pdf`}
              fileName={`${po.poNumber}.pdf`}
              disabled={!hasValidAmounts}
            />
            <Button variant="primary" onClick={handlePrint} disabled={!hasValidAmounts}>
              Print Purchase Order
            </Button>
          </div>
        </div>

        {/* Printable Document Body */}
        <div className={`p-6 bg-white border rounded shadow-sm print:p-0 print:border-none print:shadow-none space-y-6 ${hasValidAmounts ? "" : "print:hidden"}`}>
          {/* Company Letterhead */}
          <div className="border-b pb-4 text-center space-y-1">
            <h1 className="text-xl font-bold tracking-tight text-slate-900 uppercase">
              {storeSettings?.storeName || "SALRYN STORE"}
            </h1>
            {storeSettings?.businessName && (
              <p className="text-xs text-slate-600">{storeSettings.businessName}</p>
            )}
            {storeSettings?.address && (
              <p className="text-xs text-slate-500">{storeSettings.address}</p>
            )}
            <div className="text-xs text-slate-500 flex justify-center gap-4">
              {storeSettings?.contactNumber && <span>Tel: {storeSettings.contactNumber}</span>}
              {storeSettings?.tin && <span>TIN: {storeSettings.tin}</span>}
            </div>
          </div>

          {/* Document Header */}
          <div className="flex justify-between items-start">
            <div>
              <span className="text-xs uppercase tracking-wider text-slate-400 font-bold">Document Type</span>
              <h2 className="text-lg font-bold text-slate-900">PURCHASE ORDER</h2>
              <span className="inline-block mt-1 px-2.5 py-0.5 text-xs font-semibold rounded bg-slate-100 text-slate-800 border">
                Status: {po.status}
              </span>
            </div>
            <div className="text-right">
              <span className="text-xs uppercase tracking-wider text-slate-400 font-bold">PO Number</span>
              <p className="text-lg font-mono font-bold text-slate-900">{po.poNumber}</p>
              <p className="text-xs text-slate-500">Order Date: {po.poDate?.slice(0, 10)}</p>
              {po.expectedDeliveryDate && (
                <p className="text-xs text-slate-500">Expected Delivery: {po.expectedDeliveryDate?.slice(0, 10)}</p>
              )}
            </div>
          </div>

          {/* Vendor & Delivery Information */}
          <div className="grid grid-cols-2 gap-4 p-4 bg-slate-50 border rounded text-xs">
            <div>
              <span className="text-slate-500 font-bold uppercase block mb-1">Vendor / Supplier</span>
              <p className="text-sm font-bold text-slate-900">{po.supplierName}</p>
              <p className="text-slate-600">Code: {po.supplierCode}</p>
              <p className="text-slate-600">Payment Terms: {po.paymentTermsDays} days</p>
            </div>
            <div>
              <span className="text-slate-500 font-bold uppercase block mb-1">Ship To / Delivery Destination</span>
              <p className="text-sm font-semibold text-slate-900">
                {po.deliveryLocationName || "Main Warehouse & Store"}
              </p>
              <p className="text-slate-600">{po.deliveryAddress || storeSettings?.address || "Main Receiving Dock"}</p>
            </div>
          </div>

          {!hasValidAmounts && (
            <p role="alert" className="text-sm font-semibold text-red-700 print:hidden">
              {printIssue} Printing is disabled until the source document is corrected.
            </p>
          )}

          {/* Line Items Table */}
          <table className="w-full text-left text-xs border border-slate-200">
            <thead className="bg-slate-100 text-slate-700 uppercase font-semibold border-b">
              <tr>
                <th className="p-2 border-r w-8 text-center">#</th>
                <th className="p-2 border-r">Item Description</th>
                <th className="p-2 border-r">Code / SKU</th>
                <th className="p-2 border-r text-right">Ordered Qty</th>
                <th className="p-2 border-r">Unit</th>
                <th className="p-2 border-r text-right">Unit Price</th>
                <th className="p-2 text-right">Line Total (net + tax)</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {po.items.map((item, idx) => (
                <tr key={item.id} className="hover:bg-slate-50">
                  <td className="p-2 border-r text-center font-mono">{idx + 1}</td>
                  <td className="p-2 border-r font-medium text-slate-900">{item.productName}</td>
                  <td className="p-2 border-r font-mono text-slate-500">{item.productSku || "—"}</td>
                  <td className="p-2 border-r text-right font-mono font-semibold text-slate-900">
                    {item.orderedQuantityX1000 / 1000}
                  </td>
                  <td className="p-2 border-r text-slate-600">{item.uomName}</td>
                  <td className="p-2 border-r text-right font-mono">{money(item.unitCostCents)}</td>
                  <td className="p-2 text-right font-mono font-semibold text-slate-900">
                    {money(item.totalCostCents)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot className="bg-slate-50 font-bold border-t">
              <tr>
                <td colSpan={6} className="p-2 text-right uppercase">
                  Subtotal:
                </td>
                <td className="p-2 text-right font-mono text-slate-900">{money(po.subtotalCents)}</td>
              </tr>
              {po.discountCents > 0 && (
                <tr>
                  <td colSpan={6} className="p-2 text-right uppercase">
                    Discount:
                  </td>
                  <td className="p-2 text-right font-mono text-slate-900">−{money(po.discountCents)}</td>
                </tr>
              )}
              {po.taxCents > 0 && (
                <tr>
                  <td colSpan={6} className="p-2 text-right uppercase">
                    Tax / VAT:
                  </td>
                  <td className="p-2 text-right font-mono text-slate-900">{money(po.taxCents)}</td>
                </tr>
              )}
              {po.shippingCents > 0 && (
                <tr>
                  <td colSpan={6} className="p-2 text-right uppercase">
                    Shipping / Freight:
                  </td>
                  <td className="p-2 text-right font-mono text-slate-900">{money(po.shippingCents)}</td>
                </tr>
              )}
              <tr className="border-t">
                <td colSpan={6} className="p-2 text-right uppercase text-sm">
                  Total Order Amount:
                </td>
                <td className="p-2 text-right font-mono text-sm text-slate-900 font-bold">
                  {money(po.totalCents)}
                </td>
              </tr>
            </tfoot>
          </table>

          {po.notes && (
            <div className="p-3 border rounded text-xs bg-slate-50">
              <span className="font-semibold text-slate-700 block mb-1">Instructions / Notes:</span>
              <p className="text-slate-600">{po.notes}</p>
            </div>
          )}

          {/* Authorized Signatures Footer */}
          <div className="grid grid-cols-2 gap-8 pt-8 border-t text-xs">
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                Purchasing Officer / Store Operations
              </div>
              <span className="text-slate-500 mt-1 block">Prepared & Authorized By</span>
            </div>
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                {po.supplierName}
              </div>
              <span className="text-slate-500 mt-1 block">Supplier Confirmation / Conforme</span>
            </div>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
