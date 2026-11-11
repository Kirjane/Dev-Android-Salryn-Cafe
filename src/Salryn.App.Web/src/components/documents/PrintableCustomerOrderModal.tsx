import { Dialog, Button } from "../ui";
import type { CustomerOrder } from "../../api/customerOrdersAndQuotes";
import type { StoreSettings } from "../../api/storeSettings";
import { formatMoney } from "../../utils/format";

interface Props {
  open: boolean;
  onClose: () => void;
  order: CustomerOrder | null;
  storeSettings?: StoreSettings | null;
}

const money = (cents: number) => formatMoney(cents / 100);

export function PrintableCustomerOrderModal({ open, onClose, order, storeSettings }: Props) {
  if (!order) return null;

  const handlePrint = () => {
    window.print();
  };

  return (
    <Dialog open={open} onClose={onClose} title={`Customer Order: ${order.orderNumber}`}>
      <div className="space-y-6 text-slate-800">
        <div className="flex justify-between items-center border-b pb-3 print:hidden">
          <span className="text-xs text-slate-500">Commercial Client Order / Booking Slip</span>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
            <Button variant="primary" onClick={handlePrint}>
              🖨️ Print Order Slip
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
              <h2 className="text-lg font-bold text-slate-900">CUSTOMER ORDER SLIP</h2>
              <span className="inline-block mt-1 px-2 py-0.5 text-xs font-semibold rounded bg-blue-100 text-blue-800">
                CLIENT RESERVATION / PRE-ORDER
              </span>
            </div>
            <div className="text-right text-sm space-y-1">
              <div>
                <span className="text-slate-500 text-xs">Order #: </span>
                <span className="font-mono font-bold text-slate-900">{order.orderNumber}</span>
              </div>
              <div>
                <span className="text-slate-500 text-xs">Order Date: </span>
                <span className="font-medium">{new Date(order.createdAt).toLocaleDateString()}</span>
              </div>
              {order.promisedDate && (
                <div>
                  <span className="text-slate-500 text-xs">Promised Date: </span>
                  <span className="font-semibold text-blue-700">{order.promisedDate}</span>
                </div>
              )}
            </div>
          </div>

          {/* Customer & Order Status Info */}
          <div className="grid grid-cols-2 gap-4 border rounded p-3 bg-slate-50/50 text-xs">
            <div>
              <span className="text-slate-400 font-bold uppercase tracking-wider">Customer Information</span>
              <p className="font-bold text-sm text-slate-900 mt-1">{order.customerName}</p>
              {order.customerPhone && (
                <p className="text-slate-600">Contact #: {order.customerPhone}</p>
              )}
            </div>
            <div className="text-right">
              <span className="text-slate-400 font-bold uppercase tracking-wider">Order Tracking</span>
              <p className="font-medium text-slate-900 mt-1">Booked by: {order.createdByUserName}</p>
              <p className="text-slate-700 font-semibold mt-0.5">Status: {order.status}</p>
              {order.fulfilledAt && (
                <p className="text-emerald-700 text-[11px]">Fulfilled on: {new Date(order.fulfilledAt).toLocaleString()}</p>
              )}
            </div>
          </div>

          {/* Special Instructions / Notes */}
          {order.notes && (
            <div className="text-xs bg-slate-50 p-2.5 rounded border border-slate-200">
              <span className="font-semibold text-slate-600">Fulfillment Instructions / Notes: </span>
              <span className="text-slate-700">{order.notes}</span>
            </div>
          )}

          {/* Ordered Items Table */}
          <div className="border rounded overflow-hidden">
            <table className="w-full text-xs text-left">
              <thead className="bg-slate-100 text-slate-600 font-semibold border-b">
                <tr>
                  <th className="p-2.5 w-10 text-center">#</th>
                  <th className="p-2.5">Ordered Product</th>
                  <th className="p-2.5 w-24">Barcode</th>
                  <th className="p-2.5 w-20 text-right">Qty</th>
                  <th className="p-2.5 w-24 text-right">Unit Price</th>
                  <th className="p-2.5 w-28 text-right">Total</th>
                </tr>
              </thead>
              <tbody className="divide-y text-slate-700">
                {order.lines.map((line, idx) => (
                  <tr key={line.id} className="hover:bg-slate-50">
                    <td className="p-2.5 text-center text-slate-400">{idx + 1}</td>
                    <td className="p-2.5 font-medium text-slate-900">{line.productNameSnapshot}</td>
                    <td className="p-2.5 font-mono text-slate-500">{line.barcodeSnapshot || "—"}</td>
                    <td className="p-2.5 text-right font-medium">
                      {(line.quantityX1000 / 1000).toLocaleString(undefined, { maximumFractionDigits: 3 })} {line.unitNameSnapshot}
                    </td>
                    <td className="p-2.5 text-right font-mono">{money(line.unitPriceCents)}</td>
                    <td className="p-2.5 text-right font-mono font-bold text-slate-900">
                      {money(line.lineTotalCents)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot className="bg-slate-100 border-t font-semibold text-slate-900">
                <tr className="text-sm">
                  <td colSpan={5} className="p-2.5 text-right font-bold">TOTAL ORDER VALUE:</td>
                  <td className="p-2.5 text-right font-mono font-bold text-slate-950">
                    {money(order.totalCents)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          {/* Signatures & Pickup/Delivery Confirmation */}
          <div className="grid grid-cols-2 gap-6 pt-6 border-t text-xs">
            <div className="space-y-4">
              <p className="text-[11px] text-slate-500 leading-relaxed">
                Please present this order slip upon claiming or payment at the store counter.
              </p>
              <div className="pt-6 border-b border-dashed border-slate-300 w-3/4"></div>
              <p className="text-slate-600">Customer Received By / Date</p>
            </div>
            <div className="space-y-4 text-right">
              <p className="text-[11px] text-slate-500">
                Salryn POS & Customer Order Fulfillment
              </p>
              <div className="pt-6 border-b border-dashed border-slate-300 w-3/4 ml-auto"></div>
              <p className="text-slate-600">Store Staff / Releaser</p>
            </div>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
