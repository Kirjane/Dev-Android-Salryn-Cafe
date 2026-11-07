import { useRef, useState } from "react";
import { ApiError } from "../api/http";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  collectReceivable,
  fetchCustomerLedgerPage,
  fetchReceivableCustomersPage,
  fetchReceivablesSummary,
  ReceivableEntryType,
  voidReceivableCollection,
} from "../api/receivables";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, LoadingState, TextInput } from "../components/ui";
import { useAppSession } from "../hooks/useAppSession";
import { Permissions, hasPermission } from "../security/permissions";
import { formatDateTime, formatMoney } from "../utils/format";

const money = (cents: number) => formatMoney(cents / 100);
const SalePaymentStatus = { Partial: 3 } as const;

function ledgerLabel(entryType: number, paymentStatus: number | null) {
  if (entryType === ReceivableEntryType.CreditSale) {
    return paymentStatus === SalePaymentStatus.Partial ? "Partial Credit Sale" : "Credit Sale";
  }
  if (entryType === ReceivableEntryType.Collection) return "Payment Collection";
  if (entryType === ReceivableEntryType.CollectionReversal) return "Collection Reversal";
  if (entryType === ReceivableEntryType.SaleReversal) return "Credit Sale Reversal";
  if (entryType === ReceivableEntryType.AdjustmentDebit || entryType === ReceivableEntryType.AdjustmentCredit) return "Adjustment";
  return "Ledger Entry";
}

