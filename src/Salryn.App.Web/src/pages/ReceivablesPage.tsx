import { useRef, useState } from "react";
import { ApiError } from "../api/http";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  collectReceivable,
  fetchCustomerLedger,
  fetchReceivableCustomers,
  fetchReceivablesSummary,
} from "../api/receivables";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, LoadingState, TextInput } from "../components/ui";
import { formatDateTime, formatMoney } from "../utils/format";

const money = (cents: number) => formatMoney(cents / 100);

export function ReceivablesPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [collectionAmount, setCollectionAmount] = useState("");
  const [collectionNote, setCollectionNote] = useState("");
  const [message, setMessage] = useState("");
  const [isCollecting, setIsCollecting] = useState(false);
  const collectionRequestIdRef = useRef<string | null>(null);
  const summary = useQuery({ queryKey: ["receivables-summary"], queryFn: fetchReceivablesSummary });
  const customers = useQuery({ queryKey: ["receivables-customers", search], queryFn: () => fetchReceivableCustomers(search) });
  const ledger = useQuery({
    queryKey: ["receivables-ledger", selectedId],
    queryFn: () => fetchCustomerLedger(selectedId),
    enabled: Boolean(selectedId),
  });
  const selected = customers.data?.find((row) => row.customer.id === selectedId);

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
      await collectReceivable({ customerId: selectedId, amountCents, method: "Cash", note: collectionNote || undefined, clientRequestId });
      collectionRequestIdRef.current = null;
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

  return <div className="space-y-4">
    <CommandBar title="Utang Ledger" subtitle="Accounts Receivable balances, collections, and immutable customer history." />
    {message ? <Alert tone="info">{message}</Alert> : null}
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {[
        ["Total receivables", summary.data?.totalReceivablesCents],
        ["Due today", summary.data?.dueTodayCents],
        ["Overdue", summary.data?.overdueCents],
        ["Collected today", summary.data?.collectedTodayCents],
      ].map(([label, value]) => <Card key={label as string}><CardBody><p className="text-xs uppercase text-slate-500">{label}</p><p className="mt-2 text-2xl font-bold text-slate-950">{money((value as number | undefined) ?? 0)}</p></CardBody></Card>)}
    </div>
    <div className="grid gap-4 xl:grid-cols-[1fr_1.2fr]">
      <Card><CardHeader><CardTitle>Customers</CardTitle></CardHeader><CardBody className="space-y-3">
        <TextInput value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search customer name or phone" />
        {customers.isLoading ? <LoadingState label="Loading customers..." /> : null}
        <div className="max-h-[32rem] space-y-2 overflow-y-auto">{(customers.data ?? []).map((row) =>
          <button key={row.customer.id} type="button" onClick={() => setSelectedId(row.customer.id)}
            className={`w-full rounded-lg border p-3 text-left ${selectedId === row.customer.id ? "border-blue-500 bg-blue-50" : "border-slate-200 bg-white"}`}>
            <div className="flex justify-between gap-3"><div><p className="font-semibold">{row.customer.fullName}</p><p className="text-xs text-slate-500">{row.customer.phone || "No phone"}</p></div><p className="font-bold">{money(row.balanceCents)}</p></div>
            <p className={`mt-2 text-xs ${row.oldestDueDate && row.oldestDueDate < new Date().toISOString().slice(0, 10) ? "font-semibold text-red-700" : "text-slate-500"}`}>
              {row.oldestDueDate ? `Due ${row.oldestDueDate}` : "No due date"} · {row.lastTransactionAt ? formatDateTime(row.lastTransactionAt) : "No ledger activity"}
            </p>
          </button>)}</div>
      </CardBody></Card>
      <Card><CardHeader><CardTitle>{selected ? `${selected.customer.fullName} ledger` : "Customer ledger"}</CardTitle></CardHeader><CardBody className="space-y-4">
        {!selectedId ? <p className="text-sm text-slate-500">Select a customer to inspect recurring Utang and collections.</p> : <>
          <div className="grid gap-2 sm:grid-cols-3"><TextInput value={collectionAmount} onChange={(event) => setCollectionAmount(event.target.value)} inputMode="decimal" placeholder="Collection amount" disabled={isCollecting} /><TextInput value={collectionNote} onChange={(event) => setCollectionNote(event.target.value)} placeholder="Optional collection note" disabled={isCollecting} /><Button onClick={() => void collect()} disabled={isCollecting}>{isCollecting ? "Recording..." : "Collect payment"}</Button></div>
          <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="border-b text-left text-xs uppercase text-slate-500"><th className="p-2">Date</th><th className="p-2">Entry</th><th className="p-2">Debit</th><th className="p-2">Credit</th><th className="p-2">Balance</th><th className="p-2">Note</th></tr></thead><tbody>{(ledger.data ?? []).map((entry) => <tr key={entry.id} className="border-b"><td className="p-2">{formatDateTime(entry.createdAt)}</td><td className="p-2">{entry.entryType}</td><td className="p-2">{money(entry.debitCents)}</td><td className="p-2">{money(entry.creditCents)}</td><td className="p-2 font-semibold">{money(entry.balanceAfterCents)}</td><td className="p-2">{entry.note || "—"}</td></tr>)}</tbody></table></div>
        </>}
      </CardBody></Card>
    </div>
  </div>;
}
