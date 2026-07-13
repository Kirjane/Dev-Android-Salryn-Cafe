import { type ReactNode, useEffect, useMemo, useState } from "react";
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
import { printReceiptForSale, type ReceiptPrintResult } from "../api/sales";
import { fetchSession } from "../api/session";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
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

type CheckoutReceiptMetadata = CheckoutEnvelope["checkout"] & {
  receiptSnapshotSaved?: boolean;
  fileReceiptStatus?: string;
  fileReceiptPath?: string | null;
  fileReceiptError?: string | null;
};

type CompletedSaleState = {
  receiptNumber: string | null;
  saleId: string | null;
  totalCents: number | null;
  amountPaidCents: number;
  changeCents: number | null;
  fileReceiptStatus: string | null;
  fileReceiptPath: string | null;
  fileReceiptError: string | null;
};

type StockState = {
  label: string;
  tone: "neutral" | "primary" | "success" | "warning" | "danger" | "info";
  canAdd: boolean;
  helper: string;
};

const initialAttemptState: CheckoutAttemptState = {
  httpStatus: null,
  backendCode: null,
  checkoutStatus: null,
  receiptNumber: null,
  saleId: null,
};

const checkoutPageSize = 25;

function toCents(value: string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.round(parsed * 100) : 0;
}

function fromCents(value: number | null | undefined): string {
  return formatMoney((value ?? 0) / 100);
}

function shortId(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  return value.length <= 8 ? value : value.slice(0, 8);
}

export function describeCheckoutBackendError(error: unknown): string {
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
  return (state ?? "").localeCompare("Open", undefined, { sensitivity: "accent" }) === 0 ||
    (state ?? "").localeCompare("Opened", undefined, { sensitivity: "accent" }) === 0;
}

function stockState(product: ProductListItem): StockState {
  if (!product.isActive) {
    return { label: "Inactive", tone: "danger", canAdd: false, helper: "Product is inactive." };
  }

  if (!product.trackStock) {
    return { label: "Untracked", tone: "info", canAdd: true, helper: "Stock is not tracked for this item." };
  }

  if (product.stockOnHand <= 0) {
    return { label: "Out of stock", tone: "danger", canAdd: false, helper: "Tracked item has no available stock." };
  }

  if (product.stockOnHand <= product.reorderLevel) {
    return {
      label: "Low stock",
      tone: "warning",
      canAdd: true,
      helper: `${formatQuantity(product.stockOnHand)} left; reorder at ${formatQuantity(product.reorderLevel)}.`,
    };
  }

  return { label: "In stock", tone: "success", canAdd: true, helper: `${formatQuantity(product.stockOnHand)} available.` };
}

function maxQuantity(product: ProductListItem): number | null {
  return product.trackStock ? Math.max(0, Math.floor(product.stockOnHand)) : null;
}

function Dialog({ title, children, footer, onClose }: { title: string; children: ReactNode; footer: ReactNode; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4" role="dialog" aria-modal="true">
      <div className="max-h-[90vh] w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-2xl">
        <div className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4">
          <div>
            <h2 className="text-lg font-semibold text-slate-950">{title}</h2>
            <p className="mt-1 text-xs text-slate-500">Esc closes this dialog. Enter confirms only from checkout review.</p>
          </div>
          <Button variant="ghost" size="sm" onClick={onClose}>Close</Button>
        </div>
        <div className="max-h-[62vh] overflow-y-auto px-5 py-4">{children}</div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-200 bg-slate-50 px-5 py-4">{footer}</div>
      </div>
    </div>
  );
}

function printTone(status: string | null | undefined): "success" | "warning" | "danger" | "info" {
  if (status === "Succeeded") {
    return "success";
  }

  if (status === "Failed") {
    return "danger";
  }

  if (status === "Disabled/Unavailable") {
    return "warning";
  }

  return "info";
}

