import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  closeHostShift,
  fetchHostShift,
  openHostShift,
  submitCheckout,
  type CheckoutEnvelope,
} from "../api/checkout";
import { ApiError } from "../api/http";
import { fetchProducts, type ProductListItem } from "../api/products";
import { fetchSession } from "../api/session";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  ErrorState,
  LoadingState,
  StatusPill,
  TextInput,
} from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";

type CartLine = {
  product: ProductListItem;
  quantity: number;
};

type CheckoutAttemptState = {
  httpStatus: number | null;
  backendCode: string | null;
  checkoutStatus: string | null;
  receiptNumber: string | null;
  saleId: string | null;
};

const initialAttemptState: CheckoutAttemptState = {
  httpStatus: null,
  backendCode: null,
  checkoutStatus: null,
  receiptNumber: null,
  saleId: null,
};

function toCents(value: string): number {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    return 0;
  }

  return Math.round(parsed * 100);
}

function fromCents(value: number | null | undefined): string {
  return formatMoney((value ?? 0) / 100);
}

function describeBackendError(error: unknown): string {
  if (error instanceof ApiError) {
    const code = error.code ?? "";
    const checkoutStatus = error.checkoutStatus ?? "";

    if (error.status === 401) {
      return "Session expired or login required.";
    }

    if (error.status === 403) {
      return "Your role cannot perform checkout.";
    }

    if (error.status === 409) {
      if (code.includes("OpenShiftRequired") || checkoutStatus === "OpenShiftRequired") {
        return "Open a shift before checkout.";
      }

      if (code === "Sales.NegativeStockNotAllowed" || checkoutStatus === "StockChanged") {
        return "Stock changed or is insufficient. Refresh products before retrying.";
      }

      return error.checkoutMessage || error.message || "Checkout conflict. Review shift and stock state.";
    }

    if (error.status === 400) {
      return error.checkoutMessage || error.message || "Invalid cart or payment. Check quantities and cash tender.";
    }

    if (error.status === 404) {
      return "A cart product is missing or inactive. Refresh products before retrying.";
    }

    const codeSuffix = error.code ? ` Backend code: ${error.code}.` : "";
    return `${error.message}.${codeSuffix}`;
  }

  return error instanceof Error ? error.message : "Unexpected checkout error.";
}

function isOpenShift(state: string | undefined): boolean {
  return stringEquals(state, "Open") || stringEquals(state, "Opened");
}

function stringEquals(left: string | undefined, right: string): boolean {
  return (left ?? "").localeCompare(right, undefined, { sensitivity: "accent" }) === 0;
}

