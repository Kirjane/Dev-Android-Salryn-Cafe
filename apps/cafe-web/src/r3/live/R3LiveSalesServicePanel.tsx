import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, ToastProvider } from "@salryn/cafe-ui";
import { createLiveHostApi } from "../../api/hostClient";
import { CashierWorkstation } from "../../cashier/CashierWorkstation";
import { OrdersBoard } from "../../orders/OrdersBoard";
import { KitchenDisplay } from "../../kitchen/KitchenDisplay";
import type {
  CafeOrderDetail,
  CafeOrderSummary,
  CafePrintStation,
  CustomerAccount,
  HostSession,
  MenuPublication,
} from "../../cashier/types";
import { DurableIntentBanner } from "./DurableIntentBanner";
import { useDurableIntent, type DurableIntentAuthority } from "./durableIntent";

type SalesView = "pos" | "orders" | "kitchen";

type OrderMutationPayload = {
  operationId: string;
  path: string;
  body: Record<string, unknown>;
};

function SalesServiceInner({
  view,
  session,
  authority,
  onNavigateView,
  onDataChanged,
}: {
  view: SalesView;
  session: HostSession;
  authority: DurableIntentAuthority | null;
  onNavigateView: (next: SalesView) => void;
  onDataChanged?: () => void;
}) {
  const api = useMemo(() => createLiveHostApi(), []);
  const permissions = session.user?.permissions ?? [];
  const [menu, setMenu] = useState<MenuPublication | null>(null);
  const [orders, setOrders] = useState<CafeOrderSummary[]>([]);
  const [activeOrder, setActiveOrder] = useState<CafeOrderDetail | null>(null);
  const [customers, setCustomers] = useState<CustomerAccount[]>([]);
  const [printStations, setPrintStations] = useState<CafePrintStation[]>([]);
  const [lastCompletion, setLastCompletion] = useState<{
    orderId: string;
    saleId: string;
    receiptNumber: string;
  } | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const orderIntent = useDurableIntent<OrderMutationPayload, Record<string, unknown>>(
    "pos-order-mutation",
    authority,
  );

  const refresh = useCallback(async () => {
    setRefreshing(true);
    setErrorMsg(null);
    try {
      const [publishedMenu, orderList] = await Promise.all([
        api.getPublishedMenu().catch(() => null),
        permissions.includes("Orders.View") ? api.getOrders().catch(() => []) : Promise.resolve([]),
      ]);
      setMenu(publishedMenu);
      setOrders(orderList);

      if (permissions.includes("Receivables.View")) {
        api.getCustomers().then(setCustomers).catch(() => setCustomers([]));
      }
      if (permissions.includes("Orders.View") || permissions.includes("Settings.Manage")) {
        api.getPrintStations().then(setPrintStations).catch(() => setPrintStations([]));
      }
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : "Failed to load live POS/Order state.");
    } finally {
      setRefreshing(false);
    }
  }, [api, permissions]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const applySuccessResult = useCallback(
    async (path: string, res: Record<string, unknown>) => {
      const orderIdMatch = path.match(/\/api\/cafe\/orders\/([0-9a-f-]{36})\//i);
      const mutatedOrderId =
        (typeof res.orderId === "string" && res.orderId) ||
        (orderIdMatch ? orderIdMatch[1] : null);

      if (path.endsWith("/complete") && typeof res.saleId === "string") {
        setLastCompletion({
          orderId: String(res.orderId ?? mutatedOrderId ?? ""),
          saleId: res.saleId,
          receiptNumber: String(res.receiptNumber ?? ""),
        });
        if (mutatedOrderId) {
          const detail = await api.getOrderDetail(mutatedOrderId).catch(() => null);
          setActiveOrder(detail);
        } else {
          setActiveOrder(null);
        }
      } else if (mutatedOrderId) {
        const detail = await api.getOrderDetail(mutatedOrderId).catch(() => null);
        setActiveOrder(detail);
      }
      await refresh();
      onDataChanged?.();
    },
    [api, refresh, onDataChanged],
  );

  const dispatchIntent = useCallback(
    async (path: string, body: Record<string, unknown>): Promise<boolean> => {
      setErrorMsg(null);
      const opId =
        (typeof body.completionOperationId === "string" && body.completionOperationId) ||
        (typeof body.operationId === "string" && body.operationId) ||
        crypto.randomUUID();

      const res = await orderIntent.execute(
        { path, body },
        () => ({ operationId: opId, path, body }),
        (payload) => api.sendRawIntent(payload.path, payload.body),
      );

      if (res.ok) {
        await applySuccessResult(path, res.value);
        return true;
      }
      setErrorMsg(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
      return false;
    },
    [api, orderIntent, applySuccessResult],
  );

  const handleRetryPending = async () => {
    if (!orderIntent.pending) return;
    const targetPath = orderIntent.pending.payload.path;
    const res = await orderIntent.retryPending((payload) => api.sendRawIntent(payload.path, payload.body));
    if (res.ok) {
      await applySuccessResult(targetPath, res.value);
    } else {
      setErrorMsg(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleSelectOrder = async (orderId: string) => {
    try {
      setErrorMsg(null);
      const detail = await api.getOrderDetail(orderId);
      setActiveOrder(detail);
      onNavigateView("pos");
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : "Failed to load selected order detail.");
    }
  };

  const blocked =
    orderIntent.inFlight ||
    Boolean(orderIntent.pending) ||
    Boolean(orderIntent.corruptReason) ||
    orderIntent.authorityMismatch;

  return (
    <div style={{ display: "grid", gap: 14 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">
            {view === "pos" ? "POS Counter" : view === "orders" ? "Orders Board" : "Kitchen Display (KDS)"}
          </h1>
          <p className="r3-page-subtitle">
            Live Host order engine — Cash, AR (Utang), and Partial Cash+AR only, with authority-bound durable operation IDs.
          </p>
        </div>
        <div className="r3-page-actions">
          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void refresh()} disabled={refreshing || orderIntent.inFlight}>
            Refresh Live Orders
          </button>
        </div>
      </div>

      {errorMsg && <Alert tone="danger" title="Host Order Notice" description={errorMsg} />}

      <DurableIntentBanner
        label="Order Mutation"
        operationId={orderIntent.pending?.payload.operationId ?? null}
        pending={orderIntent.pending}
        corruptReason={orderIntent.corruptReason}
        authorityMismatch={orderIntent.authorityMismatch}
        inFlight={orderIntent.inFlight}
        onRetry={() => void handleRetryPending()}
        onClearRejected={() => {
          orderIntent.clearPending(false);
          setErrorMsg(null);
        }}
        onClearCorrupt={() => {
          orderIntent.clearPending(true);
          setErrorMsg(null);
        }}
      />

      {view === "pos" && (
        <CashierWorkstation
          api={api}
          menu={menu}
          customers={customers}
          printStations={printStations}
          permissions={permissions}
          activeOrder={activeOrder}
          lastCompletion={lastCompletion}
          blocked={blocked}
          onDispatchIntent={dispatchIntent}
          onClearActiveOrder={() => setActiveOrder(null)}
        />
      )}

      {view === "orders" && (
        <OrdersBoard
          api={api}
          orders={orders}
          printStations={printStations}
          permissions={permissions}
          blocked={blocked}
          onDispatchIntent={dispatchIntent}
          onSelectOrder={handleSelectOrder}
        />
      )}

      {view === "kitchen" && (
        <KitchenDisplay
          api={api}
          orders={orders}
          printStations={printStations}
          permissions={permissions}
          blocked={blocked}
          onDispatchIntent={dispatchIntent}
        />
      )}
    </div>
  );
}

export function R3LiveSalesServicePanel(props: {
  view: SalesView;
  session: HostSession;
  authority: DurableIntentAuthority | null;
  onNavigateView: (next: SalesView) => void;
  onDataChanged?: () => void;
}) {
  return (
    <ToastProvider>
      <SalesServiceInner {...props} />
    </ToastProvider>
  );
}
