import { Dialog, Button } from "../ui";
import { PdfDownloadButton } from "./PdfDownloadButton";
import type { ReceivingReport } from "../../api/procureToPay";
import type { StoreSettings } from "../../api/storeSettings";
import { formatMoney } from "../../utils/format";

interface Props {
  open: boolean;
  onClose: () => void;
  rr: ReceivingReport | null;
  storeSettings?: StoreSettings | null;
}

const money = (cents: number) => formatMoney(cents / 100);

export function PrintableReceivingReportModal({ open, onClose, rr, storeSettings }: Props) {
  if (!rr) return null;

  const handlePrint = () => {
    window.print();
  };

  return (
    <Dialog open={open} onClose={onClose} title={`Receiving Report: ${rr.rrNumber}`}>
      <div className="space-y-6 text-slate-800">
        <div className="flex justify-between items-center border-b pb-3 print:hidden">
          <span className="text-xs text-slate-500">Official Inward Goods Receiving & Inspection Record</span>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
            <PdfDownloadButton url={`/api/lpi/receiving-reports/${rr.id}/pdf`} fileName={`${rr.rrNumber}.pdf`} />
            <Button variant="primary" onClick={handlePrint}>
              Print Receiving Report
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
              <h2 className="text-lg font-bold text-slate-900">GOODS RECEIVING REPORT (RR)</h2>
              <span className="inline-block mt-1 px-2.5 py-0.5 text-xs font-semibold rounded bg-slate-100 text-slate-800 border">
                Status: {rr.status} {rr.isOverdelivery ? "(Overdelivery)" : ""}
              </span>
            </div>
            <div className="text-right">
              <span className="text-xs uppercase tracking-wider text-slate-400 font-bold">RR Number</span>
              <p className="text-lg font-mono font-bold text-slate-900">{rr.rrNumber}</p>
              <p className="text-xs text-slate-500">Date Received: {rr.receivedDate?.slice(0, 10)}</p>
              <p className="text-xs font-mono font-medium text-slate-700">Source PO: {rr.poNumber}</p>
              {rr.deliveryReceiptNumber && (
                <p className="text-xs text-slate-600 font-mono">DR #: {rr.deliveryReceiptNumber}</p>
              )}
            </div>
          </div>

          {/* Logistics & Delivery Details */}
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 p-3 bg-slate-50 border rounded text-xs">
            <div>
              <span className="text-slate-500 block">Supplier</span>
              <span className="font-semibold text-slate-800">{rr.supplierName}</span>
            </div>
            <div>
              <span className="text-slate-500 block">Carrier</span>
              <span className="font-semibold text-slate-800">{rr.carrier || "Direct Delivery"}</span>
            </div>
            <div>
              <span className="text-slate-500 block">Driver / Courier</span>
              <span className="font-semibold text-slate-800">{rr.driverName || "—"}</span>
            </div>
            <div>
              <span className="text-slate-500 block">Receiving Officer</span>
              <span className="font-semibold text-slate-800">{rr.receivedByUserName || "Receiving Clerk"}</span>
            </div>
          </div>

          {/* Received Items Table */}
          <table className="w-full text-left text-xs border border-slate-200">
            <thead className="bg-slate-100 text-slate-700 uppercase font-semibold border-b">
              <tr>
                <th className="p-2 border-r w-8 text-center">#</th>
                <th className="p-2 border-r">Item Description</th>
                <th className="p-2 border-r text-right">Delivered</th>
                <th className="p-2 border-r text-right font-bold text-emerald-800">Accepted</th>
                <th className="p-2 border-r text-right font-bold text-rose-800">Rejected</th>
                <th className="p-2 border-r">Unit</th>
                <th className="p-2 border-r">Location</th>
                <th className="p-2 border-r text-right">Unit Cost</th>
                <th className="p-2 text-right">Accepted Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rr.items.map((item, idx) => (
                <tr key={item.id} className="hover:bg-slate-50">
                  <td className="p-2 border-r text-center font-mono">{idx + 1}</td>
                  <td className="p-2 border-r font-medium text-slate-900">
                    <div>{item.productName}</div>
                    {item.rejectionReason && (
                      <div className="text-[10px] text-rose-600 italic">Rejection: {item.rejectionReason}</div>
                    )}
                  </td>
                  <td className="p-2 border-r text-right font-mono">{item.deliveredQuantityX1000 / 1000}</td>
                  <td className="p-2 border-r text-right font-mono font-semibold text-emerald-700">
                    {item.acceptedQuantityX1000 / 1000}
                  </td>
                  <td className="p-2 border-r text-right font-mono font-semibold text-rose-700">
                    {item.rejectedQuantityX1000 / 1000}
                  </td>
                  <td className="p-2 border-r text-slate-600">{item.uomName}</td>
                  <td className="p-2 border-r text-slate-600">{item.targetLocationName}</td>
                  <td className="p-2 border-r text-right font-mono">{money(item.unitCostCents)}</td>
                  <td className="p-2 text-right font-mono font-semibold text-slate-900">
                    {money(item.totalAcceptedCostCents)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot className="bg-slate-50 font-bold border-t">
              <tr>
                <td colSpan={8} className="p-2 text-right uppercase">
                  Total Accepted Stock Value:
                </td>
                <td className="p-2 text-right font-mono text-sm text-slate-900 font-bold">
                  {money(rr.totalAcceptedCostCents)}
                </td>
              </tr>
            </tfoot>
          </table>

          {rr.notes && (
            <div className="p-3 border rounded text-xs bg-slate-50">
              <span className="font-semibold text-slate-700 block mb-1">Receiving Remarks:</span>
              <p className="text-slate-600">{rr.notes}</p>
            </div>
          )}

          {/* Quality Assurance & Receiving Signatures */}
          <div className="grid grid-cols-2 gap-8 pt-8 border-t text-xs">
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                {rr.receivedByUserName || "Receiving Clerk"}
              </div>
              <span className="text-slate-500 mt-1 block">Inspected & Received By</span>
            </div>
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                Warehouse Supervisor / Store Manager
              </div>
              <span className="text-slate-500 mt-1 block">Accepted & Verified Into Stock</span>
            </div>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
