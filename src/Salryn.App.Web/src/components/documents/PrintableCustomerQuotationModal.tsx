import { Dialog, Button } from "../ui";
import type { CustomerQuotation } from "../../api/customerOrdersAndQuotes";
import type { StoreSettings } from "../../api/storeSettings";
import { formatMoney } from "../../utils/format";

interface Props {
  open: boolean;
  onClose: () => void;
  quotation: CustomerQuotation | null;
  storeSettings?: StoreSettings | null;
}

const money = (cents: number) => formatMoney(cents / 100);

export function PrintableCustomerQuotationModal({ open, onClose, quotation, storeSettings }: Props) {
  if (!quotation) return null;

  const handlePrint = () => {
    window.print();
  };

  return (
    <Dialog open={open} onClose={onClose} title={`Price Quotation: ${quotation.quoteNumber}`}>
      <div className="space-y-6 text-slate-800">
        <div className="flex justify-between items-center border-b pb-3 print:hidden">
          <span className="text-xs text-slate-500">Commercial Price Canvass Document</span>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
            <Button variant="primary" onClick={handlePrint}>
              🖨️ Print Quotation
            </Button>
          </div>
        </div>

        {/* Printable Document Body */}
        <div className="p-6 bg-white border rounded shadow-sm print:p-0 print:border-none print:shadow-none space-y-6">
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
              <h2 className="text-lg font-bold text-slate-900">PRICE QUOTATION / CANVASS</h2>
              <span className="inline-block mt-1 px-2 py-0.5 text-xs font-semibold rounded bg-amber-100 text-amber-800">
                PRICE ESTIMATE ONLY — NOT AN OFFICIAL RECEIPT / NOT A SALE
              </span>
            </div>
            <div className="text-right text-sm space-y-1">
              <div>
                <span className="text-slate-500 text-xs">Quotation #: </span>
                <span className="font-mono font-bold text-slate-900">{quotation.quoteNumber}</span>
              </div>
              <div>
                <span className="text-slate-500 text-xs">Date Prepared: </span>
                <span className="font-medium">{new Date(quotation.createdAt).toLocaleDateString()}</span>
              </div>
              {quotation.validUntil && (
                <div>
                  <span className="text-slate-500 text-xs">Valid Until: </span>
                  <span className="font-medium text-amber-700">{quotation.validUntil}</span>
                </div>
              )}
            </div>
          </div>

          {/* Customer & Prepared By Info */}
          <div className="grid grid-cols-2 gap-4 border rounded p-3 bg-slate-50/50 text-xs">
            <div>
              <span className="text-slate-400 font-bold uppercase tracking-wider">Quotation For (Client)</span>
              <p className="font-bold text-sm text-slate-900 mt-1">{quotation.customerName}</p>
              {quotation.customerContact && (
                <p className="text-slate-600">Contact / Phone: {quotation.customerContact}</p>
              )}
            </div>
            <div className="text-right">
              <span className="text-slate-400 font-bold uppercase tracking-wider">Prepared By (Staff)</span>
              <p className="font-medium text-slate-900 mt-1">{quotation.createdByUserName}</p>
              <p className="text-slate-500">Status: {quotation.status}</p>
            </div>
          </div>

          {/* Notes if any */}
          {quotation.notes && (
            <div className="text-xs bg-slate-50 p-2.5 rounded border border-slate-200">
              <span className="font-semibold text-slate-600">Remarks / Terms: </span>
              <span className="text-slate-700">{quotation.notes}</span>
            </div>
          )}

          {/* Quoted Items Table */}
          <div className="border rounded overflow-hidden">
            <table className="w-full text-xs text-left">
              <thead className="bg-slate-100 text-slate-600 font-semibold border-b">
                <tr>
                  <th className="p-2.5 w-10 text-center">#</th>
                  <th className="p-2.5">Item Description</th>
                  <th className="p-2.5 w-24">Barcode</th>
                  <th className="p-2.5 w-20 text-right">Qty</th>
                  <th className="p-2.5 w-24 text-right">Unit Price</th>
                  <th className="p-2.5 w-20 text-right">Disc</th>
                  <th className="p-2.5 w-28 text-right">Total</th>
                </tr>
              </thead>
              <tbody className="divide-y text-slate-700">
                {quotation.lines.map((line, idx) => (
                  <tr key={line.id} className="hover:bg-slate-50">
                    <td className="p-2.5 text-center text-slate-400">{idx + 1}</td>
                    <td className="p-2.5 font-medium text-slate-900">{line.productNameSnapshot}</td>
                    <td className="p-2.5 font-mono text-slate-500">{line.barcodeSnapshot || "—"}</td>
                    <td className="p-2.5 text-right font-medium">
                      {(line.quantityX1000 / 1000).toLocaleString(undefined, { maximumFractionDigits: 3 })} {line.unitNameSnapshot}
                    </td>
                    <td className="p-2.5 text-right font-mono">{money(line.unitPriceCents)}</td>
                    <td className="p-2.5 text-right font-mono">{line.discountCents > 0 ? money(line.discountCents) : "—"}</td>
                    <td className="p-2.5 text-right font-mono font-bold text-slate-900">
                      {money(line.lineTotalCents)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot className="bg-slate-50 border-t font-semibold text-slate-900">
                <tr>
                  <td colSpan={6} className="p-2.5 text-right text-slate-600">Subtotal:</td>
                  <td className="p-2.5 text-right font-mono">{money(quotation.subtotalCents)}</td>
                </tr>
                {quotation.discountCents > 0 && (
                  <tr>
                    <td colSpan={6} className="p-2.5 text-right text-emerald-700">Quotation Discount:</td>
                    <td className="p-2.5 text-right font-mono text-emerald-700">-{money(quotation.discountCents)}</td>
                  </tr>
                )}
                <tr className="border-t text-sm bg-slate-100">
                  <td colSpan={6} className="p-2.5 text-right font-bold">TOTAL ESTIMATE:</td>
                  <td className="p-2.5 text-right font-mono font-bold text-slate-950">
                    {money(quotation.totalCents)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          {/* Official Quotation Disclaimer & Signatures */}
          <div className="grid grid-cols-2 gap-6 pt-6 border-t text-xs">
            <div className="space-y-4">
              <p className="text-[11px] text-slate-500 leading-relaxed">
                This document is a price quotation and canvass estimate only. Prices and product availability are subject to change without prior notice after the stated validity date.
              </p>
              <div className="pt-6 border-b border-dashed border-slate-300 w-3/4"></div>
              <p className="text-slate-600">Client Signature / Acceptance Date</p>
            </div>
            <div className="space-y-4 text-right">
              <p className="text-[11px] text-slate-500">
                Printed via Salryn Point of Sale & Retail Management System
              </p>
              <div className="pt-6 border-b border-dashed border-slate-300 w-3/4 ml-auto"></div>
              <p className="text-slate-600">Authorized Store Representative</p>
            </div>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
