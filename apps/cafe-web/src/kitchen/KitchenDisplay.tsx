import { Bluetooth, CheckCircle2, Clock, Printer, RotateCcw } from "lucide-react";
import { useMemo, useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  StatusBadge,
  useToast,
  type StatusTone,
} from "@salryn/cafe-ui";
import type { CafeWorkstationClient } from "../cashier/mockCafeClient";
import {
  UX_B_DATA_SOURCE,
  type KitchenStation,
  type KitchenTicket,
  type KitchenTicketStatus,
} from "../cashier/types";
import { useDurableOperation } from "../cashier/useDurableOperation";

const TICKET_TONE: Record<KitchenTicketStatus, StatusTone> = {
  QUEUED: "info",
  IN_PROGRESS: "copper",
  READY: "success",
  RECALLED: "warning",
};

export type KitchenDisplayProps = {
  client: CafeWorkstationClient;
  tickets: KitchenTicket[];
  onTicketUpdated: (ticket: KitchenTicket) => void;
};

export function KitchenDisplay({ client, tickets, onTicketUpdated }: KitchenDisplayProps) {
  const { notify } = useToast();
  const [stationFilter, setStationFilter] = useState<"ALL" | KitchenStation>("ALL");
  const bumpOp = useDurableOperation("kds-bump");
  const printOp = useDurableOperation("kds-print");

  const visibleTickets = useMemo(
    () => (stationFilter === "ALL" ? tickets : tickets.filter((t) => t.station === stationFilter)),
    [tickets, stationFilter],
  );

  async function handleBump(ticket: KitchenTicket, nextStatus: KitchenTicketStatus) {
    const updated = await bumpOp.execute((operationId) =>
      client.bumpKitchenTicket({ operationId, ticketId: ticket.id, nextStatus }),
    );
    if (updated) {
      onTicketUpdated(updated);
      notify("success", `Ticket ${updated.ticketNumber} → ${updated.status}`);
    }
  }

  async function handleReprint(ticket: KitchenTicket) {
    const res = await printOp.execute((operationId) =>
      client.reprintDocument({ operationId, kind: "KITCHEN", reference: ticket.ticketNumber }),
    );
    if (res?.printed) {
      notify("info", `Kitchen ticket reprinted · ${ticket.ticketNumber}`, "Sent to isolated BT kitchen printer transport.");
    }
  }

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <Card>
        <CardHeader
          title="Kitchen & Barista Ticket Queue"
          subtitle="Touch-friendly bump actions with separate Bluetooth kitchen printer transport."
          action={
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              <StatusBadge tone="copper">{UX_B_DATA_SOURCE}</StatusBadge>
              <StatusBadge tone="info">
                <Bluetooth aria-hidden="true" size={12} /> BT Kitchen Printer: Separate Peripheral
              </StatusBadge>
            </div>
          }
        />
        <CardBody>
          <div role="group" aria-label="Filter by kitchen station" style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {(["ALL", "BARISTA", "HOT_KITCHEN", "PASTRY"] as const).map((st) => (
              <Button
                key={st}
                size="sm"
                variant={stationFilter === st ? "primary" : "secondary"}
                aria-pressed={stationFilter === st}
                onClick={() => setStationFilter(st)}
              >
                {st === "ALL" ? "All Stations" : st.replace("_", " ")}
              </Button>
            ))}
          </div>
        </CardBody>
      </Card>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(290px, 1fr))", gap: 14 }}>
        {visibleTickets.map((ticket) => (
          <Card key={ticket.id}>
            <CardHeader
              title={`${ticket.ticketNumber} · ${ticket.tableOrRef}`}
              subtitle={`${ticket.station.replace("_", " ")} · Receipt ${ticket.receiptNumber}`}
              action={<StatusBadge tone={TICKET_TONE[ticket.status]}>{ticket.status.replace("_", " ")}</StatusBadge>}
            />
            <CardBody>
              <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 10, fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                <Clock aria-hidden="true" size={14} />
                <span className="salryn-tabular">{ticket.elapsedMinutes} min elapsed · {ticket.channel.replace("_", "-")}</span>
              </div>
              <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: 8 }}>
                {ticket.items.map((item, idx) => (
                  <li key={`${ticket.id}-${idx}`} style={{ padding: 10, borderRadius: 8, background: "var(--salryn-surface-subtle)" }}>
                    <div style={{ fontWeight: 700 }}>
                      <span className="salryn-tabular">{item.quantity}×</span> {item.name}
                    </div>
                    {item.modifiers.length > 0 ? (
                      <div style={{ fontSize: "0.8125rem", color: "var(--salryn-dark-copper)", fontWeight: 600 }}>
                        {item.modifiers.join(" · ")}
                      </div>
                    ) : null}
                    {item.note ? (
                      <div style={{ fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>Note: {item.note}</div>
                    ) : null}
                  </li>
                ))}
              </ul>
            </CardBody>
            <CardFooter>
              <Button size="sm" variant="secondary" leftIcon={<Printer size={14} />} onClick={() => void handleReprint(ticket)}>
                Reprint
              </Button>
              {ticket.status === "QUEUED" || ticket.status === "RECALLED" ? (
                <Button size="sm" variant="accent" disabled={bumpOp.inFlight} onClick={() => void handleBump(ticket, "IN_PROGRESS")}>
                  Start Prep
                </Button>
              ) : null}
              {ticket.status === "IN_PROGRESS" ? (
                <Button
                  size="sm"
                  variant="primary"
                  leftIcon={<CheckCircle2 size={15} />}
                  disabled={bumpOp.inFlight}
                  onClick={() => void handleBump(ticket, "READY")}
                >
                  Mark Ready
                </Button>
              ) : null}
              {ticket.status === "READY" ? (
                <Button size="sm" variant="ghost" leftIcon={<RotateCcw size={14} />} disabled={bumpOp.inFlight} onClick={() => void handleBump(ticket, "RECALLED")}>
                  Recall
                </Button>
              ) : null}
            </CardFooter>
          </Card>
        ))}
      </div>
    </div>
  );
}