export function CheckoutPage() {
  const [search, setSearch] = useState("");
  const [productPage, setProductPage] = useState(1);
  const [cartLines, setCartLines] = useState<CartLine[]>([]);
  const [cashAmount, setCashAmount] = useState("");
  const [openingCash, setOpeningCash] = useState("0.00");
  const [backendMessage, setBackendMessage] = useState<string | null>(null);
  const [checkoutResult, setCheckoutResult] = useState<CheckoutEnvelope | null>(null);
  const [completedSale, setCompletedSale] = useState<CompletedSaleState | null>(null);
  const [showConfirmCheckout, setShowConfirmCheckout] = useState(false);
  const [attemptState, setAttemptState] = useState<CheckoutAttemptState>(initialAttemptState);
  const [lastRefreshAt, setLastRefreshAt] = useState<string | null>(null);
  const [isOpeningShift, setIsOpeningShift] = useState(false);
  const [isClosingShift, setIsClosingShift] = useState(false);
  const [isSubmittingCheckout, setIsSubmittingCheckout] = useState(false);
  const [isPrintingReceipt, setIsPrintingReceipt] = useState(false);
  const [printResult, setPrintResult] = useState<ReceiptPrintResult | null>(null);

  const sessionQuery = useQuery({ queryKey: ["host-checkout-session"], queryFn: fetchSession, retry: false });
  const shiftQuery = useQuery({ queryKey: ["host-checkout-shift"], queryFn: fetchHostShift, retry: false });
  const productsQuery = useQuery({
    queryKey: ["host-checkout-products", search, productPage],
    queryFn: () => fetchProducts({ search: search.trim() || undefined, isActive: true, page: productPage, pageSize: checkoutPageSize }),
    retry: false,
  });

  const subtotal = useMemo(() => cartLines.reduce((sum, line) => sum + line.product.sellingPrice * line.quantity, 0), [cartLines]);
  const itemQuantityTotal = useMemo(() => cartLines.reduce((sum, line) => sum + line.quantity, 0), [cartLines]);
  const cashCents = toCents(cashAmount);
  const subtotalCents = Math.round(subtotal * 100);
  const estimatedChangeCents = cashCents >= subtotalCents ? cashCents - subtotalCents : 0;
  const shift = shiftQuery.data?.shift;
  const shiftOpen = isOpenShift(shift?.shiftState);
  const operatorLabel = sessionQuery.data?.user?.displayName ?? sessionQuery.data?.user?.username ?? "Not signed in";
  const roleLabel = sessionQuery.data?.user?.roleName ?? "—";
  const productResponse = productsQuery.data;
  const canGoPrevious = (productResponse?.pageNumber ?? productPage) > 1;
  const canGoNext = productResponse ? productResponse.pageNumber < productResponse.totalPages : false;

  const cartValidationMessage = useMemo(() => {
    if (completedSale) {
      return "Start a new sale before submitting again.";
    }

    if (!shiftOpen) {
      return "No open shift.";
    }

    if (cartLines.length === 0) {
      return "No cart lines yet.";
    }

    if (cashCents <= 0) {
      return "Cash tender is missing.";
    }

    if (cashCents < subtotalCents) {
      return "Cash tender is below subtotal.";
    }

    if (isSubmittingCheckout) {
      return "Checkout is submitting.";
    }

    return null;
  }, [cartLines.length, cashCents, completedSale, isSubmittingCheckout, shiftOpen, subtotalCents]);

  const canReviewCheckout = cartValidationMessage === null && cartLines.length > 0;

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "F9") {
        event.preventDefault();
        if (canReviewCheckout && !showConfirmCheckout && !completedSale) {
          setShowConfirmCheckout(true);
        }
        return;
      }

      if (event.key === "Enter" && showConfirmCheckout) {
        event.preventDefault();
        void handleSubmitCheckout();
        return;
      }

      if (event.key === "F5" && completedSale) {
        event.preventDefault();
        void startNewSale();
        return;
      }

      if (event.key === "Escape") {
        if (showConfirmCheckout) {
          event.preventDefault();
          setShowConfirmCheckout(false);
        }
        if (completedSale) {
          event.preventDefault();
          setCompletedSale(null);
        }
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [canReviewCheckout, completedSale, showConfirmCheckout]);

  function handleSearchChange(value: string) {
    setSearch(value);
    setProductPage(1);
  }

  function addProduct(product: ProductListItem) {
    if (!shiftOpen) {
      setBackendMessage("Open a shift before adding products.");
      return;
    }

    const state = stockState(product);
    if (!state.canAdd) {
      setBackendMessage(`${product.name} cannot be added: ${state.helper}`);
      return;
    }

    setCheckoutResult(null);
    setCompletedSale(null);
    setPrintResult(null);
    setBackendMessage(null);
    setCartLines((current) => {
      const existing = current.find((line) => line.product.id === product.id);
      const max = maxQuantity(product);
      if (existing) {
        const nextQuantity = existing.quantity + 1;
        if (max !== null && nextQuantity > max) {
          setBackendMessage(`${product.name} has only ${formatQuantity(max)} available.`);
          return current;
        }
        return current.map((line) => (line.product.id === product.id ? { ...line, quantity: nextQuantity } : line));
      }

      if (max !== null && max < 1) {
        setBackendMessage(`${product.name} is out of stock.`);
        return current;
      }

      return [...current, { product, quantity: 1 }];
    });
  }

  function updateQuantity(productId: string, quantity: number) {
    setCheckoutResult(null);
    setCompletedSale(null);
    setPrintResult(null);
    setCartLines((current) =>
      current
        .map((line) => {
          if (line.product.id !== productId) {
            return line;
          }
          const numericQuantity = Number.isFinite(quantity) ? Math.floor(quantity) : 0;
          const nextQuantity = Math.max(0, numericQuantity);
          const max = maxQuantity(line.product);
          if (max !== null && nextQuantity > max) {
            setBackendMessage(`${line.product.name} has only ${formatQuantity(max)} available.`);
            return { ...line, quantity: max };
          }
          return { ...line, quantity: nextQuantity };
        })
        .filter((line) => line.quantity > 0),
    );
  }

  function clearCart() {
    setCartLines([]);
    setCashAmount("");
    setCheckoutResult(null);
    setCompletedSale(null);
    setPrintResult(null);
    setShowConfirmCheckout(false);
    setAttemptState(initialAttemptState);
    setBackendMessage(null);
  }

  async function startNewSale() {
    clearCart();
    await refreshShiftAndProducts();
  }

  async function refreshShiftAndProducts() {
    setBackendMessage(null);
    await Promise.all([sessionQuery.refetch(), shiftQuery.refetch(), productsQuery.refetch()]);
    setLastRefreshAt(new Date().toISOString());
  }

  async function handleOpenShift() {
    setBackendMessage(null);
    setIsOpeningShift(true);
    try {
      const result = await openHostShift({ openingCashCents: toCents(openingCash) });
      setBackendMessage(result.message);
      await Promise.all([shiftQuery.refetch(), productsQuery.refetch()]);
      setLastRefreshAt(new Date().toISOString());
    } catch (error) {
      setBackendMessage(describeCheckoutBackendError(error));
    } finally {
      setIsOpeningShift(false);
    }
  }

  async function handleCloseShift() {
    setBackendMessage(null);
    setIsClosingShift(true);
    try {
      const result = await closeHostShift();
      clearCart();
      setBackendMessage(result.message);
      await shiftQuery.refetch();
      setLastRefreshAt(new Date().toISOString());
    } catch (error) {
      setBackendMessage(describeCheckoutBackendError(error));
    } finally {
      setIsClosingShift(false);
    }
  }

  function reviewCheckout() {
    if (!canReviewCheckout) {
      setBackendMessage(cartValidationMessage ?? "Checkout is not ready.");
      return;
    }

    setBackendMessage(null);
    setShowConfirmCheckout(true);
  }

  async function handleSubmitCheckout() {
    if (!canReviewCheckout || isSubmittingCheckout) {
      return;
    }

    setBackendMessage(null);
    setCheckoutResult(null);
    setCompletedSale(null);
    setPrintResult(null);
    setAttemptState(initialAttemptState);
    setIsSubmittingCheckout(true);

    try {
      const result = await submitCheckout({
        lines: cartLines.map((line) => ({ productId: line.product.id, quantityX1000: Math.round(line.quantity * 1000) })),
        payments: [{ method: "Cash", amountCents: cashCents }],
      });

      const receiptMetadata = result.checkout as CheckoutReceiptMetadata;
      setCheckoutResult(result);
      setCompletedSale({
        receiptNumber: result.checkout.receiptNumber,
        saleId: result.checkout.saleId,
        totalCents: result.checkout.totalCents,
        amountPaidCents: cashCents,
        changeCents: result.checkout.changeCents,
        fileReceiptStatus: receiptMetadata.fileReceiptStatus ?? null,
        fileReceiptPath: receiptMetadata.fileReceiptPath ?? null,
        fileReceiptError: receiptMetadata.fileReceiptError ?? null,
      });
      setShowConfirmCheckout(false);
      setBackendMessage(result.message);
      setAttemptState({
        httpStatus: 200,
        backendCode: result.code,
        checkoutStatus: result.checkout.status,
        receiptNumber: result.checkout.receiptNumber,
        saleId: result.checkout.saleId,
      });
      await productsQuery.refetch();
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
      setBackendMessage(describeCheckoutBackendError(error));
    } finally {
      setIsSubmittingCheckout(false);
    }
  }

  async function handlePrintCompletedReceipt() {
    if (!completedSale?.saleId || isPrintingReceipt) {
      return;
    }

    setIsPrintingReceipt(true);
    setPrintResult(null);
    try {
      const result = await printReceiptForSale(completedSale.saleId);
      setPrintResult(result.print);
    } catch (error) {
      setPrintResult({
        succeeded: false,
        status: "Failed",
        saleId: completedSale.saleId,
        receiptNumber: completedSale.receiptNumber,
        source: "PersistedReceiptSnapshot",
        adapterName: "Unknown",
        message: "Receipt print request failed. Sale remains committed.",
        errorMessage: error instanceof Error ? error.message : "Unknown print error.",
        printedAtUtc: null,
      });
    } finally {
      setIsPrintingReceipt(false);
    }
  }

  function renderProductCard(product: ProductListItem) {
    const state = stockState(product);
    const alreadyInCart = cartLines.find((line) => line.product.id === product.id)?.quantity ?? 0;
    return (
      <div key={product.id} className="rounded-lg border border-slate-200 bg-white p-3 shadow-sm transition-colors hover:border-blue-200 hover:bg-blue-50/40">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 space-y-2">
            <div>
              <p className="truncate text-sm font-semibold text-slate-950">{product.name}</p>
              <p className="text-xs text-slate-500">SKU {product.sku ?? "—"} · Barcode {product.barcode ?? "—"}</p>
            </div>
            <div className="flex flex-wrap gap-1.5">
              <StatusPill tone="neutral">{product.categoryName ?? "No category"}</StatusPill>
              <StatusPill tone="neutral">{product.brandName ?? "No brand"}</StatusPill>
              <StatusPill tone={state.tone}>{state.label}</StatusPill>
            </div>
            <p className="text-xs text-slate-500">{state.helper}</p>
          </div>
          <div className="shrink-0 text-right">
            <p className="text-base font-bold text-slate-950">{formatMoney(product.sellingPrice)}</p>
            <p className="text-xs text-slate-500">per {product.unitName}</p>
            {alreadyInCart > 0 ? <p className="mt-1 text-xs font-semibold text-blue-700">In cart: {formatQuantity(alreadyInCart)}</p> : null}
            <Button size="sm" className="mt-2" variant={state.canAdd ? "primary" : "secondary"} onClick={() => addProduct(product)} disabled={!state.canAdd || !shiftOpen}>Add</Button>
          </div>
        </div>
      </div>
    );
  }

  function renderShiftLockedState() {
    return (
      <Card className="border-amber-300 bg-amber-50/70">
        <CardHeader>
          <div className="flex items-start justify-between gap-3">
            <div>
              <CardTitle>Cashier workstation locked</CardTitle>
              <p className="mt-1 text-sm text-amber-900">Open a cashier shift before product search, cart, payment, and checkout controls become available.</p>
            </div>
            <StatusPill tone="warning">Shift required</StatusPill>
          </div>
        </CardHeader>
        <CardBody className="grid gap-4 text-sm text-amber-950 lg:grid-cols-[1fr_auto] lg:items-end">
          <div>
            <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-amber-900">Opening cash</label>
            <TextInput value={openingCash} onChange={(event) => setOpeningCash(event.target.value)} />
            <p className="mt-2 text-xs text-amber-800">Locked state intentionally hides cashier product/cart/payment controls until the shift opens.</p>
          </div>
          <Button onClick={() => void handleOpenShift()} disabled={isOpeningShift || shiftQuery.isLoading}>{isOpeningShift ? "Opening..." : "Open Shift"}</Button>
        </CardBody>
      </Card>
    );
  }

  function renderWorkstation() {
    return (
      <div className="grid min-h-0 gap-4 xl:h-[calc(100vh-16rem)] xl:min-h-[36rem] xl:grid-cols-[minmax(18rem,1fr)_minmax(22rem,1.05fr)_24rem]">
        <Card className="flex min-h-[28rem] min-w-0 flex-col xl:min-h-0">
          <CardHeader className="shrink-0">
            <div className="flex items-center justify-between gap-3">
              <CardTitle>Product search</CardTitle>
              {productResponse ? <span className="text-xs font-semibold text-slate-500">{productResponse.totalCount} result{productResponse.totalCount === 1 ? "" : "s"}</span> : null}
            </div>
          </CardHeader>
          <CardBody className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
            <TextInput className="min-h-12 shrink-0 text-base" placeholder="Search SKU, product, or barcode text..." value={search} onChange={(event) => handleSearchChange(event.target.value)} autoComplete="off" />
            <p className="shrink-0 text-xs text-slate-500">Active products from /api/products. No extra input-device behavior is added in this lane.</p>
            {productsQuery.isLoading ? <LoadingState label="Loading products..." /> : null}
            {productsQuery.isError ? <ErrorState message={describeCheckoutBackendError(productsQuery.error)} /> : null}
            {productResponse ? (
              <>
                <div className="min-h-0 flex-1 space-y-2 overflow-y-auto overscroll-contain pr-1">
                  {productResponse.items.length > 0 ? productResponse.items.map(renderProductCard) : <div className="rounded-lg border border-dashed border-slate-300 p-6 text-sm text-slate-500">No active products found. Try SKU, barcode text, or product name.</div>}
                </div>
                <div className="flex shrink-0 items-center justify-between border-t border-slate-100 pt-3 text-xs text-slate-500">
                  <span>Page {productResponse.pageNumber} of {Math.max(1, productResponse.totalPages)}</span>
                  <div className="flex gap-2">
                    <Button size="sm" variant="secondary" onClick={() => setProductPage((page) => Math.max(1, page - 1))} disabled={!canGoPrevious || productsQuery.isFetching}>Prev</Button>
                    <Button size="sm" variant="secondary" onClick={() => setProductPage((page) => page + 1)} disabled={!canGoNext || productsQuery.isFetching}>Next</Button>
                  </div>
                </div>
              </>
            ) : null}
          </CardBody>
        </Card>

        <Card className="flex min-h-[28rem] min-w-0 flex-col xl:min-h-0">
          <CardHeader className="shrink-0">
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle>Active cart</CardTitle>
                <p className="mt-1 text-xs text-slate-500">{cartLines.length} line{cartLines.length === 1 ? "" : "s"} · {formatQuantity(itemQuantityTotal)} item{itemQuantityTotal === 1 ? "" : "s"}</p>
              </div>
              <Button variant="secondary" size="sm" onClick={clearCart} disabled={cartLines.length === 0}>Clear</Button>
            </div>
          </CardHeader>
          <CardBody className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1">
            {cartLines.length === 0 ? (
              <div className="rounded-lg border border-dashed border-slate-300 p-8 text-center text-sm text-slate-500">Search a product, then press Add. Cart lines stay visible until New Sale is selected after checkout.</div>
            ) : (
              <div className="space-y-3">
                {cartLines.map((line) => {
                  const max = maxQuantity(line.product);
                  const stock = stockState(line.product);
                  return (
                    <div key={line.product.id} className="rounded-lg border border-slate-200 bg-white p-3 shadow-sm">
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-semibold text-slate-950">{line.product.name}</p>
                          <p className="text-xs text-slate-500">SKU {line.product.sku ?? "—"} · {line.product.unitName} · {formatMoney(line.product.sellingPrice)} each</p>
                          <div className="mt-2 flex flex-wrap gap-1.5"><StatusPill tone={stock.tone}>{stock.label}</StatusPill>{line.product.categoryName ? <StatusPill tone="neutral">{line.product.categoryName}</StatusPill> : null}</div>
                          {max !== null && line.quantity >= max ? <p className="mt-2 text-xs font-semibold text-amber-700">Reached known stock limit.</p> : null}
                        </div>
                        <Button size="sm" variant="ghost" onClick={() => updateQuantity(line.product.id, 0)}>Remove</Button>
                      </div>
                      <div className="mt-3 grid gap-3 border-t border-slate-100 pt-3 sm:grid-cols-[auto_1fr_auto] sm:items-center">
                        <div className="inline-flex items-center rounded-md border border-slate-300 bg-slate-50">
                          <Button size="sm" variant="ghost" onClick={() => updateQuantity(line.product.id, Math.max(0, line.quantity - 1))}>−</Button>
                          <input className="h-8 w-14 border-x border-slate-200 bg-white text-center text-sm font-semibold text-slate-900 focus:outline-none" type="number" min={0} max={max ?? undefined} value={line.quantity} onChange={(event) => updateQuantity(line.product.id, Number(event.target.value))} />
                          <Button size="sm" variant="ghost" onClick={() => updateQuantity(line.product.id, line.quantity + 1)} disabled={max !== null && line.quantity >= max}>+</Button>
                        </div>
                        <p className="text-xs text-slate-500">{max === null ? "Untracked item; backend still validates sale." : `${formatQuantity(max)} known stock.`}</p>
                        <div className="text-right"><p className="text-xs text-slate-500">Line total</p><p className="text-base font-bold text-slate-950">{formatMoney(line.product.sellingPrice * line.quantity)}</p></div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </CardBody>
        </Card>

        <Card className="flex min-h-[28rem] flex-col border-slate-300 xl:min-h-0">
          <CardHeader className="shrink-0"><CardTitle>Payment / checkout</CardTitle></CardHeader>
          <CardBody className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto overscroll-contain">
            <div className="rounded-lg bg-slate-950 p-4 text-white"><div className="flex items-center justify-between text-sm text-slate-300"><span>Subtotal</span><span>{cartLines.length} line(s)</span></div><p className="mt-2 text-3xl font-bold tracking-tight">{formatMoney(subtotal)}</p></div>
            <div>
              <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">Cash tendered</label>
              <TextInput className="min-h-12 text-lg font-semibold" value={cashAmount} onChange={(event) => setCashAmount(event.target.value)} placeholder="0.00" inputMode="decimal" />
              <div className="mt-2 grid grid-cols-3 gap-2"><Button size="sm" variant="secondary" onClick={() => setCashAmount(subtotal.toFixed(2))} disabled={subtotal <= 0}>Exact</Button><Button size="sm" variant="secondary" onClick={() => setCashAmount("500.00")}>500</Button><Button size="sm" variant="secondary" onClick={() => setCashAmount("1000.00")}>1000</Button></div>
            </div>
            <div className="rounded-lg border border-slate-200 bg-slate-50 p-4"><div className="flex items-center justify-between text-sm text-slate-500"><span>Change due</span><span>{cashCents < subtotalCents ? "Insufficient cash" : "Ready"}</span></div><p className={`mt-1 text-2xl font-bold ${cashCents < subtotalCents ? "text-red-700" : "text-emerald-700"}`}>{fromCents(estimatedChangeCents)}</p></div>
            {cartValidationMessage ? <Alert tone={completedSale ? "success" : "warning"} title="Checkout readiness">{cartValidationMessage}</Alert> : <Alert tone="success" title="Ready">Press Review Checkout or F9 to verify totals before committing the sale.</Alert>}
            <div className="mt-auto space-y-3 border-t border-slate-100 pt-3">
              <Button className="min-h-14 w-full text-base" variant="success" onClick={reviewCheckout} disabled={!canReviewCheckout}>{isSubmittingCheckout ? "Completing..." : "Review Checkout"}</Button>
              <div className="rounded-lg bg-slate-100 p-3 text-xs text-slate-600"><p className="font-semibold text-slate-800">Keyboard hints</p><p>F9 = Review Checkout · Enter = Confirm when review is open · F5 = New Sale after completion · Esc = Close/cancel dialog</p><p className="mt-1">No extra input-device or Fast Add behavior is added in this lane.</p></div>
            </div>
          </CardBody>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-4 overflow-hidden">
      <CommandBar
        title="POS Checkout"
        subtitle="Cashier workstation using wired local session, shift, product, checkout, receipt file status, and POS-M2 printer adapter status."
        status={<div className="flex flex-wrap items-center gap-2"><StatusPill tone={sessionQuery.data?.isAuthenticated ? "success" : "warning"}>{operatorLabel}</StatusPill><StatusPill tone={shiftOpen ? "success" : "warning"}>Shift {shift?.shiftState ?? "Unknown"}</StatusPill></div>}
        actions={<Button variant="secondary" onClick={() => void refreshShiftAndProducts()} disabled={shiftQuery.isFetching || productsQuery.isFetching || sessionQuery.isFetching}>{shiftQuery.isFetching || productsQuery.isFetching || sessionQuery.isFetching ? "Refreshing..." : "Refresh"}</Button>}
      />
      <Card className="border-slate-300"><CardBody className="grid gap-3 py-3 text-sm lg:grid-cols-[1fr_1fr_1fr_auto] lg:items-center"><div><p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Operator</p><p className="font-semibold text-slate-950">{operatorLabel}</p><p className="text-xs text-slate-500">Role: {roleLabel}</p></div><div><p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Shift</p><div className="mt-1 flex flex-wrap items-center gap-2"><StatusPill tone={shiftOpen ? "success" : "warning"}>{shift?.shiftState ?? "Unknown"}</StatusPill><span className="font-mono text-xs text-slate-500">{shortId(shift?.shiftId)}</span></div></div><div><p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Opened / cash</p><p className="font-semibold text-slate-800">{shift?.openedAtUtc ? formatDateTime(shift.openedAtUtc) : "No open shift"}</p><p className="text-xs text-slate-500">Opening cash: {fromCents(shift?.openingCashCents)}</p></div><div className="flex flex-wrap items-end gap-2 lg:justify-end">{shiftOpen ? <Button variant="danger" onClick={() => void handleCloseShift()} disabled={isClosingShift}>{isClosingShift ? "Closing..." : "Close Shift"}</Button> : null}</div></CardBody></Card>
      {backendMessage ? <Alert tone={completedSale ? "success" : "warning"}>{backendMessage}</Alert> : null}
      {shiftQuery.isLoading ? <LoadingState label="Reading host shift..." /> : null}
      {shiftQuery.isError ? <ErrorState message={describeCheckoutBackendError(shiftQuery.error)} /> : null}
      {!shiftOpen ? renderShiftLockedState() : renderWorkstation()}
      <details className="rounded-lg border border-slate-200 bg-white p-4 text-sm text-slate-600 shadow-card"><summary className="cursor-pointer text-sm font-semibold text-slate-900">Checkout diagnostics / runtime proof</summary><div className="mt-4 grid gap-2 md:grid-cols-2 xl:grid-cols-4"><p>Session: {sessionQuery.isLoading ? "Loading..." : sessionQuery.data?.isAuthenticated ? `Authenticated as ${operatorLabel}` : "Not authenticated"}</p><p>Shift status: {shift?.shiftState ?? "Unknown"}</p><p>Permission required: Sales.Create</p><p>Product source: real backend products API</p><p>Cart line count: {cartLines.length}</p><p>Cart item quantity: {formatQuantity(itemQuantityTotal)}</p><p>Cart total: {formatMoney(subtotal)}</p><p>Cash tendered: {fromCents(cashCents)}</p><p>Local validation: {cartValidationMessage ?? "Ready for review"}</p><p>Submit lock: {isSubmittingCheckout ? "Locked" : "Unlocked"}</p><p>Print status: {printResult?.status ?? "NotAttempted"}</p><p>Last checkout HTTP status: {attemptState.httpStatus ?? "—"}</p><p>Last backend code: {attemptState.backendCode ?? "—"}</p><p>Last checkout status: {attemptState.checkoutStatus ?? "—"}</p><p>Last receipt number: {attemptState.receiptNumber ?? "—"}</p><p>Last sale ID: {attemptState.saleId ?? "—"}</p><p>Last refresh: {lastRefreshAt ? formatDateTime(lastRefreshAt) : "—"}</p></div></details>
      {showConfirmCheckout ? (
        <Dialog title="Review Checkout" onClose={() => setShowConfirmCheckout(false)} footer={<><Button variant="secondary" onClick={() => setShowConfirmCheckout(false)} disabled={isSubmittingCheckout}>Cancel</Button><Button variant="success" onClick={() => void handleSubmitCheckout()} disabled={isSubmittingCheckout || !canReviewCheckout}>{isSubmittingCheckout ? "Committing..." : "Confirm Sale"}</Button></>}>
          <div className="space-y-4 text-sm text-slate-700"><Alert tone="warning" title="Final review before sale commit">Sale commits only after Confirm Sale. Printing is attempted only after a committed sale and only when Print Receipt is clicked.</Alert><div className="grid gap-3 sm:grid-cols-2"><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Cart</p><p className="text-lg font-semibold text-slate-950">{cartLines.length} line(s) · {formatQuantity(itemQuantityTotal)} item(s)</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Total</p><p className="text-lg font-semibold text-slate-950">{formatMoney(subtotal)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Cash tendered</p><p className="text-lg font-semibold text-slate-950">{fromCents(cashCents)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Change</p><p className="text-lg font-semibold text-emerald-700">{fromCents(estimatedChangeCents)}</p></div></div><div className="max-h-56 overflow-y-auto rounded-lg border border-slate-200">{cartLines.map((line) => <div key={line.product.id} className="flex justify-between gap-3 border-b border-slate-100 px-3 py-2 last:border-b-0"><div><p className="font-semibold text-slate-900">{line.product.name}</p><p className="text-xs text-slate-500">{formatQuantity(line.quantity)} × {formatMoney(line.product.sellingPrice)}</p></div><p className="font-semibold text-slate-900">{formatMoney(line.quantity * line.product.sellingPrice)}</p></div>)}</div></div>
        </Dialog>
      ) : null}
      {completedSale ? (
        <Dialog
          title="Sale Completed"
          onClose={() => setCompletedSale(null)}
          footer={<><Button className="min-h-12" variant="secondary" onClick={() => void handlePrintCompletedReceipt()} disabled={!completedSale.saleId || isPrintingReceipt}>{isPrintingReceipt ? "Printing..." : "Print Receipt"}</Button><Button className="min-h-12" variant="success" onClick={() => void startNewSale()}>New Sale (F5)</Button></>}
        >
          <div className="space-y-4 text-sm text-slate-700">
            <div className="rounded-xl border border-emerald-200 bg-emerald-50 p-5 text-center"><p className="text-xs font-semibold uppercase tracking-wide text-emerald-700">Cash sale committed</p><p className="mt-2 text-2xl font-bold text-emerald-900">{completedSale.receiptNumber ?? "Receipt pending"}</p><p className="mt-1 text-xs text-emerald-800">This checkout receipt number is an internal POS reference only.</p><p className="mt-1 font-mono text-xs text-emerald-800">Sale ID: {completedSale.saleId ?? "—"}</p></div>
            <div className="grid gap-3 sm:grid-cols-3"><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Total</p><p className="text-lg font-semibold text-slate-950">{fromCents(completedSale.totalCents)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Amount paid</p><p className="text-lg font-semibold text-slate-950">{fromCents(completedSale.amountPaidCents)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Change</p><p className="text-lg font-semibold text-emerald-700">{fromCents(completedSale.changeCents)}</p></div></div>
            <Alert tone={completedSale.fileReceiptStatus === "Written" ? "success" : "warning"} title="Receipt file status">{completedSale.fileReceiptStatus === "Written" ? `Receipt file created${completedSale.fileReceiptPath ? `: ${completedSale.fileReceiptPath}` : "."}` : completedSale.fileReceiptStatus ? `Receipt file status: ${completedSale.fileReceiptStatus}${completedSale.fileReceiptError ? ` — ${completedSale.fileReceiptError}` : ""}` : "Receipt file status was not included in this checkout response."}</Alert>
            {printResult ? <Alert tone={printTone(printResult.status)} title="Printer adapter status">{printResult.message}{printResult.errorMessage ? ` — ${printResult.errorMessage}` : ""} Source: {printResult.source}. Adapter: {printResult.adapterName}.</Alert> : <Alert tone="info" title="Printer adapter status">Printing is optional. Sale remains committed if printer is disabled, unavailable, or failed.</Alert>}
            <p className="text-xs text-slate-500">Print Receipt uses the persisted receipt snapshot/file source through the printer adapter. It does not start a new sale. New Sale remains a separate action.</p>
            {checkoutResult ? <p className="rounded-md bg-slate-100 p-3 text-slate-700">{checkoutResult.checkout.message}</p> : null}
          </div>
        </Dialog>
      ) : null}
    </div>
  );
}
