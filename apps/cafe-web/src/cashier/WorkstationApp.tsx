import { ClipboardList, Coffee, UtensilsCrossed } from "lucide-react";
import { useMemo, useState } from "react";
import {
  CafeShell,
  ErrorBoundary,
  ToastProvider,
  type CafeShellNavItem,
  type StatusTone,
} from "@salryn/cafe-ui";
import { CashierWorkstation } from "./CashierWorkstation";
import {
  createMockCafeClient,
  INITIAL_MOCK_ORDERS,
  INITIAL_MOCK_TICKETS,
  type CafeWorkstationClient,
} from "./mockCafeClient";
import type { KitchenTicket, OrderRecord, StationTrustState } from "./types";
import { OrdersBoard } from "../orders/OrdersBoard";
import { KitchenDisplay } from "../kitchen/KitchenDisplay";

const TRUST_BADGE: Record<StationTrustState, { label: string; tone: StatusTone }> = {
  PAIRED_ONLINE: { label: "Paired · Online", tone: "success" },
  OFFLINE_LEASE_VALID: { label: "Offline Lease Valid", tone: "warning" },
  OFFLINE_LEASE_EXPIRED: { label: "Lease Expired", tone: "danger" },
  REVOKED: { label: "Device Revoked", tone: "danger" },
};

export type WorkstationAppProps = {
  client?: CafeWorkstationClient;
  initialTrustState?: StationTrustState;
};

export function WorkstationApp({
  client: providedClient,
  initialTrustState = "PAIRED_ONLINE",
}: WorkstationAppProps) {
  const client = useMemo(() => providedClient ?? createMockCafeClient(), [providedClient]);
  const [activeNav, setActiveNav] = useState("cashier");
  const [trustState] = useState<StationTrustState>(initialTrustState);
  const [orders, setOrders] = useState<OrderRecord[]>(INITIAL_MOCK_ORDERS);
  const [tickets, setTickets] = useState<KitchenTicket[]>(INITIAL_MOCK_TICKETS);

  const navItems: CafeShellNavItem[] = [
    { id: "cashier", label: "Cafe Cashier", group: "Front of House", Icon: Coffee },
    { id: "orders", label: "Orders & Lifecycle", group: "Front of House", Icon: ClipboardList, badge: orders.filter((o) => o.status !== "SERVED" && o.status !== "VOIDED").length },
    { id: "kitchen", label: "Kitchen & Barista", group: "Preparation", Icon: UtensilsCrossed, badge: tickets.filter((t) => t.status !== "READY").length },
  ];

  return (
    <ErrorBoundary>
      <ToastProvider>
        <CafeShell
          storeName="Salryn Cafe"
          stationLabel="Front Counter · Workstation (MOCKED_UI)"
          connectionStatus={TRUST_BADGE[trustState]}
          operatorName="Shift Cashier"
          roleName="Cashier"
          navItems={navItems}
          activeNav={activeNav}
          onNavigate={setActiveNav}
          onSignOut={() => undefined}
        >
          {activeNav === "cashier" ? (
            <CashierWorkstation
              client={client}
              trustState={trustState}
              onCommittedSale={(receipt) => {
                setOrders((prev) => [
                  {
                    id: `ord-${receipt.receiptNumber}`,
                    receiptNumber: receipt.receiptNumber,
                    channel: receipt.channel,
                    tableOrRef: receipt.tableOrRef,
                    summary: "New cafe order",
                    totalDueCents: receipt.totalDueCents,
                    tenderMode: receipt.tenderMode,
                    status: "OPEN",
                    createdAtLabel: "Just now",
                  },
                  ...prev,
                ]);
              }}
            />
          ) : null}

          {activeNav === "orders" ? (
            <OrdersBoard
              client={client}
              orders={orders}
              onOrderUpdated={(updated) =>
                setOrders((prev) => prev.map((o) => (o.id === updated.id ? updated : o)))
              }
            />
          ) : null}

          {activeNav === "kitchen" ? (
            <KitchenDisplay
              client={client}
              tickets={tickets}
              onTicketUpdated={(updated) =>
                setTickets((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
              }
            />
          ) : null}
        </CafeShell>
      </ToastProvider>
    </ErrorBoundary>
  );
}