export function ReceivablesPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [customerPage, setCustomerPage] = useState(1);
  const [ledgerPage, setLedgerPage] = useState(1);
  const [selectedId, setSelectedId] = useState("");
  const [collectionAmount, setCollectionAmount] = useState("");
  const [collectionNote, setCollectionNote] = useState("");
  const [message, setMessage] = useState("");
  const [isCollecting, setIsCollecting] = useState(false);
  const [collectionAcknowledgment, setCollectionAcknowledgment] = useState("");
  const [voidCollectionId, setVoidCollectionId] = useState("");
  const [voidReason, setVoidReason] = useState("");
  const [voidConfirmed, setVoidConfirmed] = useState(false);
  const [isVoiding, setIsVoiding] = useState(false);
  const collectionRequestIdRef = useRef<string | null>(null);
  const session = useAppSession();
  const canAdjust = hasPermission(session.data, Permissions.ReceivablesAdjust);
  const summary = useQuery({ queryKey: ["receivables-summary"], queryFn: fetchReceivablesSummary });
  const customers = useQuery({ queryKey: ["receivables-customers", search, customerPage], queryFn: () => fetchReceivableCustomersPage(search, customerPage, 50) });
  const ledger = useQuery({
    queryKey: ["receivables-ledger", selectedId, ledgerPage],
    queryFn: () => fetchCustomerLedgerPage(selectedId, ledgerPage, 100),
    enabled: Boolean(selectedId),
  });
  const selected = customers.data?.items.find((row) => row.customer.id === selectedId);
  const manilaToday = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Manila", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());

  async function collect() {
    if (isCollecting) return;
    const amountCents = Math.round(Number(collectionAmount) * 100);
    if (!selectedId || !Number.isFinite(amountCents) || amountCents <= 0) {
      setMessage("Select a customer and enter a positive collection amount.");
      return;
    }
    setIsCollecting(true);
    try {
      const clientRequestId = collectionRequestIdRef.current ?? crypto.randomUUID();
      collectionRequestIdRef.current = clientRequestId;
      const result = await collectReceivable({ customerId: selectedId, amountCents, method: "Cash", note: collectionNote || undefined, clientRequestId });
      collectionRequestIdRef.current = null;
      setCollectionAcknowledgment(result.plainTextAcknowledgment);
      setCollectionAmount(""); setCollectionNote(""); setMessage("Collection recorded. No new sale was created.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["receivables-summary"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-customers"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-ledger", selectedId] }),
      ]);
    } catch (error) {
      if (error instanceof ApiError) collectionRequestIdRef.current = null;
      setMessage(error instanceof Error ? error.message : "Collection failed.");
    } finally {
      setIsCollecting(false);
    }
  }

  async function reverseCollection() {
    if (isVoiding || !voidCollectionId || !voidReason.trim() || !voidConfirmed) return;
    setIsVoiding(true);
    try {
      await voidReceivableCollection(voidCollectionId, voidReason.trim());
      setVoidCollectionId("");
      setVoidReason("");
      setVoidConfirmed(false);
      setMessage("Collection reversed. The original collection remains in history and one reversal was appended.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["receivables-summary"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-customers"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-ledger", selectedId] }),
      ]);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Collection reversal failed.");
    } finally {
      setIsVoiding(false);
    }
  }

  function printCollectionAcknowledgment() {
    if (!collectionAcknowledgment) return;
    const printWindow = window.open("", "_blank", "width=520,height=720");
    if (!printWindow) {
      setMessage("Print window was blocked. Allow pop-ups and try again.");
      return;
    }
    printWindow.opener = null;
    printWindow.document.title = "Collection Receipt";
    const receipt = printWindow.document.createElement("pre");
    receipt.style.whiteSpace = "pre-wrap";
    receipt.style.fontFamily = "monospace";
    receipt.style.fontSize = "12px";
    receipt.textContent = collectionAcknowledgment;
    printWindow.document.body.appendChild(receipt);
    printWindow.print();
  }

  return <div className="space-y-4">
    <CommandBar title="Utang Ledger" subtitle="Accounts Receivable balances, collections, and immutable customer history." />
    {message ? <Alert tone="info">{message}</Alert> : null}
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {[
        ["Total receivables", summary.data?.totalReceivablesCents],
        ["Due today", summary.data?.dueTodayCents],
        ["Overdue", summary.data?.overdueCents],
        ["Collected today", summary.data?.collectedTodayCents],
      ].map(([label, value]) => <Card key={label as string}><CardBody><p className="text-xs uppercase text-slate-500">{label}</p><p className="mt-2 text-2xl font-bold text-slate-950">{summary.isError ? "Unavailable" : summary.isLoading ? "Loading…" : money(value as number)}</p></CardBody></Card>)}
    </div>
    <div className="grid gap-4 xl:grid-cols-[1fr_1.2fr]">
      <Card><CardHeader><CardTitle>Customers</CardTitle></CardHeader><CardBody className="space-y-3">
        <TextInput value={search} onChange={(event) => { setSearch(event.target.value); setCustomerPage(1); }} placeholder="Search customer name or phone" />
        {customers.isLoading ? <LoadingState label="Loading customers..." /> : null}
        {customers.isError ? <Alert tone="danger">{(customers.error as Error).message}</Alert> : null}
        <div className="max-h-[32rem] space-y-2 overflow-y-auto">{(customers.data?.items ?? []).map((row) =>
          <button key={row.customer.id} type="button" onClick={() => { setSelectedId(row.customer.id); setLedgerPage(1); }}
            className={`w-full rounded-lg border p-3 text-left ${selectedId === row.customer.id ? "border-blue-500 bg-blue-50" : "border-slate-200 bg-white"}`}>
            <div className="flex justify-between gap-3"><div><p className="font-semibold">{row.customer.fullName}</p><p className="text-xs text-slate-500">{row.customer.phone || "No phone"}</p></div><p className="font-bold">{money(row.balanceCents)}</p></div>
            <p className={`mt-2 text-xs ${row.oldestDueDate && row.oldestDueDate < manilaToday ? "font-semibold text-red-700" : "text-slate-500"}`}>
              {row.oldestDueDate ? `Due ${row.oldestDueDate}` : "No due date"} · {row.lastTransactionAt ? formatDateTime(row.lastTransactionAt) : "No ledger activity"}
            </p>
          </button>)}</div>
        {customers.data ? <div className="flex items-center justify-between text-xs text-slate-500"><span>Showing {customers.data.items.length} of {customers.data.totalCount} · page {customers.data.pageNumber} of {Math.max(1, customers.data.totalPages)}</span><div className="flex gap-2"><Button size="sm" variant="secondary" disabled={customerPage <= 1} onClick={() => setCustomerPage(p => p - 1)}>Previous</Button><Button size="sm" variant="secondary" disabled={!customers.data.hasMore} onClick={() => setCustomerPage(p => p + 1)}>Next</Button></div></div> : null}
      </CardBody></Card>
      <Card><CardHeader><CardTitle>{selected ? `${selected.customer.fullName} ledger` : "Customer ledger"}</CardTitle></CardHeader><CardBody className="space-y-4">
        {!selectedId ? <p className="text-sm text-slate-500">Select a customer to inspect recurring Utang and collections.</p> : <>
          <div className="grid gap-2 sm:grid-cols-3"><TextInput value={collectionAmount} onChange={(event) => setCollectionAmount(event.target.value)} inputMode="decimal" placeholder="Collection amount" disabled={isCollecting} /><TextInput value={collectionNote} onChange={(event) => setCollectionNote(event.target.value)} placeholder="Optional collection note" disabled={isCollecting} /><Button onClick={() => void collect()} disabled={isCollecting}>{isCollecting ? "Recording..." : "Collect payment"}</Button></div>
          {collectionAcknowledgment ? <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3">
            <div className="flex flex-wrap items-center justify-between gap-2"><p className="text-sm font-semibold text-emerald-950">Collection Receipt</p><Button size="sm" variant="secondary" onClick={printCollectionAcknowledgment}>Print acknowledgment</Button></div>
            <pre className="mt-3 whitespace-pre-wrap rounded-md border border-emerald-100 bg-white p-3 font-mono text-xs text-slate-800">{collectionAcknowledgment}</pre>
          </div> : null}
          {canAdjust && voidCollectionId ? <div className="space-y-3 rounded-md border border-rose-200 bg-rose-50 p-3">
            <p className="text-sm font-semibold text-rose-950">Reverse collection</p>
            <TextInput value={voidReason} onChange={(event) => { setVoidReason(event.target.value); setVoidConfirmed(false); }} placeholder="Required reversal reason" disabled={isVoiding} />
            <label className="flex items-start gap-2 rounded-md border border-rose-200 bg-white p-3 text-xs text-rose-900">
              <input type="checkbox" className="mt-0.5" checked={voidConfirmed} onChange={(event) => setVoidConfirmed(event.target.checked)} disabled={isVoiding} />
              <span>I confirm this collection was recorded incorrectly. The original entry will remain and one append-only reversal will restore the balance.</span>
            </label>
            <div className="flex gap-2"><Button variant="danger" onClick={() => void reverseCollection()} disabled={isVoiding || !voidReason.trim() || !voidConfirmed}>{isVoiding ? "Reversing..." : "Confirm reversal"}</Button><Button variant="secondary" onClick={() => { setVoidCollectionId(""); setVoidReason(""); setVoidConfirmed(false); }} disabled={isVoiding}>Cancel</Button></div>
          </div> : null}
          {ledger.isError ? <Alert tone="danger">{(ledger.error as Error).message}</Alert> : null}
          <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="border-b text-left text-xs uppercase text-slate-500"><th className="p-2">Date</th><th className="p-2">Entry</th><th className="p-2">Debit</th><th className="p-2">Credit</th><th className="p-2">Balance</th><th className="p-2">Note</th>{canAdjust ? <th className="p-2">Action</th> : null}</tr></thead><tbody>{(ledger.data?.items ?? []).map((entry) => {
            const alreadyReversed = Boolean(entry.collectionId) && (ledger.data?.items ?? []).some((candidate) => candidate.collectionId === entry.collectionId && candidate.entryType === ReceivableEntryType.CollectionReversal);
            return <tr key={entry.id} className="border-b"><td className="p-2">{formatDateTime(entry.createdAt)}</td><td className="p-2">{ledgerLabel(entry.entryType, entry.paymentStatus)}</td><td className="p-2">{money(entry.debitCents)}</td><td className="p-2">{money(entry.creditCents)}</td><td className="p-2 font-semibold">{money(entry.balanceAfterCents)}</td><td className="p-2">{entry.note || "—"}</td>{canAdjust ? <td className="p-2">{entry.entryType === ReceivableEntryType.Collection && entry.collectionId ? <Button size="sm" variant="danger" onClick={() => { setVoidCollectionId(entry.collectionId!); setVoidReason(""); setVoidConfirmed(false); }} disabled={isVoiding || alreadyReversed}>{alreadyReversed ? "Reversed" : "Reverse"}</Button> : null}</td> : null}</tr>;
          })}</tbody></table></div>
          {ledger.data ? <div className="flex items-center justify-between text-xs text-slate-500"><span>Showing {ledger.data.items.length} of {ledger.data.totalCount} entries · page {ledger.data.pageNumber} of {Math.max(1, ledger.data.totalPages)}</span><div className="flex gap-2"><Button size="sm" variant="secondary" disabled={ledgerPage <= 1} onClick={() => setLedgerPage(p => p - 1)}>Previous</Button><Button size="sm" variant="secondary" disabled={!ledger.data.hasMore} onClick={() => setLedgerPage(p => p + 1)}>Next</Button></div></div> : null}
        </>}
      </CardBody></Card>
    </div>
  </div>;
}
