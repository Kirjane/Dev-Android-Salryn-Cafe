import { useEffect, useRef, useState } from "react";
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
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, Dialog as UiDialog, LoadingState, StatusPill, TextInput, useNotifications } from "../components/ui";
import { PdfDownloadButton } from "../components/documents/PdfDownloadButton";
import { useAppSession } from "../hooks/useAppSession";
import { Permissions, hasPermission } from "../security/permissions";
import { formatDateTime, formatMoney } from "../utils/format";
import { safeNotificationDescription } from "../utils/notificationError";

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
  const { notify } = useNotifications();
  const incoming = new URLSearchParams(typeof window !== "undefined" ? window.location.search : "");
  const [search, setSearch] = useState(incoming.get("search") ?? "");
  const [customerPage, setCustomerPage] = useState(1);
  const [ledgerPage, setLedgerPage] = useState(1);
  const [selectedId, setSelectedId] = useState(incoming.get("customerId") ?? "");
  const [collectionAmount, setCollectionAmount] = useState("");
  const [collectionMethod, setCollectionMethod] = useState<"Cash">("Cash");
  const [collectionNote, setCollectionNote] = useState("");
  const [message, setMessage] = useState("");
  const [isCollecting, setIsCollecting] = useState(false);
  const [collectionAcknowledgment, setCollectionAcknowledgment] = useState("");
  const [collectionAcknowledgmentId, setCollectionAcknowledgmentId] = useState<string | null>(null);
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

  useEffect(() => {
    if (!selectedId && customers.data?.items.length && incoming.get("customerId")) {
      setSelectedId(incoming.get("customerId")!);
    }
  }, [customers.data, incoming, selectedId]);

  async function collect() {
    if (isCollecting) return;
    if (collectionMethod !== "Cash") {
      setMessage("Only Cash collections are supported.");
      notify.error("Unsupported collection method", "Only Cash collections are allowed.");
      return;
    }
    const amountCents = Math.round(Number(collectionAmount) * 100);
    if (!selectedId || !Number.isFinite(amountCents) || amountCents <= 0) {
      setMessage("Select a customer and enter a positive collection amount.");
      notify.warning("Collection amount required", "Select a customer and enter a valid positive collection amount.");
      return;
    }
    setIsCollecting(true);
    try {
      const clientRequestId = collectionRequestIdRef.current ?? crypto.randomUUID();
      collectionRequestIdRef.current = clientRequestId;
      const result = await collectReceivable({
        customerId: selectedId,
        amountCents,
        method: "Cash",
        note: collectionNote || undefined,
        clientRequestId,
      });
      collectionRequestIdRef.current = null;
      setCollectionAcknowledgment(result.plainTextAcknowledgment);
      setCollectionAcknowledgmentId(result.collection.id);
      setCollectionAmount("");
      setCollectionNote("");
      setMessage("Collection recorded. No new sale was created.");
      notify.success("Collection recorded", "Payment collected and credited to customer ledger. No new sale was created.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["receivables-summary"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-customers"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-ledger", selectedId] }),
      ]);
    } catch (error) {
      if (error instanceof ApiError) collectionRequestIdRef.current = null;
      const errDescription = error instanceof Error ? error.message : "Collection failed.";
      setMessage(errDescription);
      notify.error("Collection failed", safeNotificationDescription(error, "The collection could not be recorded."));
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
      notify.success("Collection reversed", "The collection entry was reversed. One append-only reversal was recorded.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["receivables-summary"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-customers"] }),
        queryClient.invalidateQueries({ queryKey: ["receivables-ledger", selectedId] }),
      ]);
    } catch (error) {
      const errDescription = error instanceof Error ? error.message : "Collection reversal failed.";
      setMessage(errDescription);
      notify.error("Reversal failed", safeNotificationDescription(error, "The collection could not be reversed."));
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

  return (
    <div className="space-y-4">
      <CommandBar
        title="Accounts Receivable"
        subtitle="Utang Ledger — Customer balances, collections, and immutable history."
        status={<StatusPill tone="info">Signed U1 Ledger</StatusPill>}
      />
      {message ? <Alert tone={message.includes("failed") ? "danger" : "info"}>{message}</Alert> : null}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {[
          ["Total receivables", summary.data?.totalReceivablesCents],
          ["Due today", summary.data?.dueTodayCents],
          ["Overdue", summary.data?.overdueCents],
          ["Collected today", summary.data?.collectedTodayCents],
        ].map(([label, value]) => (
          <Card key={label as string}>
            <CardBody>
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p>
              <p className="mt-2 text-2xl font-bold tabular-nums text-slate-950">
                {summary.isError ? "Unavailable" : summary.isLoading ? "Loading…" : money(value as number)}
              </p>
            </CardBody>
          </Card>
        ))}
      </div>
      <div className="grid gap-4 xl:grid-cols-[1fr_1.2fr]">
        <Card>
          <CardHeader>
            <CardTitle>Customers</CardTitle>
          </CardHeader>
          <CardBody className="space-y-3">
            <TextInput
              id="receivables-customer-search"
              aria-label="Search customer name or phone"
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setCustomerPage(1);
              }}
              placeholder="Search customer name or phone"
            />
            {customers.isLoading ? <LoadingState label="Loading customers..." /> : null}
            {customers.isError ? <Alert tone="danger">{(customers.error as Error).message}</Alert> : null}
            <div className="max-h-[32rem] space-y-2 overflow-y-auto">
              {(customers.data?.items ?? []).map((row) => (
                <button
                  key={row.customer.id}
                  type="button"
                  onClick={() => {
                    setSelectedId(row.customer.id);
                    setLedgerPage(1);
                  }}
                  className={`w-full rounded-lg border p-3 text-left transition-colors ${
                    selectedId === row.customer.id
                      ? "border-blue-500 bg-blue-50"
                      : "border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50/60"
                  }`}
                >
                  <div className="flex justify-between gap-3">
                    <div>
                      <p className="font-semibold text-slate-900">{row.customer.fullName}</p>
                      <p className="text-xs text-slate-500">{row.customer.phone || "No phone"}</p>
                    </div>
                    <p className="font-bold tabular-nums text-slate-950">{money(row.balanceCents)}</p>
                  </div>
                  <p
                    className={`mt-2 text-xs ${
                      row.oldestDueDate && row.oldestDueDate < manilaToday
                        ? "font-semibold text-red-700"
                        : "text-slate-500"
                    }`}
                  >
                    {row.oldestDueDate ? `Due ${row.oldestDueDate}` : "No due date"} ·{" "}
                    {row.lastTransactionAt ? formatDateTime(row.lastTransactionAt) : "No ledger activity"}
                  </p>
                </button>
              ))}
            </div>
            {customers.data ? (
              <div className="flex items-center justify-between text-xs text-slate-500">
                <span>
                  Showing {customers.data.items.length} of {customers.data.totalCount} · page {customers.data.pageNumber} of{" "}
                  {Math.max(1, customers.data.totalPages)}
                </span>
                <div className="flex gap-2">
                  <Button size="sm" variant="secondary" disabled={customerPage <= 1} onClick={() => setCustomerPage((p) => p - 1)}>
                    Previous
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    disabled={!customers.data.hasMore}
                    onClick={() => setCustomerPage((p) => p + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            ) : null}
          </CardBody>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{selected ? `${selected.customer.fullName} ledger` : "Customer ledger"}</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            {!selectedId ? (
              <p className="text-sm text-slate-500">Select a customer to inspect recurring Utang and collections.</p>
            ) : (
              <>
                <div className="space-y-3 rounded-lg border border-slate-200 bg-slate-50/70 p-3">
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div className="min-w-0">
                      <label
                        htmlFor="receivables-collection-method"
                        className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600"
                      >
                        Method
                      </label>
                      <select
                        id="receivables-collection-method"
                        aria-label="Collection payment method"
                        className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900"
                        value={collectionMethod}
                        onChange={(event) => {
                          const next = event.target.value;
                          if (next !== "Cash") {
                            setMessage("Only Cash collections are supported.");
                            notify.error("Unsupported collection method", "Only Cash collections are allowed.");
                            return;
                          }
                          setCollectionMethod("Cash");
                        }}
                        disabled={isCollecting}
                      >
                        <option value="Cash">Cash</option>
                      </select>
                    </div>
                    <div className="min-w-0">
                      <label
                        htmlFor="receivables-collection-amount"
                        className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600"
                      >
                        Collection amount
                      </label>
                      <TextInput
                        id="receivables-collection-amount"
                        aria-label="Collection amount"
                        className="h-10 w-full"
                        value={collectionAmount}
                        onChange={(event) => setCollectionAmount(event.target.value)}
                        inputMode="decimal"
                        placeholder="Collection amount"
                        disabled={isCollecting}
                      />
                    </div>
                  </div>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end">
                    <div className="min-w-0">
                      <label
                        htmlFor="receivables-collection-note"
                        className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600"
                      >
                        Collection note
                      </label>
                      <TextInput
                        id="receivables-collection-note"
                        aria-label="Collection note"
                        className="h-10 w-full"
                        value={collectionNote}
                        onChange={(event) => setCollectionNote(event.target.value)}
                        placeholder="Optional collection note"
                        disabled={isCollecting}
                      />
                    </div>
                    <Button
                      className="h-10 w-full whitespace-nowrap sm:w-auto"
                      aria-label="Collect payment"
                      onClick={() => void collect()}
                      disabled={isCollecting}
                    >
                      {isCollecting ? "Recording..." : "Collect payment"}
                    </Button>
                  </div>
                </div>
                {collectionAcknowledgment ? (
                  <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <p className="text-sm font-semibold text-emerald-950">Collection Receipt</p>
                      <div className="flex flex-wrap gap-2">
                        {collectionAcknowledgmentId ? (
                          <PdfDownloadButton
                            size="sm"
                            url={`/api/receivables/collections/${collectionAcknowledgmentId}/pdf`}
                            fileName={`AR-ACK-${collectionAcknowledgmentId.slice(0, 8).toUpperCase()}.pdf`}
                          />
                        ) : null}
                        <Button size="sm" variant="secondary" onClick={printCollectionAcknowledgment}>
                          Print acknowledgment
                        </Button>
                      </div>
                    </div>
                    <pre className="mt-3 whitespace-pre-wrap rounded-md border border-emerald-100 bg-white p-3 font-mono text-xs text-slate-800">
                      {collectionAcknowledgment}
                    </pre>
                  </div>
                ) : null}

                {ledger.isError ? <Alert tone="danger">{(ledger.error as Error).message}</Alert> : null}
                <div className="overflow-x-auto rounded-lg border border-slate-200">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-slate-200 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                        <th className="p-2.5">Date</th>
                        <th className="p-2.5">Entry</th>
                        <th className="p-2.5 text-right">Debit</th>
                        <th className="p-2.5 text-right">Credit</th>
                        <th className="p-2.5 text-right">Balance</th>
                        <th className="p-2.5">Note</th>
                        <th className="p-2.5 text-right">Action</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {(ledger.data?.items ?? []).map((entry) => {
                        const alreadyReversed =
                          Boolean(entry.collectionId) &&
                          (ledger.data?.items ?? []).some(
                            (candidate) =>
                              candidate.collectionId === entry.collectionId &&
                              candidate.entryType === ReceivableEntryType.CollectionReversal,
                          );
                        return (
                          <tr key={entry.id} className="hover:bg-slate-50/70">
                            <td className="p-2.5 text-xs text-slate-600">{formatDateTime(entry.createdAt)}</td>
                            <td className="p-2.5 font-medium text-slate-900">
                              {ledgerLabel(entry.entryType, entry.paymentStatus)}
                            </td>
                            <td className="p-2.5 text-right tabular-nums text-slate-800">{money(entry.debitCents)}</td>
                            <td className="p-2.5 text-right tabular-nums text-emerald-700">{money(entry.creditCents)}</td>
                            <td className="p-2.5 text-right font-semibold tabular-nums text-slate-950">
                              {money(entry.balanceAfterCents)}
                            </td>
                            <td className="p-2.5 text-slate-600">{entry.note || "—"}</td>
                            <td className="p-2.5 text-right">
                              {entry.entryType === ReceivableEntryType.Collection && entry.collectionId ? (
                                <div className="flex justify-end gap-1.5">
                                  <PdfDownloadButton
                                    size="sm"
                                    label="A4 PDF"
                                    url={`/api/receivables/collections/${entry.collectionId}/pdf`}
                                    fileName={`AR-ACK-${entry.collectionId.slice(0, 8).toUpperCase()}.pdf`}
                                  />
                                  {canAdjust ? (
                                    <Button
                                      size="sm"
                                      variant="danger"
                                      onClick={() => {
                                        setVoidCollectionId(entry.collectionId!);
                                        setVoidReason("");
                                        setVoidConfirmed(false);
                                      }}
                                      disabled={isVoiding || alreadyReversed}
                                    >
                                      {alreadyReversed ? "Reversed" : "Reverse"}
                                    </Button>
                                  ) : null}
                                </div>
                              ) : null}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
                {ledger.data ? (
                  <div className="flex items-center justify-between text-xs text-slate-500">
                    <span>
                      Showing {ledger.data.items.length} of {ledger.data.totalCount} entries · page {ledger.data.pageNumber} of{" "}
                      {Math.max(1, ledger.data.totalPages)}
                    </span>
                    <div className="flex gap-2">
                      <Button size="sm" variant="secondary" disabled={ledgerPage <= 1} onClick={() => setLedgerPage((p) => p - 1)}>
                        Previous
                      </Button>
                      <Button
                        size="sm"
                        variant="secondary"
                        disabled={!ledger.data.hasMore}
                        onClick={() => setLedgerPage((p) => p + 1)}
                      >
                        Next
                      </Button>
                    </div>
                  </div>
                ) : null}
              </>
            )}
          </CardBody>
        </Card>
      </div>

      {canAdjust && voidCollectionId ? (
        <UiDialog
          open={Boolean(voidCollectionId)}
          title="Reverse collection"
          subtitle="Append-only reversal will restore customer balance. Original receipt stays in audit history."
          busy={isVoiding}
          onClose={() => {
            if (!isVoiding) {
              setVoidCollectionId("");
              setVoidReason("");
              setVoidConfirmed(false);
            }
          }}
          footer={
            <>
              <Button
                variant="secondary"
                disabled={isVoiding}
                onClick={() => {
                  setVoidCollectionId("");
                  setVoidReason("");
                  setVoidConfirmed(false);
                }}
              >
                Cancel
              </Button>
              <Button
                variant="danger"
                disabled={isVoiding || !voidReason.trim() || !voidConfirmed}
                onClick={() => void reverseCollection()}
              >
                {isVoiding ? "Reversing..." : "Confirm reversal"}
              </Button>
            </>
          }
        >
          <div className="space-y-3">
            <label className="block text-sm font-medium text-slate-700" htmlFor="receivables-void-reason">
              Required reversal reason
            </label>
            <TextInput
              id="receivables-void-reason"
              aria-label="Required reversal reason"
              value={voidReason}
              onChange={(event) => {
                setVoidReason(event.target.value);
                setVoidConfirmed(false);
              }}
              placeholder="e.g. Incorrect tender amount recorded..."
              disabled={isVoiding}
            />
            <label className="flex items-start gap-2 rounded-md border border-rose-200 bg-rose-50/50 p-3 text-xs text-rose-900">
              <input
                type="checkbox"
                className="mt-0.5"
                checked={voidConfirmed}
                onChange={(event) => setVoidConfirmed(event.target.checked)}
                disabled={isVoiding}
              />
              <span>
                I confirm this collection was recorded incorrectly. The original entry will remain and one append-only reversal will restore the balance.
              </span>
            </label>
          </div>
        </UiDialog>
      ) : null}
    </div>
  );
}
