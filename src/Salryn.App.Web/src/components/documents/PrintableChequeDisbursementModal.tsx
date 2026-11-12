import { Dialog, Button } from "../ui";
import type { SupplierPayment } from "../../api/procureToPay";
import type { StoreSettings } from "../../api/storeSettings";
import { formatMoney } from "../../utils/format";

interface Props {
  open: boolean;
  onClose: () => void;
  payment: SupplierPayment | null;
  storeSettings?: StoreSettings | null;
}

const money = (cents: number) => formatMoney(cents / 100);

export function PrintableChequeDisbursementModal({ open, onClose, payment, storeSettings }: Props) {
  if (!payment) return null;

  const handlePrint = () => {
    window.print();
  };
  const isSettled = payment.status === "Settled";
  const actualTransferDate = isSettled ? (payment.actualDisbursementDate || "Unknown / Not recorded") : "Not yet disbursed";
  const confirmationTime = isSettled
    ? (payment.approvedAt ? new Date(payment.approvedAt).toLocaleString("en-PH", { timeZone: "Asia/Manila" }) : "Unknown / Not recorded")
    : "Not yet confirmed";

  return (
    <Dialog open={open} onClose={onClose} title={`Payment Voucher: ${payment.paymentNumber}`}>
      <div className="space-y-6 text-slate-800">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b pb-3 print:hidden">
          <span className="min-w-0 flex-1 basis-52 text-xs text-slate-500">Accounts Payable Payment Voucher — {payment.status === "Settled" ? "Operator-Confirmed Disbursement" : "Pending Settlement — Not Yet Disbursed"}</span>
          <div className="flex max-w-full flex-wrap items-center justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
            <Button variant="primary" className="whitespace-nowrap" onClick={handlePrint}>
              🖨️ Print Voucher
            </Button>
          </div>
        </div>

        {/* Printable Voucher Body */}
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
              <span className="text-xs uppercase tracking-wider text-slate-400 font-bold">Treasury & Accounts Payable</span>
              <h2 className="text-lg font-bold text-slate-900">CHECK / DISBURSEMENT VOUCHER</h2>
              <span className="inline-block mt-1 px-2.5 py-0.5 text-xs font-semibold rounded bg-slate-100 text-slate-800 border">
                Status: {payment.status === "Settled" ? "Operator-Confirmed Disbursement" : "Pending Settlement — Not Yet Disbursed"}
              </span>
            </div>
            <div className="text-right">
              <span className="text-xs uppercase tracking-wider text-slate-400 font-bold">Voucher Number</span>
              <p className="text-lg font-mono font-bold text-slate-900">{payment.paymentNumber}</p>
              <p className="text-xs text-slate-500">Recorded / requested date: {payment.paymentDate?.slice(0, 10) || "Unknown / Not recorded"}</p>
              <p className="text-xs text-slate-700 font-semibold">Actual funds-transfer date: {actualTransferDate}</p>
              <p className="text-xs text-slate-500">Settlement confirmed at (Asia/Manila): {confirmationTime}</p>
              {payment.checkNumber && (
                <p className="text-xs font-mono font-bold text-indigo-700">Check #: {payment.checkNumber}</p>
              )}
            </div>
          </div>

          {/* Payee and Settlement Account */}
          <div className="grid grid-cols-2 gap-4 p-4 bg-slate-50 border rounded text-xs">
            <div>
              <span className="text-slate-500 font-bold uppercase block mb-1">Payee (Supplier)</span>
              <p className="text-sm font-bold text-slate-900">{payment.supplierName}</p>
              <p className="text-slate-600">{payment.status === "Settled" ? "Settled Invoice" : "Intended Invoice"}: {payment.invoiceNumber}</p>
            </div>
            <div>
              <span className="text-slate-500 font-bold uppercase block mb-1">Payment Method & Banking</span>
              <p className="text-sm font-semibold text-slate-900">{payment.paymentMethod}</p>
              {payment.bankName && <p className="text-slate-600">Bank Name: {payment.bankName}</p>}
              {payment.referenceNumber && (
                <p className="text-slate-600 font-mono">Ref / Trx #: {payment.referenceNumber}</p>
              )}
            </div>
          </div>

          {/* Disbursement Details Table */}
          <table className="w-full text-left text-xs border border-slate-200">
            <thead className="bg-slate-100 text-slate-700 uppercase font-semibold border-b">
              <tr>
                <th className="p-2 border-r w-8 text-center">#</th>
                <th className="p-2 border-r">Account Title / Particulars</th>
                <th className="p-2 border-r">Invoice Reference</th>
                <th className="p-2 text-right">Disbursement Amount</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td className="p-2 border-r text-center font-mono">1</td>
                <td className="p-2 border-r font-medium text-slate-900">
                  Accounts Payable {payment.status === "Settled" ? "Settlement" : "Payment Request"} — {payment.supplierName}
                  {payment.notes && <div className="text-[11px] text-slate-500">{payment.notes}</div>}
                </td>
                <td className="p-2 border-r font-mono text-slate-700">{payment.invoiceNumber}</td>
                <td className="p-2 text-right font-mono font-bold text-slate-900">{money(payment.amountCents)}</td>
              </tr>
            </tbody>
            <tfoot className="bg-slate-50 font-bold border-t">
              <tr>
                <td colSpan={3} className="p-2 text-right uppercase">
                  {payment.status === "Settled" ? "Net Amount Disbursed:" : "Pending Amount — Not Yet Disbursed:"}
                </td>
                <td className="p-2 text-right font-mono text-sm text-slate-900 font-bold">
                  {money(payment.amountCents)}
                </td>
              </tr>
            </tfoot>
          </table>

          {/* Cheque Layout Mockup if paymentMethod is Check */}
          {payment.checkNumber && (
            <div className="p-4 border-2 border-dashed border-slate-300 rounded bg-slate-50 text-xs space-y-2">
              <div className="flex justify-between font-mono text-slate-700">
                <span>BANK NAME: {payment.bankName || "Not recorded"}</span>
                <span>CHECK NO: {payment.checkNumber}</span>
              </div>
              <div className="flex justify-between items-baseline pt-2">
                <div>
                  <span className="text-slate-400 mr-2">PAY TO THE ORDER OF:</span>
                  <span className="font-bold underline text-slate-900">{payment.supplierName}</span>
                </div>
                <div className="border px-2 py-1 bg-white font-mono font-bold">{money(payment.amountCents)}</div>
              </div>
            </div>
          )}

          <p className="text-[11px] text-slate-600">The actual funds-transfer date is supplied by an authorized operator, not independently verified by a bank or payment provider. Pending vouchers are not proof of disbursement. Historical settled records without this date remain Unknown / Not recorded.</p>

          {/* Transaction custody and unsigned recipient acknowledgment */}
          <div className="grid grid-cols-3 gap-4 pt-8 border-t text-xs">
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                {payment.recordedByUserId}
              </div>
              <span className="text-slate-500 mt-1 block">Recorded By — User ID</span>
            </div>
            <div className="text-center">
              <div className="border-b border-slate-400 pb-1 font-semibold text-slate-800">
                {payment.status === "Settled" && payment.approvedByUserId ? payment.approvedByUserId : "Pending Confirmation"}
              </div>
              <span className="text-slate-500 mt-1 block">
                {payment.status === "Settled" ? `Settlement Confirmed By — User ID • ${confirmationTime} (Asia/Manila)` : "Settlement Status"}
              </span>
            </div>
            <div className="text-center">
              <div className="min-h-5 border-b border-slate-400 pb-1" aria-label="Unsigned recipient acknowledgment">&nbsp;</div>
              <span className="text-slate-500 mt-1 block">Recipient Acknowledgment (Unsigned)</span>
            </div>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
