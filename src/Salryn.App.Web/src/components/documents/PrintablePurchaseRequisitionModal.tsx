import { Dialog, Button } from "../ui";
import type { PurchaseRequisition } from "../../api/procureToPay";
import type { StoreSettings } from "../../api/storeSettings";
import { formatMoney } from "../../utils/format";

interface Props {
  open: boolean;
  onClose: () => void;
  pr: PurchaseRequisition | null;
  storeSettings?: StoreSettings | null;
}

const money = (cents: number) => formatMoney(cents / 100);

export function PrintablePurchaseRequisitionModal({ open, onClose, pr, storeSettings }: Props) {
  if (!pr) return null;

  const handlePrint = () => {
    window.print();
  };

  return (
    <Dialog open={open} onClose={onClose} title={`Purchase Requisition: ${pr.prNumber}`}>
      <div className="space-y-6 text-slate-800">
        <div className="flex justify-between items-center border-b pb-3 print:hidden">
          <span className="text-xs text-slate-500">Official Internal Procurement Document</span>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
            <Button variant="primary" onClick={handlePrint}>
              🖨️ Print Requisition
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
              <h2 className="text-lg font-bold text-slate-900">PURCHASE REQUISITION</h2>
              <span className="inline-block mt-1 px-2.5 py-0.5 text-xs font-semibold rounded bg-slate-100 text-slate-800 border">
                Status: {pr.status}
              </span>
            </div>
            <div className="text-right">
              <span className="text-xs uppercase tracking-wider text-slate-400 font-bold">Requisition Number</span>
              <p className="text-lg font-mono font-bold text-slate-900">{pr.prNumber}</p>
              <p className="text-xs text-slate-500">Date: {pr.requisitionDate?.slice(0, 10)}</p>
              {pr.requiredDate && (
                <p className="text-xs text-slate-500">Date Needed: {pr.requiredDate?.slice(0, 10)}</p>
              )}
            </div>
          </div>

          {/* Metadata Grid */}
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 p-3 bg-slate-50 border rounded text-xs">
            <div>
              <span className="text-slate-500 block">Department</span>
              <span className="font-semibold text-slate-800">{pr.department || "General Store"}</span>
            </div>
            <div>
              <span className="text-slate-500 block">Requested By</span>
              <span className="font-semibold text-slate-800">{pr.requestedByUserName || "Authorized Requester"}</span>
            </div>
            <div>
              <span className="text-slate-500 block">Priority</span>
              <span className="font-semibold text-slate-800">{pr.purpose || "Normal"}</span>
            </div>
            {pr.approvedByUserName && (
              <div className="col-span-2">
                <span className="text-slate-500 block">Approved By & Timestamp</span>
                <span className="font-semibold text-emerald-700">
                  {pr.approvedByUserName} {pr.approvedAt ? `(${new Date(pr.approvedAt).toLocaleString()})` : ""}
                </span>
              </div>
            )}
          </div>

          {/* Line Items Table */}
          <table className="w-full text-left text-xs border border-slate-200">
            <thead className="bg-slate-100 text-slate-700 uppercase font-semibold border-b">
              <tr>
                <th className="p-2 border-r w-8 text-center">#</th>
                <th className="p-2 border-r">Item Description</th>
                <th className="p-2 border-r">SKU / Code</th>
                <th className="p-2 border-r text-right">Req. Qty</th>
                <th className="p-2 border-r text-right">Appr. Qty</th>
                <th className="p-2 border-r">Unit</th>
                <th className="p-2 border-r text-right">Est. Unit Cost</th>
                <th className="p-2 text-right">Est. Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {pr.items.map((item, idx) => (
                <tr key={item.id} className="hover:bg-slate-50">
                  <td className="p-2 border-r text-center font-mono">{idx + 1}</td>
                  <td className="p-2 border-r font-medium text-slate-900">{item.productName}</td>
                  <td className="p-2 border-r font-mono text-slate-500">{item.productSku || "—"}</td>
                  <td className="p-2 border-r text-right font-mono">{item.requestedQuantityX1000 / 1000}</td>
                  <td className="p-2 border-r text-right font-mono font-semibold text-slate-900">
                    {item.approvedQuantityX1000 / 1000}
                  </td>
                  <td className="p-2 border-r text-slate-600">{item.uomName}</td>
                  <td className="p-2 border-r text-right font-mono">{money(item.estimatedCostCents)}</td>
                  <td className="p-2 text-right font-mono font-semibold text-slate-900">
                    {money(item.estimatedTotalCents)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot className="bg-slate-50 font-bold border-t">
              <tr>
                <td colSpan={7} className="p-2 text-right uppercase">
                  Grand Total Estimated:
                </td>
                <td className="p-2 text-right font-mono text-sm text-slate-900">
                  {money(pr.totalEstimatedCostCents)}
                </td>
              </tr>
            </tfoot>
          </table>

          {pr.notes && (
            <div className="p-3 border rounded text-xs bg-slate-50">
              <span className="font-semibold text-slate-700 block mb-1">Requisition Notes:</span>
              <p className="text-slate-600">{pr.notes}</p>
            </div>
          )}

          {/* Signatures Footer */}
          <div className="grid grid-cols-2 gap-8 pt-8 border-t text-xs">
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                {pr.requestedByUserName || "Requester"}
              </div>
              <span className="text-slate-500 mt-1 block">Requested By / Signature</span>
            </div>
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                {pr.approvedByUserName || "Authorized Approver"}
              </div>
              <span className="text-slate-500 mt-1 block">Approved By / Store Manager</span>
            </div>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
