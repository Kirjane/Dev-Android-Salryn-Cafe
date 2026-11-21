import { useEffect, useRef, useState } from "react";
import "./liveCafe.css";

type Session = { isAuthenticated: boolean; user?: { userId: string; displayName: string; permissions: string[] } };
type Item = { productId: string; name: string; sellingPriceCents: number; isTemporarilyUnavailable: boolean };
type Menu = { storeId: string; items: Item[]; variants: {variantId: string; productId: string; name: string; isTemporarilyUnavailable: boolean}[];
  modifierGroups: {groupId: string; name: string; minSelections: number; maxSelections: number}[];
  modifierOptions: {optionId: string; groupId: string; name: string}[];
  itemModifierGroups: {ownerId: string; groupId: string}[]; variantModifierGroups: {ownerId: string; groupId: string}[] };
type Line = { orderLineId: string; productId: string; variantId: string | null; modifierOptionIds: string[]; quantityX1000: number; displayOrder: number };
type Order = { orderId: string; orderVersion: number; state: string; serviceReference: string | null };
type Detail = { order: Order; currentRevision: { lines: { lineTotalCents: number; productNameSnapshot: string }[] } };
type Pending = { storeId: string; actor: string; path: string; body: object; rejection?: string };
class HostError extends Error { constructor(message: string, readonly status: number, readonly code?: string) { super(message); } }
const pendingKey = "salryn.cafe.host.pending.v1";
const money = (cents: number) => new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" }).format(cents / 100);
function cents(value: string): number {
  if (!/^\d+(\.\d{1,2})?$/.test(value)) throw new Error("Enter a nonnegative amount with at most two decimal places.");
  const [whole, fraction = ""] = value.split(".");
  const amount = Number(whole) * 100 + Number(fraction.padEnd(2, "0"));
  if (!Number.isSafeInteger(amount)) throw new Error("Amount is too large.");
  return amount;
}
async function request<T>(path: string, body?: object): Promise<T> {
  const response = await fetch(path, { method: body ? "POST" : "GET", credentials: "same-origin",
    headers: body ? { "Content-Type": "application/json" } : undefined, body: body ? JSON.stringify(body) : undefined });
  const result = await response.json();
  if (!response.ok) throw new HostError(result.error ?? result.detail ?? `Host returned HTTP ${response.status}`, response.status, result.code);
  return result as T;
}
export function LiveCafeWorkstation() {
  const [session, setSession] = useState<Session>({ isAuthenticated: false });
  const [menu, setMenu] = useState<Menu | null>(null), [orders, setOrders] = useState<Order[]>([]);
  const [order, setOrder] = useState<Detail | null>(null), [lines, setLines] = useState<Line[]>([]);
  const [itemId, setItemId] = useState(""), [variant, setVariant] = useState(""), [options, setOptions] = useState<string[]>([]);
  const [username, setUsername] = useState(""), [password, setPassword] = useState("");
  const [serviceMode, setServiceMode] = useState("Takeout"), [reference, setReference] = useState("");
  const [centralOrigin,setCentralOrigin]=useState(""),[centralTenant,setCentralTenant]=useState(""),[centralStore,setCentralStore]=useState(""),[centralPin,setCentralPin]=useState(""),[joinCode,setJoinCode]=useState(""),[syncPassword,setSyncPassword]=useState("");
  const [tender, setTender] = useState("Cash"), [cash, setCash] = useState("0"), [customer, setCustomer] = useState("");
  const [customers, setCustomers] = useState<{id: string; fullName: string}[]>([]);
  const [counted, setCounted] = useState("0"), [message, setMessage] = useState("Connecting to the local Host…");
  const [rejectionReason, setRejectionReason] = useState("");
  const [busy, setBusy] = useState(false), lock = useRef(false);
  const [pending, setPending] = useState<Pending | null>(() => {
    try { return JSON.parse(localStorage.getItem(pendingKey) ?? "null") as Pending | null; } catch { return null; }
  });
  const permitted = (permission: string) => session.user?.permissions.includes(permission) ?? false;
  async function refresh(current = session) {
    if (!current.isAuthenticated) return;
    const [published, saved] = await Promise.all([request<Menu | null>("/api/menu/publication/current"), request<Order[]>("/api/cafe/orders")]);
    setMenu(published); setOrders(saved);
    if (current.user?.permissions.includes("Receivables.View")) {
      const accounts = await request<{customer: {id: string; fullName: string; isActive: boolean}}[]>("/api/customers");
      setCustomers(accounts.map(account => account.customer).filter(account => account.isActive));
    }
  }
  async function run(action: () => Promise<void>) {
    if (lock.current) return;
    lock.current = true; setBusy(true);
    try { await action(); } catch (error) { setMessage(error instanceof Error ? error.message : "Host connection failed. Retained requests can be retried."); }
    finally { lock.current = false; setBusy(false); }
  }
  useEffect(() => { void run(async () => { const current = await request<Session>("/api/session"); setSession(current); await refresh(current); setMessage("Local Host connected."); }); }, []);
  async function dispatch(path: string, body: object) {
    if (!session.user) throw new Error("Sign in before sending a request.");
    if (pending) throw new Error("Retry the retained request before creating another action.");
    if (!menu?.storeId) throw new Error("A Host menu with store authority is required before submitting actions.");
    const intent = { storeId:menu.storeId, actor: session.user.userId, path, body };
    localStorage.setItem(pendingKey, JSON.stringify(intent)); setPending(intent);
    await send(intent);
  }
  async function send(intent: Pending) {
    if (intent.actor !== session.user?.userId) throw new Error("The original operator must sign in to retry this request.");
    if (!menu?.storeId || intent.storeId !== menu.storeId) throw new Error("The retained request belongs to another Host store. Preserve it for administrator reconciliation.");
    let result: {orderId?: string; saleId?: string; receiptNumber?: string};
    try { result = await request<{orderId?: string; saleId?: string; receiptNumber?: string}>(intent.path, intent.body); }
    catch (error) {
      if (error instanceof HostError && (error.status === 400 ||
          (error.status === 409 && ["CafeOrder.ExpectedVersionConflict", "CafeOrder.NotReady", "CafeOrder.SubmittedRevisionConflict", "CafeOrder.AlreadyCompleted"].includes(error.code ?? "")))) {
        const rejected = {...intent, rejection:error.message};
        localStorage.setItem(pendingKey,JSON.stringify(rejected)); setPending(rejected);
      }
      throw error;
    }
    if (/\/orders\/[^/]+\/complete$/.test(intent.path) &&
        (!result.orderId || !result.saleId || !result.receiptNumber || !intent.path.includes(`/${result.orderId}/`)))
      throw new Error("Host did not return matching sale and receipt evidence. The original payment request is retained.");
    localStorage.removeItem(pendingKey); setPending(null);
    if (result?.orderId) { setOrder(await request<Detail>(`/api/cafe/orders/${result.orderId}`)); setLines([]); }
    setMessage(result.receiptNumber ? `Payment confirmed. Receipt ${result.receiptNumber}.` : "Host confirmed the action."); await refresh();
  }
  const total = order?.currentRevision.lines.reduce((sum, line) => sum + line.lineTotalCents, 0) ?? 0;
  const groups = menu?.modifierGroups.filter(group => [...menu.itemModifierGroups.filter(link => link.ownerId === itemId),
    ...menu.variantModifierGroups.filter(link => link.ownerId === variant)].some(link => link.groupId === group.groupId)) ?? [];
  const blocked = busy || !!pending;
  return <main className="live-cafe">
    <header><div><p className="eyebrow">Salryn Cafe · Local Host</p><h1>Cafe workstation</h1></div>
      {session.isAuthenticated && <button disabled={busy} onClick={() => void run(async () => { await request("/api/auth/logout", {}); setSession({isAuthenticated:false}); setMenu(null); setOrders([]); setOrder(null); setLines([]); })}>Sign out</button>}</header>
    <p role="status" aria-live="polite">{message}</p>
    {!session.isAuthenticated ? <form onSubmit={event => { event.preventDefault(); void run(async () => {
      const current = await request<Session>("/api/auth/login", { username, password }); setPassword(""); setSession(current); await refresh(current); setMessage("Signed in to local Host.");
    }); }}><h2>Operator sign in</h2><label>Username<input autoComplete="username" value={username} onChange={e => setUsername(e.target.value)} required /></label>
      <label>Password<input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required /></label><button disabled={busy}>Sign in</button></form> : <>
      <p>Operator: {session.user?.displayName}</p>
      {pending && <section aria-label="Retained request"><h2>Action needs confirmation</h2><p>The original request is retained across reload. Confirm it before creating another action.</p>
        <button disabled={busy || pending.actor !== session.user?.userId} onClick={() => void run(() => send(pending))}>Retry retained request</button>
        {pending.rejection && <><p>Host rejected this request: {pending.rejection}</p><label>Reason to archive rejected request<input value={rejectionReason} onChange={e => setRejectionReason(e.target.value)} /></label>
          <button disabled={busy || !rejectionReason.trim() || pending.actor !== session.user?.userId} onClick={() => void run(async () => {
            const history = JSON.parse(localStorage.getItem("salryn.cafe.host.rejection-history.v1") ?? "[]") as object[];
            localStorage.setItem("salryn.cafe.host.rejection-history.v1", JSON.stringify([...history,{request:pending,reason:rejectionReason.trim(),archivedAtUtc:new Date().toISOString()}]));
            localStorage.removeItem(pendingKey); setPending(null); setRejectionReason(""); setMessage("Rejected request retained in local rejection history. Correct the order before retrying.");
          })}>Archive rejected request</button></>}
      </section>}
      <button disabled={busy} onClick={() => void run(async () => { await refresh(); setMessage("Refreshed from Host."); })}>Refresh from Host</button>
      <details><summary>Central pairing and synchronization</summary>
        <p>Cohost QA profile: Central and this Host must use the same local canonical database. Pair before opening a drawer or creating orders.</p>
        {permitted("StoreTerminal.Manage") && <><label>Approved Central HTTPS loopback origin<input value={centralOrigin} onChange={e=>setCentralOrigin(e.target.value)} /></label>
        <label>Central tenant ID<input value={centralTenant} onChange={e=>setCentralTenant(e.target.value)} /></label>
        <label>Central store ID<input value={centralStore} onChange={e=>setCentralStore(e.target.value)} /></label>
        <label>Independent server signing public key<input value={centralPin} onChange={e=>setCentralPin(e.target.value)} /></label>
        <label>One-time Central join code<input value={joinCode} onChange={e=>setJoinCode(e.target.value)} /></label>
        <button disabled={busy} onClick={()=>void run(async()=>{await request("/api/cafe/central/enroll",{configuration:{httpsOrigin:centralOrigin,tenantId:centralTenant,storeId:centralStore,pinnedServerKey:centralPin},joinCode,deviceName:"Windows Cafe QA Host"});setJoinCode("");setMessage("Enrollment requested. Administrator must approve the exact Windows device before synchronization.");})}>Request Windows device enrollment</button></>}
        <label>Current operator password for Central<input type="password" autoComplete="current-password" value={syncPassword} onChange={e=>setSyncPassword(e.target.value)} /></label>
        <button disabled={busy||!syncPassword} onClick={()=>void run(async()=>{const supplied=syncPassword;setSyncPassword("");const result=await request<{applied:number;pending:number}>("/api/cafe/central/synchronize",{password:supplied});setMessage(`Central confirmed ${result.applied} operations; ${result.pending} remain pending.`);})}>Connect and synchronize Windows operations</button>
      </details>
      <div className="workspace-grid"><section><h2>Published menu</h2>{!menu ? <p>No published menu is available.</p> : <>
        <label>Menu item<select aria-label="Menu item" value={itemId} onChange={e => {setItemId(e.target.value); setVariant(""); setOptions([]);}}><option value="">Choose an item</option>
          {menu.items.map(item => <option key={item.productId} value={item.productId} disabled={item.isTemporarilyUnavailable}>{item.name} · {money(item.sellingPriceCents)}</option>)}</select></label>
        <label>Variant<select aria-label="Variant" value={variant} onChange={e => {setVariant(e.target.value); setOptions([]);}}><option value="">Standard</option>
          {menu.variants.filter(v => v.productId === itemId).map(v => <option key={v.variantId} value={v.variantId} disabled={v.isTemporarilyUnavailable}>{v.name}</option>)}</select></label>
        {groups.map(group => <fieldset key={group.groupId}><legend>{group.name} · choose {group.minSelections}–{group.maxSelections}</legend>
          {menu.modifierOptions.filter(o => o.groupId === group.groupId).map(option => <label key={option.optionId}><input type="checkbox" checked={options.includes(option.optionId)} onChange={e => setOptions(previous => e.target.checked ? [...previous,option.optionId] : previous.filter(id => id !== option.optionId))} />{option.name}</label>)}</fieldset>)}
        <button disabled={blocked || !itemId || !!order} onClick={() => {setLines(previous => [...previous, {orderLineId:crypto.randomUUID(),productId:itemId,variantId:variant || null,modifierOptionIds:options,quantityX1000:1000,displayOrder:previous.length}]);}}>Add item</button>
      </>}<h2>New order</h2><label>Service mode<select aria-label="Service mode" value={serviceMode} onChange={e => setServiceMode(e.target.value)}><option>Takeout</option><option>DineIn</option></select></label>
        <label>Table / reference<input value={reference} onChange={e => setReference(e.target.value)} /></label>
        <ul>{lines.map(line => <li key={line.orderLineId}>{menu?.items.find(item => item.productId === line.productId)?.name} × 1</li>)}</ul>
        <button disabled={blocked || !lines.length || !!order || !permitted("Orders.Create")} onClick={() => void run(() => dispatch("/api/cafe/orders/revision", {operationId:crypto.randomUUID(), orderId:crypto.randomUUID(), expectedVersion:0, serviceMode, serviceReference:reference, lines}))}>Save order</button>
      </section><section><h2>Orders & preparation</h2><ul>{orders.map(saved => <li key={saved.orderId}><button disabled={busy || !!lines.length} onClick={() => void run(async () => setOrder(await request<Detail>(`/api/cafe/orders/${saved.orderId}`)))}>{saved.serviceReference || "Takeout"} · {saved.state}</button></li>)}</ul>
        {order && <><h3>Current order · {order.order.state}</h3><ul>{order.currentRevision.lines.map((line,index) => <li key={index}>{line.productNameSnapshot} · {money(line.lineTotalCents)}</li>)}</ul><p>Confirmed order total: {money(total)}</p>
          {order.order.state === "Open" && <button disabled={blocked || !permitted("Orders.Manage")} onClick={() => void run(() => dispatch(`/api/cafe/orders/${order.order.orderId}/submit`,{operationId:crypto.randomUUID(),expectedVersion:order.order.orderVersion}))}>Send to preparation</button>}
          {["Submitted","Preparing"].includes(order.order.state) && <button disabled={blocked || !permitted("Preparation.Manage")} onClick={() => void run(() => dispatch(`/api/cafe/orders/${order.order.orderId}/preparation`,{operationId:crypto.randomUUID(),expectedVersion:order.order.orderVersion,targetState:order.order.state === "Submitted" ? "Preparing" : "Ready"}))}>{order.order.state === "Submitted" ? "Mark preparing" : "Mark ready"}</button>}
          {order.order.state === "Ready" && <><label>Tender<select aria-label="Tender" value={tender} onChange={e => setTender(e.target.value)}><option value="Cash">Cash</option><option value="AccountsReceivable" disabled={!permitted("Receivables.CreateCreditSale")}>Accounts Receivable</option><option value="Partial" disabled={!permitted("Receivables.CreateCreditSale")}>Partial Cash + AR</option></select></label>
            {tender !== "AccountsReceivable" && <label>Cash amount (PHP)<input inputMode="decimal" value={cash} onChange={e => setCash(e.target.value)} /></label>}
            {tender !== "Cash" && <label>Customer<select aria-label="Customer" value={customer} onChange={e => setCustomer(e.target.value)}><option value="">Choose customer</option>{customers.map(account => <option key={account.id} value={account.id}>{account.fullName}</option>)}</select></label>}
            <button disabled={blocked || !permitted("Orders.Complete") || !permitted("Sales.Create") || (tender !== "Cash" && !customer)} onClick={() => void run(async () => {
              const cashCents = tender === "AccountsReceivable" ? 0 : cents(cash);
              if (tender === "Partial" && (cashCents <= 0 || cashCents >= total)) throw new Error("Partial cash must be positive and below the order total.");
              await dispatch(`/api/cafe/orders/${order.order.orderId}/complete`, {completionOperationId:crypto.randomUUID(),saleCommittedOperationId:crypto.randomUUID(),financialDecisionOperationId:crypto.randomUUID(),financialDecisionId:crypto.randomUUID(),expectedVersion:order.order.orderVersion,customerId:tender === "Cash" ? null : customer,
                payments:[...(cashCents > 0 ? [{method:"Cash",amountCents:cashCents}] : []),...(tender !== "Cash" ? [{method:"AccountsReceivable",amountCents:total-cashCents}] : [])]});
            })}>Complete payment</button></>}
          <button disabled={blocked || !!lines.length} onClick={() => setOrder(null)}>Start another order</button>
        </>}
      </section></div>
      {permitted("Shift.OpenClose") && <section><h2>Drawer · physical count</h2><label>Counted cash (PHP)<input inputMode="decimal" value={counted} onChange={e => setCounted(e.target.value)} /></label>
        <button disabled={blocked} onClick={() => void run(() => dispatch("/api/shift/close",{countedCashCents:cents(counted),physicalCountConfirmed:true,clientRequestId:crypto.randomUUID(),note:"Physical count from Cafe workstation"}))}>Close shift with physical count</button></section>}
    </>}
  </main>;
}
