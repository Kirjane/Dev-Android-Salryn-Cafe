import { PlayCircle, PauseCircle, Printer, Send, XCircle, Eye } from "lucide-react";
import { useMemo, useRef, useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  Dialog,
  FormField,
  StatusBadge,
  TextInput,
  useToast,
  type StatusTone,
} from "@salryn/cafe-ui";
import type { CafeOrderSummary, CafePrintStation, HostOrderState, ServiceMode, WorkstationApi } from "../cashier/types";

const STATE_TONE: Record<string, StatusTone> = {
  Open: "info",
  Parked: "warning",
  Submitted: "copper",
  Preparing: "copper",
  Ready: "success",
  Completed: "neutral",
  Cancelled: "danger",
};

export type OrdersBoardProps = {
  api: WorkstationApi;
  orders: CafeOrderSummary[];
  printStations: CafePrintStation[];
  permissions: string[];
  blocked: boolean;
  onDispatchIntent: (path: string, body: Record<string, unknown>) => Promise<boolean>;
  onSelectOrder: (orderId: string) => Promise<void>;
};

export function OrdersBoard({
  api,
  orders,
  printStations,
  permissions,
  blocked,
  onDispatchIntent,
  onSelectOrder,
}: OrdersBoardProps) {
  const { notify } = useToast();
  const [modeFilter, setModeFilter] = useState<"ALL" | ServiceMode>("ALL");
  const [stateFilter, setStateFilter] = useState<"ALL" | HostOrderState>("ALL");
  const [cancelTarget, setCancelTarget] = useState<CafeOrderSummary | null>(null);
  const [cancelReason, setCancelReason] = useState("");
  const [busyPrintId, setBusyPrintId] = useState<string | null>(null);
  const printLockRef = useRef(false);
  const pendingPrintOpsRef = useRef<Record<string, string>>({});

  const permitted = (perm: string) => permissions.includes(perm);
  const receiptStation = useMemo(
    () => printStations.find((s) => s.isActive && s.stationKind === "Receipt") ?? null,
    [printStations],
  );

  const visibleOrders = useMemo(
    () =>
      orders.filter((o) => {
        if (modeFilter !== "ALL" && o.serviceMode !== modeFilter) return false;
        if (stateFilter !== "ALL" && o.state !== stateFilter) return false;
        return true;
      }),
    [orders, modeFilter, stateFilter],
  );

  async function handleReprintReceipt(order: CafeOrderSummary) {
    if (!order.completedSaleId || !receiptStation || printLockRef.current) return;
    printLockRef.current = true;
    setBusyPrintId(order.orderId);
    const opKey = `${order.completedSaleId}:Reprint`;
    const operationId = pendingPrintOpsRef.current[opKey] ?? crypto.randomUUID();
    pendingPrintOpsRef.current[opKey] = operationId;
    try {
      await api.printReceipt({
        operationId,
        stationId: receiptStation.stationId,
        saleId: order.completedSaleId,
        copyKind: "Reprint",
      });
      delete pendingPrintOpsRef.current[opKey];
      notify("info", "Receipt reprint sent", `Order ${order.serviceReference || order.orderId.slice(0, 8)} queued on ${receiptStation.name}.`);
    } catch (error) {
      notify("danger", "Reprint failed", error instanceof Error ? error.message : "Could not send reprint.");
    } finally {
      printLockRef.current = false;
      setBusyPrintId(null);
    }
  }

  return (
    <Card>
      <CardHeader
        title="Cafe Orders & Lifecycle"
        subtitle="Live Host order revisions, park/resume, preparation handoff, cancellation, and receipt reprints."
        action={<StatusBadge tone="success">Host SQLite Live</StatusBadge>}
      />
      <CardBody>
        <div style={{ display: "grid", gap: 14 }}>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 12, justifyContent: "space-between" }}>
            <div role="group" aria-label="Filter orders by service mode" style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              {(["ALL", "DineIn", "Takeout", "Delivery"] as const).map((mode) => (
                <Button
                  key={mode}
                  size="sm"
                  variant={modeFilter === mode ? "primary" : "secondary"}
                  aria-pressed={modeFilter === mode}
                  onClick={() => setModeFilter(mode)}
                >
                  {mode === "ALL" ? "All Channels" : mode === "DineIn" ? "Dine-In" : mode}
                </Button>
              ))}
            </div>

            <div role="group" aria-label="Filter orders by status" style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
              {(["ALL", "Open", "Parked", "Submitted", "Preparing", "Ready", "Completed"] as const).map((st) => (
                <Button
                  key={st}
                  size="sm"
                  variant={stateFilter === st ? "accent" : "ghost"}
                  aria-pressed={stateFilter === st}
                  onClick={() => setStateFilter(st)}
                >
                  {st}
                </Button>
              ))}
            </div>
          </div>

          <DataTable
            caption="Cafe Order Lifecycle"
            rows={visibleOrders}
            getRowKey={(row) => row.orderId}
            columns={[
              {
                key: "ref",
                header: "Ticket / Ref",
                render: (r) => (
                  <div>
                    <strong>{r.serviceReference || r.serviceMode}</strong>
                    <div className="salryn-tabular" style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                      {r.orderId.slice(0, 8)} · v{r.orderVersion}
                    </div>
                  </div>
                ),
              },
              { key: "mode", header: "Service Mode", render: (r) => (r.serviceMode === "DineIn" ? "Dine-In" : r.serviceMode) },
              {
                key: "updated",
                header: "Updated",
                tabular: true,
                render: (r) => new Date(r.updatedAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
              },
              {
                key: "state",
                header: "State",
                render: (r) => <StatusBadge tone={STATE_TONE[r.state] ?? "neutral"}>{r.state}</StatusBadge>,
              },
              {
                key: "actions",
                header: "Actions",
                render: (r) => (
                  <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                    <Button
                      size="sm"
                      variant="secondary"
                      leftIcon={<Eye size={14} />}
                      disabled={blocked}
                      onClick={() => void onSelectOrder(r.orderId)}
                    >
                      Open
                    </Button>
                    {r.state === "Open" && permitted("Preparation.Manage") ? (
                      <Button
                        size="sm"
                        variant="accent"
                        leftIcon={<Send size={14} />}
                        disabled={blocked}
                        onClick={() =>
                          void onDispatchIntent(`/api/cafe/orders/${r.orderId}/submit`, {
                            operationId: crypto.randomUUID(),
                            expectedVersion: r.orderVersion,
                          })
                        }
                      >
                        Send to Prep
                      </Button>
                    ) : null}
                    {r.state === "Open" && permitted("Orders.Manage") ? (
                      <Button
                        size="sm"
                        variant="secondary"
                        leftIcon={<PauseCircle size={14} />}
                        disabled={blocked}
                        onClick={() =>
                          void onDispatchIntent(`/api/cafe/orders/${r.orderId}/park`, {
                            operationId: crypto.randomUUID(),
                            expectedVersion: r.orderVersion,
                          })
                        }
                      >
                        Park
                      </Button>
                    ) : null}
                    {r.state === "Parked" && permitted("Orders.Manage") ? (
                      <Button
                        size="sm"
                        variant="accent"
                        leftIcon={<PlayCircle size={14} />}
                        disabled={blocked}
                        onClick={() =>
                          void onDispatchIntent(`/api/cafe/orders/${r.orderId}/resume`, {
                            operationId: crypto.randomUUID(),
                            expectedVersion: r.orderVersion,
                          })
                        }
                      >
                        Resume
                      </Button>
                    ) : null}
                    {r.state === "Completed" && r.completedSaleId && receiptStation && permitted("Printing.Reprint") ? (
                      <Button
                        size="sm"
                        variant="secondary"
                        leftIcon={<Printer size={14} />}
                        loading={busyPrintId === r.orderId}
                        onClick={() => void handleReprintReceipt(r)}
                      >
                        Reprint
                      </Button>
                    ) : null}
                    {(r.state === "Open" || r.state === "Parked") && permitted("Orders.Cancel") ? (
                      <Button
                        size="sm"
                        variant="danger"
                        leftIcon={<XCircle size={14} />}
                        disabled={blocked}
                        onClick={() => {
                          setCancelReason("");
                          setCancelTarget(r);
                        }}
                      >
                        Cancel
                      </Button>
                    ) : null}
                  </div>
                ),
              },
            ]}
          />
        </div>
      </CardBody>

      <Dialog
        open={Boolean(cancelTarget)}
        title={cancelTarget ? `Cancel ${cancelTarget.serviceReference || cancelTarget.orderId.slice(0, 8)}?` : "Cancel Order?"}
        description="Audited cancellation with required reason and durable operation ID."
        onClose={() => setCancelTarget(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setCancelTarget(null)}>
              Back
            </Button>
            <Button
              variant="danger"
              onClick={() => {
                if (!cancelTarget || !cancelReason.trim()) {
                  notify("warning", "Cancellation reason required", "Enter a brief audit reason before cancelling.");
                  return;
                }
                const target = cancelTarget;
                const reason = cancelReason.trim();
                setCancelTarget(null);
                void onDispatchIntent(`/api/cafe/orders/${target.orderId}/cancel`, {
                  operationId: crypto.randomUUID(),
                  expectedVersion: target.orderVersion,
                  reason,
                });
              }}
            >
              Confirm Cancellation
            </Button>
          </>
        }
      >
        <FormField label="Audit Reason" required>
          <TextInput
            placeholder="Customer changed order"
            value={cancelReason}
            onChange={(e) => setCancelReason(e.target.value)}
          />
        </FormField>
      </Dialog>
    </Card>
  );
}
