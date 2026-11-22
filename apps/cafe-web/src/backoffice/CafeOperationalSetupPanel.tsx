import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  Alert, Button, Card, CardBody, CardHeader, FormField, SelectInput, StatusBadge, TextInput,
} from "@salryn/cafe-ui";
import { HostApiError, parsePhpToCents } from "../api/hostClient";
import type { BackofficeApi } from "./types";
import { BackofficeUnknownOutcomeError, useBackofficeOperation } from "./useBackofficeOperation";

type SetupProps = {
  api: BackofficeApi;
  onSaved?: () => Promise<void>;
};

const grid = {
  display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(195px, 1fr))",
  gap: 12, alignItems: "end",
} as const;

function uncertainNetwork(err: unknown): boolean {
  return err instanceof TypeError ||
    (err instanceof Error && err.name === "AbortError") ||
    (err instanceof HostApiError &&
      (err.status === 0 || err.status >= 500 ||
        err.code === "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN" ||
        err.code === "ERR_NETWORK_UNKNOWN"));
}

/**
 * Customer create currently has no server-side clientRequestId contract.
 * Unlike a C1-C11 mutation, an uncertain HTTP outcome must NEVER be blindly replayed.
 */
export function CafeCustomerSetup({ api, onSaved }: SetupProps) {
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [limit, setLimit] = useState("");
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function createCustomer(e: FormEvent) {
    e.preventDefault();
    if (busy || uncertain || !fullName.trim()) return;
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      await api.createArCustomer({
        fullName: fullName.trim(),
        phone: phone.trim() || null,
        creditLimitCents: limit.trim() ? parsePhpToCents(limit) : null,
      });
      setMessage("Customer created. You can now select this customer at AR checkout.");
      setFullName("");
      setPhone("");
      setLimit("");
      await onSaved?.();
    } catch (err) {
      if (uncertainNetwork(err)) {
        setUncertain(true);
        setError("Customer creation outcome is unknown. Do not submit again. Refresh the customer list and verify whether the account was already created.");
      } else {
        setError(err instanceof Error ? err.message : "Customer creation failed.");
      }
    } finally {
      setBusy(false);
    }
  }

  async function checkCustomerRecords() {
    setBusy(true);
    try {
      const rows = await api.getReceivableCustomers();
      const match = rows.some((r) => r.customer.fullName.trim().toLocaleLowerCase() === fullName.trim().toLocaleLowerCase());
      if (match) {
        setUncertain(false);
        setError(null);
        setMessage("A customer with this name exists. Verify its identity and details before creating another account.");
        await onSaved?.();
      } else {
        setError("No exact-name match in the current customer list. The outcome remains uncertain; check the Host records before attempting a new request.");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not verify customer records.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader title="Customer Accounts for AR / Utang" subtitle="Create a customer before accepting Accounts Receivable or Partial Cash + AR payments. No separate customer database is used." />
      <CardBody>
        <div style={{ display: "grid", gap: 12 }}>
          {error ? <Alert tone={uncertain ? "warning" : "danger"} title={uncertain ? "Check before retrying" : "Could not create customer"} description={error} /> : null}
          {message ? <Alert tone="success" title="Customer directory" description={message} /> : null}
          <form aria-label="Create receivable customer" style={grid} onSubmit={(e) => void createCustomer(e)}>
            <FormField label="Customer name" required><TextInput required value={fullName} onChange={(e) => setFullName(e.target.value)} placeholder="Customer or business name" disabled={uncertain} /></FormField>
            <FormField label="Phone"><TextInput value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="Optional" disabled={uncertain} /></FormField>
            <FormField label="Credit limit (PHP)"><TextInput inputMode="decimal" value={limit} onChange={(e) => setLimit(e.target.value)} placeholder="Optional" disabled={uncertain} /></FormField>
            <Button variant="primary" type="submit" loading={busy} disabled={!fullName.trim() || uncertain}>Save customer</Button>
          </form>
          {uncertain ? <Button variant="secondary" loading={busy} onClick={() => void checkCustomerRecords()}>Check saved customer records</Button> : null}
        </div>
      </CardBody>
    </Card>
  );
}

export function CafePrinterSetup({ api }: SetupProps) {
  const [stations, setStations] = useState<Awaited<ReturnType<BackofficeApi["getPrintStations"]>>>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [name, setName] = useState("Counter Receipt");
  const [kind, setKind] = useState<"CustomerReceipt" | "Kitchen" | "BarDrinks">("CustomerReceipt");
  const [routeKey, setRouteKey] = useState("front-counter");
  const op = useBackofficeOperation("setup-print-station");

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      setStations(await api.getPrintStations());
      setLoadError(null);
    } catch (err) {
      setStations([]);
      setLoadError(err instanceof Error ? err.message : "Printer stations unavailable.");
    } finally {
      setLoading(false);
    }
  }, [api]);

  useEffect(() => { void refresh(); }, [refresh]);

  async function saveStation(e: FormEvent) {
    e.preventDefault();
    const stationName = name.trim();
    if (!stationName || op.inFlight) return;
    const saved = await op.execute(async (operationId) => {
      try {
        await api.createPrintStation({ operationId, name: stationName, stationKind: kind, routeKey: routeKey.trim() });
        return true;
      } catch (err) {
        if (uncertainNetwork(err)) {
          throw new BackofficeUnknownOutcomeError(operationId,
            "Station creation may have succeeded. Keep the same name and route, refresh the list, and retry only with the original operation ID.");
        }
        throw err;
      }
    }, "station:" + kind + ":" + stationName + ":" + routeKey.trim());
    if (saved) await refresh();
  }

  return (
    <Card>
      <CardHeader title="Receipt and Kitchen Printer Stations" subtitle="Register logical stations used for receipt jobs and kitchen tickets. The physical Windows print queue is configured in Store Settings below." action={<Button size="sm" variant="secondary" loading={loading} onClick={() => void refresh()}>Refresh stations</Button>} />
      <CardBody>
        <div style={{ display: "grid", gap: 12 }}>
          {loadError ? <Alert tone="danger" title="Printer station data unavailable" description={loadError} /> : null}
          {op.error ? <Alert tone="danger" title="Station could not be saved" description={op.error} /> : null}
          {op.unknownOutcome ? <Alert tone="warning" title="Printer station result uncertain" description={"Review the station list before retrying this exact request. Operation: " + op.unknownOutcome.operationId} /> : null}
          {stations.length ? (
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              {stations.map((station) => (
                <StatusBadge key={station.stationId} tone={station.isActive ? "success" : "neutral"}>
                  {station.name + " (" + station.stationKind + ")"}
                </StatusBadge>
              ))}
            </div>
          ) : <p style={{ margin: 0 }}>No printer stations registered. Add a receipt or kitchen station before attempting printing.</p>}
          <form aria-label="Create printer station" onSubmit={(e) => void saveStation(e)} style={grid}>
            <FormField label="Station name" required><TextInput required value={name} onChange={(e) => setName(e.target.value)} placeholder="Counter Receipt" /></FormField>
            <FormField label="Station type" required>
              <SelectInput value={kind} onChange={(e) => setKind(e.target.value as "CustomerReceipt" | "Kitchen" | "BarDrinks")}>
                <option value="CustomerReceipt">Customer receipt</option>
                <option value="Kitchen">Kitchen</option>
                <option value="BarDrinks">Bar drinks</option>
              </SelectInput>
            </FormField>
            <FormField label="Station route"><TextInput value={routeKey} onChange={(e) => setRouteKey(e.target.value)} placeholder="front-counter" /></FormField>
            <Button type="submit" variant="primary" loading={op.inFlight} disabled={!name.trim()}>Create printer station</Button>
          </form>
          <Alert tone="info" title="Print jobs are separate from sales" description="Completing a sale saves financial records first. Printing can be retried without charging the customer a second time. Enable Windows print queue and choose the exact printer name in Store Settings." />
        </div>
      </CardBody>
    </Card>
  );
}
