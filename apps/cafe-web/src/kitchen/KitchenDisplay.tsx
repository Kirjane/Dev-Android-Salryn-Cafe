import { CheckCircle2, Clock, Printer } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  StatusBadge,
  useToast,
  type StatusTone,
} from "@salryn/cafe-ui";
import { clearPendingPrintOp, getOrCreatePendingPrintOp } from "../api/hostClient";
import { isKitchenStation } from "../cashier/types";
import type { CafeOrderDetail, CafeOrderSummary, CafePrintStation, WorkstationApi } from "../cashier/types";

const TICKET_TONE: Record<string, StatusTone> = {
  Submitted: "info",
  Preparing: "copper",
  Ready: "success",
};

export type KitchenDisplayProps = {
  api: WorkstationApi;
  orders: CafeOrderSummary[];
  printStations: CafePrintStation[];
  permissions: string[];
  blocked: boolean;
  onDispatchIntent: (path: string, body: Record<string, unknown>) => Promise<boolean>;
};

export function KitchenDisplay({
  api,
  orders,
  printStations,
  permissions,
  blocked,
  onDispatchIntent,
}: KitchenDisplayProps) {
  const { notify } = useToast();
  const [stateFilter, setStateFilter] = useState<"ALL" | "Submitted" | "Preparing" | "Ready">("ALL");
  const [detailsById, setDetailsById] = useState<Record<string, CafeOrderDetail>>({});
  const [detailErrorsById, setDetailErrorsById] = useState<Record<string, string>>({});
  const [printingOrderId, setPrintingOrderId] = useState<string | null>(null);
  const printLockRef = useRef(false);
  const pendingPrintOpsRef = useRef<Record<string, string>>({});

  const permitted = (perm: string) => permissions.includes(perm);
  const kitchenStation = useMemo(
    () => printStations.find(isKitchenStation) ?? null,
    [printStations],
  );

  const kitchenOrders = useMemo(
    () =>
      orders.filter((o) => {
        if (!["Submitted", "Preparing", "Ready"].includes(o.state)) return false;
        if (stateFilter !== "ALL" && o.state !== stateFilter) return false;
        return true;
      }),
    [orders, stateFilter],
  );

  const loadOrderDetail = useCallback(
    async (orderId: string) => {
      try {
        const detail = await api.getOrderDetail(orderId);
        setDetailsById((prev) => ({ ...prev, [orderId]: detail }));
        setDetailErrorsById((prev) => {
          if (!(orderId in prev)) return prev;
          const next = { ...prev };
          delete next[orderId];
          return next;
        });
      } catch (error) {
        setDetailErrorsById((prev) => ({
          ...prev,
          [orderId]: error instanceof Error ? error.message : "Could not load preparation lines.",
        }));
      }
    },
    [api],
  );

  useEffect(() => {
    const needed = kitchenOrders.filter(
      (o) => detailsById[o.orderId]?.order.orderVersion !== o.orderVersion && !(o.orderId in detailErrorsById),
    );
    if (needed.length === 0) return;
    for (const o of needed) {
      void loadOrderDetail(o.orderId);
    }
  }, [kitchenOrders, detailsById, detailErrorsById, loadOrderDetail]);

  async function handlePrintKitchen(summary: CafeOrderSummary, detail?: CafeOrderDetail) {
    if (!kitchenStation || printLockRef.current) return;
    printLockRef.current = true;
    setPrintingOrderId(summary.orderId);
    const revisionId = detail?.currentRevision.revisionId ?? summary.currentRevisionId;
    const opKey = `kitchen:${summary.orderId}:${revisionId}`;
    const operationId = pendingPrintOpsRef.current[opKey] ?? getOrCreatePendingPrintOp(opKey);
    pendingPrintOpsRef.current[opKey] = operationId;
    try {
      await api.printKitchenTicket({
        operationId,
        stationId: kitchenStation.stationId,
        orderId: summary.orderId,
        orderRevisionId: revisionId,
        copyKind: "Original",
      });
      delete pendingPrintOpsRef.current[opKey];
      clearPendingPrintOp(opKey);
      notify("info", `Kitchen ticket sent · ${kitchenStation.name}`, `${summary.serviceReference || summary.serviceMode} queued.`);
    } catch (error) {
      notify("danger", "Kitchen ticket failed", error instanceof Error ? error.message : "Could not send kitchen ticket.");
    } finally {
      printLockRef.current = false;
      setPrintingOrderId(null);
    }
  }

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <Card>
        <CardHeader
          title="Kitchen & Barista Ticket Queue"
          subtitle="Live preparation queue synced from Host SQLite orders."
          action={
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              <StatusBadge tone="success">Host SQLite Live</StatusBadge>
              <StatusBadge tone={kitchenStation ? "info" : "neutral"}>
                {kitchenStation ? `Kitchen Printer: ${kitchenStation.name}` : "No Kitchen Printer Configured"}
              </StatusBadge>
            </div>
          }
        />
        <CardBody>
          <div role="group" aria-label="Filter kitchen queue by status" style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {(["ALL", "Submitted", "Preparing", "Ready"] as const).map((st) => (
              <Button
                key={st}
                size="sm"
                variant={stateFilter === st ? "primary" : "secondary"}
                aria-pressed={stateFilter === st}
                onClick={() => setStateFilter(st)}
              >
                {st === "ALL" ? "All Active Tickets" : st}
              </Button>
            ))}
          </div>
        </CardBody>
      </Card>

      {kitchenOrders.length === 0 ? (
        <Card>
          <CardBody>
            <div style={{ textAlign: "center", padding: "24px 12px", color: "var(--salryn-mocha)" }}>
              No orders are currently in preparation. Send an open order from Cashier or Orders to see it here.
            </div>
          </CardBody>
        </Card>
      ) : (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(min(100%, 290px), 1fr))", gap: 14 }}>
          {kitchenOrders.map((summary) => {
            const detail = detailsById[summary.orderId];
            const detailError = detailErrorsById[summary.orderId];
            return (
              <Card key={summary.orderId}>
                <CardHeader
                  title={`${summary.serviceReference || summary.serviceMode}`}
                  subtitle={`${summary.serviceMode} · Order ${summary.orderId.slice(0, 8)} · v${summary.orderVersion}`}
                  action={<StatusBadge tone={TICKET_TONE[summary.state] ?? "info"}>{summary.state}</StatusBadge>}
                />
                <CardBody>
                  <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 10, fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                    <Clock aria-hidden="true" size={14} />
                    <span className="salryn-tabular">
                      Submitted {new Date(summary.updatedAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
                    </span>
                  </div>
                  {detail ? (
                    <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: 8 }}>
                      {detail.currentRevision.lines.map((line) => (
                        <li key={line.orderLineId} style={{ padding: 10, borderRadius: 8, background: "var(--salryn-surface-subtle)" }}>
                          <div style={{ fontWeight: 700 }}>
                            <span className="salryn-tabular">{Math.max(1, Math.round(line.quantityX1000 / 1000))}×</span>{" "}
                            {line.productNameSnapshot}
                            {line.variantNameSnapshot ? ` (${line.variantNameSnapshot})` : ""}
                          </div>
                          {line.modifierSelections.length > 0 ? (
                            <div style={{ fontSize: "0.8125rem", color: "var(--salryn-dark-copper)", fontWeight: 600 }}>
                              {line.modifierSelections.map((m) => m.optionNameSnapshot).join(" · ")}
                            </div>
                          ) : null}
                          {line.note ? (
                            <div style={{ fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>Note: {line.note}</div>
                          ) : null}
                        </li>
                      ))}
                    </ul>
                  ) : detailError ? (
                    <div style={{ display: "grid", gap: 8 }}>
                      <Alert tone="danger" title="Unable to load order lines" description={detailError} />
                      <div>
                        <Button size="sm" variant="secondary" onClick={() => void loadOrderDetail(summary.orderId)}>
                          Retry loading lines
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div style={{ fontSize: "0.875rem", color: "var(--salryn-mocha)" }}>Loading preparation lines…</div>
                  )}
                </CardBody>
                <CardFooter>
                  {kitchenStation && permitted("Printing.Print") ? (
                    <Button
                      size="sm"
                      variant="secondary"
                      leftIcon={<Printer size={14} />}
                      loading={printingOrderId === summary.orderId}
                      onClick={() => void handlePrintKitchen(summary, detail)}
                    >
                      Print Kitchen Ticket
                    </Button>
                  ) : null}
                  {summary.state === "Submitted" && permitted("Preparation.Manage") ? (
                    <Button
                      size="sm"
                      variant="accent"
                      disabled={blocked}
                      onClick={() =>
                        void onDispatchIntent(`/api/cafe/orders/${summary.orderId}/preparation`, {
                          operationId: crypto.randomUUID(),
                          expectedVersion: summary.orderVersion,
                          targetState: "Preparing",
                        })
                      }
                    >
                      Start Prep
                    </Button>
                  ) : null}
                  {summary.state === "Preparing" && permitted("Preparation.Manage") ? (
                    <Button
                      size="sm"
                      variant="primary"
                      leftIcon={<CheckCircle2 size={15} />}
                      disabled={blocked}
                      onClick={() =>
                        void onDispatchIntent(`/api/cafe/orders/${summary.orderId}/preparation`, {
                          operationId: crypto.randomUUID(),
                          expectedVersion: summary.orderVersion,
                          targetState: "Ready",
                        })
                      }
                    >
                      Mark Ready
                    </Button>
                  ) : null}
                </CardFooter>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