export function CheckoutPage() {
  const [search, setSearch] = useState("");
  const [cartLines, setCartLines] = useState<CartLine[]>([]);
  const [cashAmount, setCashAmount] = useState("");
  const [openingCash, setOpeningCash] = useState("0.00");
  const [backendMessage, setBackendMessage] = useState<string | null>(null);
  const [checkoutResult, setCheckoutResult] = useState<CheckoutEnvelope | null>(null);
  const [attemptState, setAttemptState] = useState<CheckoutAttemptState>(initialAttemptState);
  const [isOpeningShift, setIsOpeningShift] = useState(false);
  const [isClosingShift, setIsClosingShift] = useState(false);
  const [isSubmittingCheckout, setIsSubmittingCheckout] = useState(false);

  const sessionQuery = useQuery({
    queryKey: ["host-checkout-session"],
    queryFn: fetchSession,
    retry: false,
  });

  const shiftQuery = useQuery({
    queryKey: ["host-checkout-shift"],
    queryFn: fetchHostShift,
    retry: false,
  });

  const productsQuery = useQuery({
    queryKey: ["host-checkout-products", search],
    queryFn: () =>
      fetchProducts({
        search: search.trim() || undefined,
        isActive: true,
        page: 1,
        pageSize: 10,
      }),
    retry: false,
  });

  const subtotal = useMemo(
    () => cartLines.reduce((sum, line) => sum + line.product.sellingPrice * line.quantity, 0),
    [cartLines],
  );
  const cashCents = toCents(cashAmount);
  const subtotalCents = Math.round(subtotal * 100);
  const balanceCents = Math.max(0, subtotalCents - cashCents);
  const shift = shiftQuery.data?.shift;
  const shiftOpen = isOpenShift(shift?.shiftState);
  const cartValidationMessage = useMemo(() => {
    if (cartLines.length === 0) {
      return "Add at least one product before checkout.";
    }

    if (!shiftOpen) {
      return "Open a shift before checkout.";
    }

    if (cashCents <= 0) {
      return "Enter a cash payment amount.";
    }

    if (cashCents < subtotalCents) {
      return "Cash payment is below the cart subtotal.";
    }

    return null;
  }, [cartLines.length, cashCents, shiftOpen, subtotalCents]);
  const canSubmitCheckout =
    cartValidationMessage === null && cartLines.length > 0 && !isSubmittingCheckout;

  function addProduct(product: ProductListItem) {
    setCheckoutResult(null);
    setBackendMessage(null);
    setCartLines((current) => {
      const existing = current.find((line) => line.product.id === product.id);
      if (existing) {
        return current.map((line) =>
          line.product.id === product.id ? { ...line, quantity: line.quantity + 1 } : line,
        );
      }

      return [...current, { product, quantity: 1 }];
    });
  }

  function updateQuantity(productId: string, quantity: number) {
    setCheckoutResult(null);
    setCartLines((current) =>
      current
        .map((line) => (line.product.id === productId ? { ...line, quantity } : line))
        .filter((line) => line.quantity > 0),
    );
  }

  async function refreshShiftAndProducts() {
    setBackendMessage(null);
    await Promise.all([shiftQuery.refetch(), productsQuery.refetch()]);
  }

  async function handleOpenShift() {
    setBackendMessage(null);
    setIsOpeningShift(true);
    try {
      const result = await openHostShift({ openingCashCents: toCents(openingCash) });
      setBackendMessage(result.message);
      await shiftQuery.refetch();
    } catch (error) {
      setBackendMessage(describeBackendError(error));
    } finally {
      setIsOpeningShift(false);
    }
  }

  async function handleCloseShift() {
    setBackendMessage(null);
    setIsClosingShift(true);
    try {
      const result = await closeHostShift();
      setBackendMessage(result.message);
      await shiftQuery.refetch();
    } catch (error) {
      setBackendMessage(describeBackendError(error));
    } finally {
      setIsClosingShift(false);
    }
  }

  async function handleSubmitCheckout() {
    if (!canSubmitCheckout || isSubmittingCheckout) {
      return;
    }

    setBackendMessage(null);
    setCheckoutResult(null);
    setAttemptState(initialAttemptState);
    setIsSubmittingCheckout(true);

    try {
      const result = await submitCheckout({
        lines: cartLines.map((line) => ({
          productId: line.product.id,
          quantityX1000: Math.round(line.quantity * 1000),
        })),
        payments: [
          {
            method: "Cash",
            amountCents: cashCents,
          },
        ],
      });

      setCheckoutResult(result);
      setBackendMessage(result.message);
      setAttemptState({
        httpStatus: 200,
        backendCode: result.code,
        checkoutStatus: result.checkout.status,
        receiptNumber: result.checkout.receiptNumber,
        saleId: result.checkout.saleId,
      });
      await shiftQuery.refetch();
    } catch (error) {
      if (error instanceof ApiError) {
        setAttemptState({
          httpStatus: error.status,
          backendCode: error.code ?? null,
          checkoutStatus: error.checkoutStatus ?? null,
          receiptNumber: null,
          saleId: null,
        });
      }

      setBackendMessage(describeBackendError(error));
    } finally {
      setIsSubmittingCheckout(false);
    }
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="POS Checkout"
        subtitle="Host-local checkout wired to the approved backend endpoints."
        status={
          shift ? (
            <StatusPill tone={shiftOpen ? "success" : "warning"}>
              Shift {shift.shiftState}
            </StatusPill>
          ) : null
        }
        actions={
          <Button
            variant="secondary"
            onClick={() => void refreshShiftAndProducts()}
            disabled={shiftQuery.isFetching || productsQuery.isFetching}
          >
            {shiftQuery.isFetching || productsQuery.isFetching ? "Refreshing..." : "Refresh Shift/Products"}
          </Button>
        }
      />

      <Alert tone="primary" title="Real backend path only">
        This page calls GET /api/shift, POST /api/shift/open, POST /api/shift/close, and POST /api/checkout.
        It does not create local success states and does not handle official output, printing, PDF, or product mutation work.
      </Alert>

      {backendMessage ? <Alert tone={checkoutResult ? "success" : "warning"}>{backendMessage}</Alert> : null}
      {cartValidationMessage && !checkoutResult ? (
        <Alert tone="warning">{cartValidationMessage}</Alert>
      ) : null}

      {shiftQuery.isLoading ? <LoadingState label="Reading host shift..." /> : null}
      {shiftQuery.isError ? <ErrorState message={describeBackendError(shiftQuery.error)} /> : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Checkout readiness</CardTitle>
            </CardHeader>
            <CardBody className="space-y-2 text-sm text-slate-600">
              <p>
                Session:{" "}
                {sessionQuery.isLoading
                  ? "Loading..."
                  : sessionQuery.data?.isAuthenticated
                    ? `Authenticated as ${sessionQuery.data.user?.displayName ?? sessionQuery.data.user?.username ?? "operator"}`
                    : "Not authenticated"}
              </p>
              <p>Shift status: {shift?.shiftState ?? "Unknown"}</p>
              <p>Checkout permission required: Sales.Create</p>
              <p>Product source: real backend products API</p>
              <p>Cart line count: {cartLines.length}</p>
              <p>Cart total: {formatMoney(subtotal)}</p>
              <p>Cash tendered: {fromCents(cashCents)}</p>
              <p>Last checkout HTTP status: {attemptState.httpStatus ?? "—"}</p>
              <p>Last backend error code: {attemptState.backendCode ?? "—"}</p>
              <p>Last checkout status: {attemptState.checkoutStatus ?? "—"}</p>
              <p>Last receipt number: {attemptState.receiptNumber ?? "—"}</p>
              <p>Last sale id: {attemptState.saleId ?? "—"}</p>
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <div className="flex flex-wrap items-center justify-between gap-3">
                <CardTitle>Host shift</CardTitle>
                {shift ? <StatusPill tone={shiftOpen ? "success" : "warning"}>{shift.shiftState}</StatusPill> : null}
              </div>
            </CardHeader>
            <CardBody className="space-y-4">
              {shift ? (
                <div className="grid gap-3 text-sm text-slate-600 md:grid-cols-3">
                  <p>Shift ID: {shift.shiftId ?? "—"}</p>
                  <p>Opened: {shift.openedAtUtc ? formatDateTime(shift.openedAtUtc) : "—"}</p>
                  <p>Opening cash: {fromCents(shift.openingCashCents)}</p>
                </div>
              ) : null}

              <div className="flex flex-wrap items-end gap-3">
                <div className="w-48">
                  <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                    Opening cash
                  </label>
                  <TextInput value={openingCash} onChange={(event) => setOpeningCash(event.target.value)} />
                </div>
                <Button onClick={() => void handleOpenShift()} disabled={isOpeningShift || shiftOpen}>
                  {isOpeningShift ? "Opening..." : "Open Shift"}
                </Button>
                <Button variant="danger" onClick={() => void handleCloseShift()} disabled={isClosingShift || !shiftOpen}>
                  {isClosingShift ? "Closing..." : "Close Shift"}
                </Button>
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Product search</CardTitle>
            </CardHeader>
            <CardBody className="space-y-4">
              <TextInput
                placeholder="Search SKU, barcode, or product name..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />

              {productsQuery.isLoading ? <LoadingState label="Loading products..." /> : null}
              {productsQuery.isError ? <ErrorState message={describeBackendError(productsQuery.error)} /> : null}
              {productsQuery.data ? (
                <DataTable
                  columns={[
                    { key: "sku", header: "SKU", render: (row: ProductListItem) => row.sku ?? "—" },
                    { key: "name", header: "Product", render: (row: ProductListItem) => row.name },
                    {
                      key: "price",
                      header: "Price",
                      align: "right",
                      render: (row: ProductListItem) => formatMoney(row.sellingPrice),
                    },
                    {
                      key: "stock",
                      header: "Stock",
                      align: "right",
                      render: (row: ProductListItem) => formatQuantity(row.stockOnHand),
                    },
                    {
                      key: "action",
                      header: "Action",
                      align: "right",
                      render: (row: ProductListItem) => (
                        <Button size="sm" variant="secondary" onClick={() => addProduct(row)}>
                          Add
                        </Button>
                      ),
                    },
                  ]}
                  rows={productsQuery.data.items}
                  getRowKey={(row) => row.id}
                  emptyTitle="No active products found"
                  emptyDescription="Search results come from the real product API."
                />
              ) : null}
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <div className="flex items-center justify-between gap-3">
                <CardTitle>Cart</CardTitle>
                <Button variant="secondary" size="sm" onClick={() => setCartLines([])} disabled={cartLines.length === 0}>
                  Clear
                </Button>
              </div>
            </CardHeader>
            <CardBody>
              <DataTable
                columns={[
                  { key: "product", header: "Product", render: (row: CartLine) => row.product.name },
                  {
                    key: "price",
                    header: "Unit Price",
                    align: "right",
                    render: (row: CartLine) => formatMoney(row.product.sellingPrice),
                  },
                  {
                    key: "qty",
                    header: "Qty",
                    align: "center",
                    render: (row: CartLine) => (
                      <div className="inline-flex items-center gap-2">
                        <Button
                          size="sm"
                          variant="secondary"
                          onClick={() => updateQuantity(row.product.id, row.quantity - 1)}
                        >
                          -
                        </Button>
                        <span className="w-10 text-center">{formatQuantity(row.quantity)}</span>
                        <Button
                          size="sm"
                          variant="secondary"
                          onClick={() => updateQuantity(row.product.id, row.quantity + 1)}
                        >
                          +
                        </Button>
                      </div>
                    ),
                  },
                  {
                    key: "lineTotal",
                    header: "Amount",
                    align: "right",
                    render: (row: CartLine) => formatMoney(row.product.sellingPrice * row.quantity),
                  },
                  {
                    key: "remove",
                    header: "Remove",
                    align: "right",
                    render: (row: CartLine) => (
                      <Button size="sm" variant="ghost" onClick={() => updateQuantity(row.product.id, 0)}>
                        Remove
                      </Button>
                    ),
                  },
                ]}
                rows={cartLines}
                getRowKey={(row) => row.product.id}
                emptyTitle="Cart is empty"
                emptyDescription="Add active products from the backend product search."
              />
            </CardBody>
          </Card>
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Totals</CardTitle>
            </CardHeader>
            <CardBody className="space-y-4 text-sm">
              <div className="flex justify-between">
                <span className="text-slate-500">Subtotal</span>
                <span className="font-semibold text-slate-900">{formatMoney(subtotal)}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-slate-500">Cash payment</span>
                <span className="font-semibold text-slate-900">{fromCents(cashCents)}</span>
              </div>
              <div className="flex justify-between border-t border-slate-200 pt-3">
                <span className="text-slate-500">Balance before backend validation</span>
                <span className="font-semibold text-slate-900">{fromCents(balanceCents)}</span>
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Cash amount
                </label>
                <TextInput value={cashAmount} onChange={(event) => setCashAmount(event.target.value)} />
              </div>
              <Button className="w-full" onClick={() => void handleSubmitCheckout()} disabled={!canSubmitCheckout}>
                {isSubmittingCheckout ? "Submitting to backend..." : "Submit Checkout"}
              </Button>
              <p className="text-xs text-slate-500">
                The backend remains the source of truth for shift, product, stock, payment, and final completion validation.
              </p>
            </CardBody>
          </Card>

          {checkoutResult ? (
            <Card>
              <CardHeader>
                <CardTitle>Backend checkout result</CardTitle>
              </CardHeader>
              <CardBody className="space-y-2 text-sm text-slate-600">
                <p>Status: {checkoutResult.checkout.status}</p>
                <p>Sale reference: {checkoutResult.checkout.receiptNumber ?? "—"}</p>
                <p>Sale ID: {checkoutResult.checkout.saleId ?? "—"}</p>
                <p>Total: {fromCents(checkoutResult.checkout.totalCents)}</p>
                <p>Change: {fromCents(checkoutResult.checkout.changeCents)}</p>
                <p>{checkoutResult.checkout.message}</p>
              </CardBody>
            </Card>
          ) : null}
        </div>
      </div>
    </div>
  );
}