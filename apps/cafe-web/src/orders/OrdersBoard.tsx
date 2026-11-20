import { Printer, XCircle } from "lucide-react";
import { useMemo, useState } from "react";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  StatusBadge,
  useToast,
  type StatusTone,
} from "@salryn/cafe-ui";
import type { CafeWorkstationClient } from "../cashier/mockCafeClient";
import {
  formatPhp,
  UX_B_DATA_SOURCE,
  type OrderRecord,
  type OrderStatus,
  type ServiceChannel,
} from "../cashier/types";
import { useDurableOperation } from "../cashier/useDurableOperation";

const STATUS_TONE: Record<OrderStatus, StatusTone> = {
  OPEN: "info",
  PREPARING: "copper",
  READY: "success",
  SERVED: "neutral",
  VOIDED: "danger",
};

const NEXT_STATUS: Partial<Record<OrderStatus, { next: OrderStatus; label: string }>> = {
  OPEN: { next: "PREPARING", label: "Start Prep" },
  PREPARING: { next: "READY", label: "Mark Ready" },
  READY: { next: "SERVED", label: "Mark Served" },
};

export type OrdersBoardProps = {
  client: CafeWorkstationClient;
  orders: OrderRecord[];
  onOrderUpdated: (updated: OrderRecord) => void;
};

export function OrdersBoard({ client, orders, onOrderUpdated }: OrdersBoardProps) {
  const { notify } = useToast();
  const [channelFilter, setChannelFilter] = useState<"ALL" | ServiceChannel>("ALL");
  const [voidTarget, setVoidTarget] = useState<OrderRecord | null>(null);
  const transitionOp = useDurableOperation("order-transition");
  const reprintOp = useDurableOperation("order-reprint");

  const visibleOrders = useMemo(
    () => (channelFilter === "ALL" ? orders : orders.filter((o) => o.channel === channelFilter)),
    [orders, channelFilter],
  );

  async function advanceOrder(order: OrderRecord, nextStatus: OrderStatus) {
    const updated = await transitionOp.execute(
      (operationId) => client.transitionOrder({ operationId, orderId: order.id, nextStatus }),
      `${order.id}:${nextStatus}`,
    );
    if (updated) {
      onOrderUpdated(updated);
      notify("success", `Order ${updated.receiptNumber} → ${updated.status}`);
    }
  }

  async function reprint(receiptNumber: string) {
    const res = await reprintOp.execute((operationId) =>
      client.reprintDocument({ operationId, kind: "RECEIPT", reference: receiptNumber }),
    );
    if (res?.printed) {
      notify("info", `Receipt reprint queued · ${receiptNumber}`);
    }
  }

  return (
    <Card>
      <CardHeader
        title="Cafe Orders & Lifecycle"
        subtitle="Track Dine-In, Takeout, and Delivery tickets with durable operation IDs."
        action={<StatusBadge tone="copper">{UX_B_DATA_SOURCE}</StatusBadge>}
      />
      <CardBody>
        <div style={{ display: "grid", gap: 14 }}>
          {transitionOp.error ? (
            <Alert tone="danger" title="Order Transition Failed" description={transitionOp.error} />
          ) : null}
          {transitionOp.unknownOutcome ? (
            <Alert
              tone="warning"
              title={`Unknown Order Outcome · ${transitionOp.unknownOutcome.operationId}`}
              description={transitionOp.unknownOutcome.message}
            />
          ) : null}
          <div role="group" aria-label="Filter orders by channel" style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {(["ALL", "DINE_IN", "TAKEOUT", "DELIVERY"] as const).map((ch) => (
              <Button
                key={ch}
                size="sm"
                variant={channelFilter === ch ? "primary" : "secondary"}
                aria-pressed={channelFilter === ch}
                onClick={() => setChannelFilter(ch)}
              >
                {ch === "ALL" ? "All Channels" : ch.replace("_", "-")}
              </Button>
            ))}
          </div>

          <DataTable
            caption="Cafe Order Lifecycle"
            rows={visibleOrders}
            getRowKey={(row) => row.id}
            columns={[
              { key: "receipt", header: "Receipt", tabular: true, render: (r) => <strong>{r.receiptNumber}</strong> },
              { key: "channel", header: "Channel", render: (r) => `${r.channel.replace("_", "-")} · ${r.tableOrRef}` },
              { key: "summary", header: "Items", render: (r) => r.summary },
              { key: "total", header: "Total", align: "right", tabular: true, render: (r) => formatPhp(r.totalDueCents) },
              {
                key: "status",
                header: "Status",
                render: (r) => <StatusBadge tone={STATUS_TONE[r.status]}>{r.status}</StatusBadge>,
              },
              {
                key: "actions",
                header: "Actions",
                render: (r) => {
                  const next = NEXT_STATUS[r.status];
                  return (
                    <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                      {next ? (
                        <Button
                          size="sm"
                          variant="accent"
                          disabled={transitionOp.inFlight}
                          onClick={() => void advanceOrder(r, next.next)}
                        >
                          {next.label}
                        </Button>
                      ) : null}
                      <Button
                        size="sm"
                        variant="secondary"
                        leftIcon={<Printer size={14} />}
                        onClick={() => void reprint(r.receiptNumber)}
                      >
                        Reprint
                      </Button>
                      {r.status !== "VOIDED" && r.status !== "SERVED" ? (
                        <Button
                          size="sm"
                          variant="danger"
                          leftIcon={<XCircle size={14} />}
                          onClick={() => setVoidTarget(r)}
                        >
                          Void
                        </Button>
                      ) : null}
                    </div>
                  );
                },
              },
            ]}
          />
        </div>
      </CardBody>

      <ConfirmDialog
        open={Boolean(voidTarget)}
        title={voidTarget ? `Void Order ${voidTarget.receiptNumber}?` : "Void Order?"}
        description="Audited void transition with durable operation ID."
        confirmLabel="Void order"
        destructive
        onCancel={() => setVoidTarget(null)}
        onConfirm={() => {
          if (!voidTarget) return;
          const target = voidTarget;
          setVoidTarget(null);
          void advanceOrder(target, "VOIDED");
        }}
      />
    </Card>
  );
}
