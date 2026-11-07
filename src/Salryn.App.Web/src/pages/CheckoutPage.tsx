import { type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { closeHostShift, fetchHostShift, openHostShift, submitCheckout, type CheckoutEnvelope } from "../api/checkout";
import { ApiError } from "../api/http";
import { fastAddUnknownBarcode } from "../api/fastAddProduct";
import { fetchProducts, fetchProductUnits, type ProductListItem } from "../api/products";
import { printReceiptForSale, type ReceiptPrintResult } from "../api/receiptPrint";
import { fetchSession } from "../api/session";
import { fetchLicenseStatus } from "../api/license";
import { createCustomer, fetchReceivableCustomers } from "../api/receivables";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, ErrorState, LoadingState, StatusPill, TextInput, useNotifications } from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";
import { safeNotificationDescription } from "../utils/notificationError";

type CartLine = { product: ProductListItem; quantity: number };
type CheckoutAttemptState = { httpStatus: number | null; backendCode: string | null; checkoutStatus: string | null; receiptNumber: string | null; saleId: string | null };
type CheckoutReceiptMetadata = CheckoutEnvelope["checkout"] & { receiptSnapshotSaved?: boolean; fileReceiptStatus?: string; fileReceiptPath?: string | null; fileReceiptError?: string | null };
type CompletedSaleState = { receiptNumber: string | null; saleId: string | null; totalCents: number | null; amountPaidCents: number; changeCents: number | null; fileReceiptStatus: string | null; fileReceiptPath: string | null; fileReceiptError: string | null };
type StockState = { label: string; tone: "neutral" | "primary" | "success" | "warning" | "danger" | "info"; canAdd: boolean; helper: string };

const initialAttemptState: CheckoutAttemptState = { httpStatus: null, backendCode: null, checkoutStatus: null, receiptNumber: null, saleId: null };
const checkoutPageSize = 25;

function toCents(value: string): number { const parsed = Number(value); return Number.isFinite(parsed) ? Math.round(parsed * 100) : 0; }
function fromCents(value: number | null | undefined): string { return formatMoney((value ?? 0) / 100); }
function toNumber(value: string): number { const parsed = Number(value); return Number.isFinite(parsed) ? parsed : 0; }
function shortId(value: string | null | undefined): string { return value ? value.slice(0, 8) : "—"; }
function normalizeBarcode(value: string | null | undefined): string { return (value ?? "").trim(); }
function exactBarcodeMatch(products: ProductListItem[], barcode: string): ProductListItem | null {
  const normalized = normalizeBarcode(barcode);
  if (!normalized) return null;
  return products.find((product) => normalizeBarcode(product.barcode) === normalized) ?? null;
}

export function describeCheckoutBackendError(error: unknown): string {
  if (error instanceof ApiError) {
    const code = error.code ?? "";
    const checkoutStatus = error.checkoutStatus ?? "";
    if (error.status === 401) return "Session expired or login required.";
    if (error.status === 403) return "Your role cannot perform checkout.";
    if (error.status === 409) {
      if (code.includes("OpenShiftRequired") || checkoutStatus === "OpenShiftRequired") return "Open a shift before checkout.";
      if (code === "Products.DuplicateBarcode") return "Barcode is already assigned to another active product.";
      if (code === "Sales.NegativeStockNotAllowed" || checkoutStatus === "StockChanged") return "Stock changed or is insufficient. Refresh products before retrying.";
      return error.checkoutMessage || error.message || "Checkout conflict. Review shift and stock state.";
    }
    if (error.status === 400) return error.checkoutMessage || error.message || "Invalid cart or payment. Check quantities and cash tender.";
    if (error.status === 404) return "A cart product is missing or inactive. Refresh products before retrying.";
    const codeSuffix = error.code ? ` Backend code: ${error.code}.` : "";
    return `${error.message}.${codeSuffix}`;
  }
  return error instanceof Error ? error.message : "Unexpected checkout error.";
}

function isOpenShift(state: string | undefined): boolean {
  return (state ?? "").localeCompare("Open", undefined, { sensitivity: "accent" }) === 0 || (state ?? "").localeCompare("Opened", undefined, { sensitivity: "accent" }) === 0;
}

function stockState(product: ProductListItem): StockState {
  if (!product.isActive) return { label: "Inactive", tone: "danger", canAdd: false, helper: "Product is inactive." };
  if (!product.trackStock) return { label: "Untracked", tone: "info", canAdd: true, helper: "Stock is not tracked for this item." };
  if (product.stockOnHand <= 0) return { label: "Out of stock", tone: "danger", canAdd: false, helper: "Tracked item has no available stock." };
  if (product.stockOnHand <= product.reorderLevel) return { label: "Low stock", tone: "warning", canAdd: true, helper: `${formatQuantity(product.stockOnHand)} left; reorder at ${formatQuantity(product.reorderLevel)}.` };
  return { label: "In stock", tone: "success", canAdd: true, helper: `${formatQuantity(product.stockOnHand)} available.` };
}

function maxQuantity(product: ProductListItem): number | null { return product.trackStock ? Math.max(0, Math.floor(product.stockOnHand)) : null; }
function printTone(status: string | null | undefined): "success" | "warning" | "danger" | "info" { return status === "Succeeded" ? "success" : status === "Failed" ? "danger" : status === "Disabled/Unavailable" ? "warning" : "info"; }
function barcodeTone(message: string | null): "success" | "warning" | "info" { return message?.includes("added to cart") || message?.includes("created") ? "success" : message ? "warning" : "info"; }

function Dialog({ title, children, footer, onClose }: { title: string; children: ReactNode; footer: ReactNode; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4" role="dialog" aria-modal="true">
      <div className="max-h-[90vh] w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-2xl">
        <div className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4">
          <div><h2 className="text-lg font-semibold text-slate-950">{title}</h2><p className="mt-1 text-xs text-slate-500">Esc closes this dialog. Enter confirms only from checkout review.</p></div>
          <Button variant="ghost" size="sm" onClick={onClose}>Close</Button>
        </div>
        <div className="max-h-[62vh] overflow-y-auto px-5 py-4">{children}</div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-200 bg-slate-50 px-5 py-4">{footer}</div>
      </div>
    </div>
  );
}

export function CheckoutPage() {
  const { notify } = useNotifications();
  const [search, setSearch] = useState("");
  const [productPage, setProductPage] = useState(1);
  const [cartLines, setCartLines] = useState<CartLine[]>([]);
  const [cashAmount, setCashAmount] = useState("");
  const [paymentMode, setPaymentMode] = useState<"Cash" | "Utang" | "CashUtang">("Cash");
  const [customerId, setCustomerId] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [utangNote, setUtangNote] = useState("");
  const [showAddCustomer, setShowAddCustomer] = useState(false);
  const [newCustomerName, setNewCustomerName] = useState("");
  const [newCustomerPhone, setNewCustomerPhone] = useState("");
  const [openingCash, setOpeningCash] = useState("0.00");
  const [backendMessage, setBackendMessage] = useState<string | null>(null);
  const [checkoutResult, setCheckoutResult] = useState<CheckoutEnvelope | null>(null);
  const [completedSale, setCompletedSale] = useState<CompletedSaleState | null>(null);
  const [showConfirmCheckout, setShowConfirmCheckout] = useState(false);
  const [showFastAdd, setShowFastAdd] = useState(false);
  const [fastAddBarcode, setFastAddBarcode] = useState("");
  const [fastAddProductName, setFastAddProductName] = useState("");
  const [fastAddUnitId, setFastAddUnitId] = useState("");
  const [fastAddSellingPrice, setFastAddSellingPrice] = useState("");
  const [fastAddInitialStock, setFastAddInitialStock] = useState("1");
  const [fastAddError, setFastAddError] = useState<string | null>(null);
  const [attemptState, setAttemptState] = useState<CheckoutAttemptState>(initialAttemptState);
  const [lastRefreshAt, setLastRefreshAt] = useState<string | null>(null);
  const [isOpeningShift, setIsOpeningShift] = useState(false);
  const [isClosingShift, setIsClosingShift] = useState(false);
  const [showCloseShift,setShowCloseShift]=useState(false);
  const [countedCash,setCountedCash]=useState("");
  const [countConfirmed,setCountConfirmed]=useState(false);
  const [isSubmittingCheckout, setIsSubmittingCheckout] = useState(false);
  const [isPrintingReceipt, setIsPrintingReceipt] = useState(false);
  const [isBarcodeAdding, setIsBarcodeAdding] = useState(false);
  const [isFastAdding, setIsFastAdding] = useState(false);
  const [barcodeStatus, setBarcodeStatus] = useState<string | null>(null);
  const [printResult, setPrintResult] = useState<ReceiptPrintResult | null>(null);
  const scanInputRef = useRef<HTMLInputElement | null>(null);
  const checkoutRequestIdRef = useRef<string | null>(null);

  const sessionQuery = useQuery({ queryKey: ["host-checkout-session"], queryFn: fetchSession, retry: false });
  const licenseQuery = useQuery({ queryKey: ["license-status"], queryFn: fetchLicenseStatus, retry: false });
  const shiftQuery = useQuery({ queryKey: ["host-checkout-shift"], queryFn: fetchHostShift, retry: false });
  const unitsQuery = useQuery({ queryKey: ["host-checkout-product-units"], queryFn: fetchProductUnits, retry: false });
  const productsQuery = useQuery({ queryKey: ["host-checkout-products", search, productPage], queryFn: () => fetchProducts({ search: search.trim() || undefined, isActive: true, page: productPage, pageSize: checkoutPageSize }), retry: false });
  const canCreateReceivable =
    (sessionQuery.data?.user?.permissions.includes("Receivables.CreateCreditSale") ?? false) &&
    (licenseQuery.data?.enabledFeatures.includes("ReceivablesUtangLedger") ?? false);
  const customersQuery = useQuery({
    queryKey: ["receivable-customers-checkout"],
    queryFn: () => fetchReceivableCustomers(),
    retry: false,
    enabled: canCreateReceivable,
  });

  const subtotal = useMemo(() => cartLines.reduce((sum, line) => sum + line.product.sellingPrice * line.quantity, 0), [cartLines]);
  const itemQuantityTotal = useMemo(() => cartLines.reduce((sum, line) => sum + line.quantity, 0), [cartLines]);
  const cashCents = toCents(cashAmount);
  const subtotalCents = Math.round(subtotal * 100);
  const balanceDueCents = paymentMode === "Cash" ? 0 : Math.max(0, subtotalCents - cashCents);
  const estimatedChangeCents = cashCents >= subtotalCents ? cashCents - subtotalCents : 0;
  const shift = shiftQuery.data?.shift;
  const shiftOpen = isOpenShift(shift?.shiftState);
  const operatorLabel = sessionQuery.data?.user?.displayName ?? sessionQuery.data?.user?.username ?? "Not signed in";
  const roleLabel = sessionQuery.data?.user?.roleName ?? "—";
  const productResponse = productsQuery.data;
  const canGoPrevious = (productResponse?.pageNumber ?? productPage) > 1;
  const canGoNext = productResponse ? productResponse.pageNumber < productResponse.totalPages : false;

  const cartValidationMessage = useMemo(() => {
    if (completedSale) return "Start a new sale before submitting again.";
    if (!shiftOpen) return "No open shift.";
    if (cartLines.length === 0) return "No cart lines yet.";
    if (paymentMode === "Cash" && cashCents <= 0) return "Cash tender is missing.";
    if (paymentMode === "Cash" && cashCents < subtotalCents) return "Cash tender is below subtotal.";
    if (paymentMode !== "Cash" && !canCreateReceivable) return "Your role cannot create an Utang sale.";
    if (paymentMode === "Utang" && cashCents !== 0) return "Full Utang requires cash received to be zero.";
    if (paymentMode === "CashUtang" && (cashCents <= 0 || cashCents >= subtotalCents)) return "Cash + Utang requires a partial cash amount.";
    if (balanceDueCents > 0 && !customerId) return "Select a customer for the Utang balance.";
    if (isSubmittingCheckout) return "Checkout is submitting.";
    return null;
  }, [balanceDueCents, canCreateReceivable, cartLines.length, cashCents, completedSale, customerId, isSubmittingCheckout, paymentMode, shiftOpen, subtotalCents]);
  const canReviewCheckout = cartValidationMessage === null && cartLines.length > 0;

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "F9") { event.preventDefault(); if (canReviewCheckout && !showConfirmCheckout && !completedSale) setShowConfirmCheckout(true); return; }
      if (event.key === "Enter" && showConfirmCheckout) { event.preventDefault(); void handleSubmitCheckout(); return; }
      if (event.key === "F5" && completedSale) { event.preventDefault(); void startNewSale(); return; }
      if (event.key === "Escape") { if (showConfirmCheckout) setShowConfirmCheckout(false); if (showFastAdd) setShowFastAdd(false); if (completedSale) setCompletedSale(null); }
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [canReviewCheckout, completedSale, showConfirmCheckout, showFastAdd]);

  useEffect(() => { if (!fastAddUnitId && unitsQuery.data && unitsQuery.data.length > 0) setFastAddUnitId(unitsQuery.data[0].id); }, [fastAddUnitId, unitsQuery.data]);
  useEffect(() => {
    if (!canCreateReceivable && paymentMode !== "Cash") {
      setPaymentMode("Cash");
      setCustomerId("");
      setDueDate("");
      setUtangNote("");
    }
  }, [canCreateReceivable, paymentMode]);

  function handleSearchChange(value: string) { setSearch(value); setProductPage(1); setBarcodeStatus(null); }

  function addProduct(product: ProductListItem) {
    if (!shiftOpen) { setBackendMessage("Open a shift before adding products."); notify.warning("Shift required", "Open a shift before adding products."); return; }
    const state = stockState(product);
    if (!state.canAdd) { setBackendMessage(`${product.name} cannot be added: ${state.helper}`); notify.warning("Product was not added", state.helper); return; }
    setCheckoutResult(null); setCompletedSale(null); setPrintResult(null); setBackendMessage(null);
    setCartLines((current) => {
      const existing = current.find((line) => line.product.id === product.id);
      const max = maxQuantity(product);
      if (existing) {
        const nextQuantity = existing.quantity + 1;
        if (max !== null && nextQuantity > max) { setBackendMessage(`${product.name} has only ${formatQuantity(max)} available.`); return current; }
        return current.map((line) => (line.product.id === product.id ? { ...line, quantity: nextQuantity } : line));
      }
      if (max !== null && max < 1) { setBackendMessage(`${product.name} is out of stock.`); return current; }
      return [...current, { product, quantity: 1 }];
    });
  }

  function focusScanField() {
    window.setTimeout(() => scanInputRef.current?.focus(), 0);
  }

  async function handleBarcodeSubmit() {
    const barcode = normalizeBarcode(search);
    if (!barcode || isBarcodeAdding) return;
    if (!shiftOpen) { setBarcodeStatus("Open a shift before adding barcode products."); focusScanField(); return; }
    setIsBarcodeAdding(true); setBarcodeStatus(`Looking up barcode ${barcode}...`);
    try {
      const lookup = await fetchProducts({ search: barcode, isActive: true, page: 1, pageSize: 50 });
      const match = exactBarcodeMatch(lookup.items, barcode);
      if (!match) { openFastAddForBarcode(barcode); return; }
      const state = stockState(match);
      if (!state.canAdd) { setBarcodeStatus(`${match.name} cannot be added: ${state.helper}`); return; }
      addProduct(match); setBarcodeStatus(`${match.name} added to cart from exact barcode match.`); setSearch(""); setProductPage(1); await productsQuery.refetch();
    } catch (error) { setBarcodeStatus(error instanceof Error ? error.message : "Barcode lookup failed. No product was added."); }
    finally { setIsBarcodeAdding(false); focusScanField(); }
  }

  function openFastAddForBarcode(barcode: string) { const preserved = normalizeBarcode(barcode); setFastAddBarcode(preserved); setFastAddProductName(""); setFastAddSellingPrice(""); setFastAddInitialStock("1"); setFastAddError(null); setShowFastAdd(true); setBarcodeStatus("Product not found. Complete Fast Add to create this product explicitly."); }
  function validateFastAdd(): string | null { if (!fastAddProductName.trim()) return "Product name is required."; if (!normalizeBarcode(fastAddBarcode)) return "Barcode is required."; if (!fastAddUnitId) return "Unit is required."; if (toNumber(fastAddSellingPrice) <= 0) return "Selling price must be greater than zero."; if (toNumber(fastAddInitialStock) < 0) return "Initial stock quantity cannot be negative."; return null; }

  async function handleSubmitFastAdd() {
    if (isFastAdding) return;
    const validation = validateFastAdd(); if (validation) { setFastAddError(validation); notify.warning("Product was not created", validation); return; }
    setIsFastAdding(true); setFastAddError(null);
    try {
      const result = await fastAddUnknownBarcode({ productName: fastAddProductName.trim(), barcode: normalizeBarcode(fastAddBarcode), unitId: fastAddUnitId, sellingPrice: toNumber(fastAddSellingPrice), initialStockQuantity: toNumber(fastAddInitialStock) });
      addProduct(result.product); setBarcodeStatus(`${result.product.name} created by Fast Add and added to cart.`); setShowFastAdd(false); setSearch(""); setProductPage(1); notify.success("Product created", "The product was added to the active cart."); await productsQuery.refetch();
    } catch (error) { const description = describeCheckoutBackendError(error); setFastAddError(description); notify.error("Product was not created", safeNotificationDescription(new Error(description), "Check the required fields and try again.")); }
    finally { setIsFastAdding(false); focusScanField(); }
  }

  function updateQuantity(productId: string, quantity: number) {
    setCheckoutResult(null); setCompletedSale(null); setPrintResult(null);
    setCartLines((current) => current.map((line) => {
      if (line.product.id !== productId) return line;
      const nextQuantity = Math.max(0, Number.isFinite(quantity) ? Math.floor(quantity) : 0);
      const max = maxQuantity(line.product);
      if (max !== null && nextQuantity > max) { setBackendMessage(`${line.product.name} has only ${formatQuantity(max)} available.`); return { ...line, quantity: max }; }
      return { ...line, quantity: nextQuantity };
    }).filter((line) => line.quantity > 0));
  }

  function clearCart() { checkoutRequestIdRef.current = null; setCartLines([]); setCashAmount(""); setCheckoutResult(null); setCompletedSale(null); setPrintResult(null); setBarcodeStatus(null); setShowConfirmCheckout(false); setAttemptState(initialAttemptState); setBackendMessage(null); }
  async function startNewSale() { clearCart(); await refreshShiftAndProducts(); }
  async function refreshShiftAndProducts() { setBackendMessage(null); await Promise.all([sessionQuery.refetch(), shiftQuery.refetch(), productsQuery.refetch(), unitsQuery.refetch()]); setLastRefreshAt(new Date().toISOString()); }
  async function handleOpenShift() { setBackendMessage(null); setIsOpeningShift(true); try { await openHostShift({ openingCashCents: toCents(openingCash) }); setBackendMessage(null); notify.success("Shift opened", "Checkout is ready for sales."); await Promise.all([shiftQuery.refetch(), productsQuery.refetch(), unitsQuery.refetch()]); setLastRefreshAt(new Date().toISOString()); } catch (error) { const description = describeCheckoutBackendError(error); setBackendMessage(description); notify.error("Shift was not opened", safeNotificationDescription(new Error(description), "Review the opening cash and try again.")); } finally { setIsOpeningShift(false); } }
  async function handleAddCustomer() { if (!newCustomerName.trim()) return; try { const customer = await createCustomer({ fullName: newCustomerName.trim(), phone: newCustomerPhone.trim() || undefined }); await customersQuery.refetch(); setCustomerId(customer.id); setShowAddCustomer(false); setNewCustomerName(""); setNewCustomerPhone(""); notify.success("Customer added", "The customer is selected for this Utang sale."); } catch (error) { notify.error("Customer was not added", safeNotificationDescription(error, "Check the customer details and permissions.")); } }
  async function handleCloseShift() { setBackendMessage(null); setIsClosingShift(true); try { const result = await closeHostShift({countedCashCents:toCents(countedCash),physicalCountConfirmed:countConfirmed,clientRequestId:crypto.randomUUID()}); clearCart(); setBackendMessage(`${result.message} Expected ${fromCents(result.shift.expectedCashCents)}; counted ${fromCents(result.shift.countedCashCents)}; variance ${fromCents(result.shift.varianceCents)}; ${result.shift.reviewStatus}.`); notify.success("Physical count submitted",result.shift.reviewStatus==="PendingReview"?"Discrepancy is pending manager review.":"Drawer reconciled.");setShowCloseShift(false);setCountedCash("");setCountConfirmed(false); await shiftQuery.refetch(); setLastRefreshAt(new Date().toISOString()); } catch (error) { const description = describeCheckoutBackendError(error); setBackendMessage(description); notify.error("Shift was not closed", safeNotificationDescription(new Error(description), "Review the physical count and coverage status.")); } finally { setIsClosingShift(false); } }
  function reviewCheckout() { if (!canReviewCheckout) { const description = cartValidationMessage ?? "Checkout is not ready."; setBackendMessage(description); notify.warning("Sale is not ready", description); return; } setBackendMessage(null); setShowConfirmCheckout(true); }

  async function handleSubmitCheckout() {
    if (!canReviewCheckout || isSubmittingCheckout) return;
    setBackendMessage(null); setCheckoutResult(null); setCompletedSale(null); setPrintResult(null); setAttemptState(initialAttemptState); setIsSubmittingCheckout(true);
    try {
      const clientRequestId = checkoutRequestIdRef.current ?? crypto.randomUUID();
      checkoutRequestIdRef.current = clientRequestId;
      const customer = customersQuery.data?.find((row) => row.customer.id === customerId)?.customer;
      const payments = paymentMode === "Cash"
        ? [{ method: "Cash" as const, amountCents: cashCents }]
        : [
            ...(cashCents > 0 ? [{ method: "Cash" as const, amountCents: cashCents }] : []),
            { method: "AccountsReceivable" as const, amountCents: balanceDueCents },
          ];
      const result = await submitCheckout({
        clientRequestId,
        lines: cartLines.map((line) => ({ productId: line.product.id, quantityX1000: Math.round(line.quantity * 1000) })),
        payments,
        customerId: balanceDueCents > 0 ? customerId : undefined,
        customerFullName: customer?.fullName,
        customerPhone: customer?.phone ?? undefined,
        dueDate: dueDate || undefined,
        note: utangNote.trim() || undefined,
      });
      const receiptMetadata = result.checkout as CheckoutReceiptMetadata;
      setCheckoutResult(result);
      checkoutRequestIdRef.current = null;
      setCompletedSale({ receiptNumber: result.checkout.receiptNumber, saleId: result.checkout.saleId, totalCents: result.checkout.totalCents, amountPaidCents: cashCents, changeCents: result.checkout.changeCents, fileReceiptStatus: receiptMetadata.fileReceiptStatus ?? null, fileReceiptPath: receiptMetadata.fileReceiptPath ?? null, fileReceiptError: receiptMetadata.fileReceiptError ?? null });
      setShowConfirmCheckout(false); setBackendMessage(result.message);
      setAttemptState({ httpStatus: 200, backendCode: result.code, checkoutStatus: result.checkout.status, receiptNumber: result.checkout.receiptNumber, saleId: result.checkout.saleId });
      notify.success("Sale completed", "Receipt was saved.");
      await productsQuery.refetch();
    } catch (error) {
      if (error instanceof ApiError) {
        checkoutRequestIdRef.current = null;
        setAttemptState({ httpStatus: error.status, backendCode: error.code ?? null, checkoutStatus: error.checkoutStatus ?? null, receiptNumber: null, saleId: null });
      }
      const description = describeCheckoutBackendError(error);
      setBackendMessage(description);
      notify.error("Sale failed", safeNotificationDescription(new Error(description), "No stock or payment record was changed."));
    } finally { setIsSubmittingCheckout(false); }
  }

  async function handlePrintCompletedReceipt() {
    if (!completedSale?.saleId || isPrintingReceipt) return;
    setIsPrintingReceipt(true); setPrintResult(null);
    try {
      const result = await printReceiptForSale(completedSale.saleId);
      setPrintResult(result.print);
      if (result.print.succeeded || result.print.status === "Succeeded") {
        notify.success("Receipt printed", "Sent to the selected Windows printer.");
      } else if (result.print.status === "Disabled/Unavailable") {
        notify.warning("Receipt was not printed", "Printing is disabled or the selected printer is unavailable.");
      } else {
        notify.error("Print failed", safeNotificationDescription(new Error(result.print.errorMessage || result.print.message), "Check printer settings or use reprint later."));
      }
    }
    catch (error) {
      setPrintResult({ succeeded: false, status: "Failed", saleId: completedSale.saleId, receiptNumber: completedSale.receiptNumber, source: "PersistedReceiptSnapshot", adapterName: "Unknown", message: "Receipt print request failed. Sale remains committed.", errorMessage: error instanceof Error ? error.message : "Unknown print error.", printedAtUtc: null });
      notify.error("Print failed", safeNotificationDescription(error, "Check printer settings or use reprint later."));
    }
    finally { setIsPrintingReceipt(false); }
  }

  function renderProductCard(product: ProductListItem) {
    const state = stockState(product); const alreadyInCart = cartLines.find((line) => line.product.id === product.id)?.quantity ?? 0;
    return <div key={product.id} className="rounded-lg border border-slate-200 bg-white p-3 shadow-sm"><div className="flex items-start justify-between gap-3"><div className="min-w-0 space-y-2"><div><p className="truncate text-sm font-semibold text-slate-950">{product.name}</p><p className="text-xs text-slate-500">Barcode {product.barcode ?? "Not assigned"}</p></div><div className="flex flex-wrap gap-1.5"><StatusPill tone="neutral">{product.categoryName ?? "No category"}</StatusPill><StatusPill tone="neutral">{product.brandName ?? "No brand"}</StatusPill><StatusPill tone={state.tone}>{state.label}</StatusPill></div><p className="text-xs text-slate-500">{state.helper}</p></div><div className="shrink-0 text-right"><p className="text-base font-bold text-slate-950">{formatMoney(product.sellingPrice)}</p><p className="text-xs text-slate-500">per {product.unitName}</p>{alreadyInCart > 0 ? <p className="mt-1 text-xs font-semibold text-blue-700">In cart: {formatQuantity(alreadyInCart)}</p> : null}<Button size="sm" className="mt-2" variant={state.canAdd ? "primary" : "secondary"} onClick={() => addProduct(product)} disabled={!state.canAdd || !shiftOpen}>Add</Button></div></div></div>;
  }

  return (
    <div className="min-w-0 space-y-3 overflow-hidden">
      <CommandBar compact title="POS Checkout" subtitle="Scan, sell, and take payment." status={<div className="flex flex-wrap items-center gap-2"><StatusPill tone={sessionQuery.data?.isAuthenticated ? "success" : "warning"}>{operatorLabel}</StatusPill><StatusPill tone={shiftOpen ? "success" : "warning"}>Shift {shift?.shiftState ?? "Unknown"}</StatusPill></div>} actions={<Button size="sm" variant="secondary" onClick={() => void refreshShiftAndProducts()} disabled={shiftQuery.isFetching || productsQuery.isFetching || sessionQuery.isFetching}>{shiftQuery.isFetching || productsQuery.isFetching || sessionQuery.isFetching ? "Refreshing..." : "Refresh"}</Button>} />
      <Card className="border-slate-300"><CardBody className="flex min-w-0 flex-wrap items-center gap-x-5 gap-y-2 py-2 text-xs"><span className="min-w-0"><span className="font-semibold uppercase tracking-wide text-slate-500">Operator:</span> <span className="font-semibold text-slate-950">{operatorLabel}</span> <span className="text-slate-500">({roleLabel})</span></span><span className="flex items-center gap-2"><span className="font-semibold uppercase tracking-wide text-slate-500">Shift:</span><StatusPill tone={shiftOpen ? "success" : "warning"}>{shift?.shiftState ?? "Unknown"}</StatusPill><span className="font-mono text-slate-500">{shortId(shift?.shiftId)}</span></span><span className="min-w-0 text-slate-600"><span className="font-semibold uppercase tracking-wide text-slate-500">Opened:</span> {shift?.openedAtUtc ? formatDateTime(shift.openedAtUtc) : "No open shift"} · Cash {fromCents(shift?.openingCashCents)}</span><span className="ml-auto">{shiftOpen ? <Button size="sm" variant="danger" onClick={() => setShowCloseShift(true)} disabled={isClosingShift}>Count & Close Shift</Button> : null}</span></CardBody></Card>
      {showCloseShift ? <Card className="border-amber-300 bg-amber-50"><CardHeader><CardTitle>Physical drawer count — policy pending approval</CardTitle></CardHeader><CardBody className="space-y-3"><p className="text-sm">Count the cash physically present. The value is never prefilled from expected cash and becomes immutable after submission.</p><TextInput value={countedCash} onChange={e=>{setCountedCash(e.target.value);setCountConfirmed(false);}} inputMode="decimal" placeholder="Actual counted cash" /><label className="flex gap-2 text-sm"><input type="checkbox" checked={countConfirmed} onChange={e=>setCountConfirmed(e.target.checked)} />I physically counted this drawer and confirm the amount.</label><div className="flex gap-2"><Button variant="danger" disabled={!countConfirmed||countedCash.trim()===""||!Number.isFinite(Number(countedCash))||toCents(countedCash)<0||isClosingShift} onClick={()=>void handleCloseShift()}>{isClosingShift?"Submitting…":"Submit immutable count & close"}</Button><Button variant="secondary" onClick={()=>setShowCloseShift(false)}>Cancel</Button></div><p className="text-xs">Any nonzero variance is closed as PendingReview and requires manager/owner acknowledgment with a reason.</p></CardBody></Card>:null}
      {backendMessage ? <Alert tone={completedSale ? "success" : "warning"}>{backendMessage}</Alert> : null}
      {shiftQuery.isLoading ? <LoadingState label="Reading host shift..." /> : null}
      {shiftQuery.isError ? <ErrorState message={describeCheckoutBackendError(shiftQuery.error)} /> : null}
      {shiftOpen ? <Card className="border-blue-200 bg-blue-50/40"><CardHeader><CardTitle>Payment mode</CardTitle></CardHeader><CardBody className="grid gap-3 md:grid-cols-2 xl:grid-cols-5"><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">Sale payment</label><select className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm" value={paymentMode} onChange={(event) => { const mode = event.target.value as "Cash" | "Utang" | "CashUtang"; setPaymentMode(mode); if (mode === "Utang") setCashAmount("0"); }}><option value="Cash">Cash</option>{canCreateReceivable ? <><option value="Utang">Utang</option><option value="CashUtang">Cash + Utang</option></> : null}</select></div>{paymentMode !== "Cash" ? <><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">Customer</label><div className="flex gap-2"><select className="h-10 min-w-0 flex-1 rounded-md border border-slate-300 bg-white px-3 text-sm" value={customerId} onChange={(event) => setCustomerId(event.target.value)}><option value="">Select customer</option>{(customersQuery.data ?? []).filter((row) => row.customer.isActive).map((row) => <option key={row.customer.id} value={row.customer.id}>{row.customer.fullName}{row.customer.phone ? ` · ${row.customer.phone}` : ""}</option>)}</select><Button size="sm" variant="secondary" onClick={() => setShowAddCustomer(true)}>Add</Button></div></div><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">Due date</label><TextInput type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} /></div><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">Utang note</label><TextInput value={utangNote} onChange={(event) => setUtangNote(event.target.value)} placeholder="Optional note" /></div><div className="rounded-md border border-blue-200 bg-white p-3"><p className="text-xs uppercase text-slate-500">Balance due</p><p className="text-lg font-bold text-blue-800">{fromCents(balanceDueCents)}</p></div></> : null}</CardBody></Card> : null}

      {!shiftOpen ? (
        <Card className="border-amber-300 bg-amber-50/70"><CardHeader><CardTitle>Cashier workstation locked</CardTitle></CardHeader><CardBody className="grid gap-4 text-sm text-amber-950 lg:grid-cols-[1fr_auto] lg:items-end"><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-amber-900">Opening cash</label><TextInput value={openingCash} onChange={(event) => setOpeningCash(event.target.value)} /><p className="mt-2 text-xs text-amber-800">Open a cashier shift before product search, cart, payment, and checkout controls become available.</p></div><Button onClick={() => void handleOpenShift()} disabled={isOpeningShift || shiftQuery.isLoading}>{isOpeningShift ? "Opening..." : "Open Shift"}</Button></CardBody></Card>
      ) : (
        <div className="grid min-h-0 min-w-0 gap-3 xl:h-[calc(100vh-12rem)] xl:min-h-[36rem] xl:grid-cols-[minmax(0,0.85fr)_minmax(0,0.95fr)_minmax(21rem,1.2fr)]">
          <Card className="flex min-h-[28rem] min-w-0 flex-col xl:min-h-0">
            <CardHeader className="shrink-0"><div className="flex items-center justify-between gap-3"><CardTitle>Scan / search</CardTitle>{productResponse ? <span className="text-xs font-semibold text-slate-500">{productResponse.totalCount} result{productResponse.totalCount === 1 ? "" : "s"}</span> : null}</div></CardHeader>
            <CardBody className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
              <TextInput className="min-h-12 shrink-0 text-base" ref={scanInputRef} placeholder="Scan barcode or search product name..." value={search} onChange={(event) => handleSearchChange(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === "Tab") { event.preventDefault(); event.stopPropagation(); void handleBarcodeSubmit(); } }} autoComplete="off" autoFocus inputMode="search" />
              <details className="shrink-0 rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
                <summary className="cursor-pointer font-semibold text-slate-800">Barcode and Fast Add help</summary>
                <p className="mt-2 leading-relaxed">Barcode identifies the sellable item or package. Checkout supports keyboard-wedge barcode input. Enter performs an exact barcode add first. An unknown barcode opens Fast Add for confirmation. Product-name search remains available as you type. Inventory scans do not silently create products.</p>
              </details>
              {barcodeStatus ? <Alert tone={barcodeTone(barcodeStatus)} title="Barcode lookup">{barcodeStatus}</Alert> : null}
              {productsQuery.isLoading ? <LoadingState label="Loading products..." /> : null}
              {productsQuery.isError ? <ErrorState message={describeCheckoutBackendError(productsQuery.error)} /> : null}
              {productResponse ? <><div className="min-h-0 flex-1 space-y-2 overflow-y-auto overscroll-contain pr-1">{productResponse.items.length > 0 ? productResponse.items.map(renderProductCard) : <div className="rounded-lg border border-dashed border-slate-300 p-6 text-sm text-slate-500">No active products found. Try a barcode or product name.</div>}</div><div className="flex shrink-0 items-center justify-between border-t border-slate-100 pt-3 text-xs text-slate-500"><span>Page {productResponse.pageNumber} of {Math.max(1, productResponse.totalPages)}</span><div className="flex gap-2"><Button size="sm" variant="secondary" onClick={() => setProductPage((page) => Math.max(1, page - 1))} disabled={!canGoPrevious || productsQuery.isFetching}>Prev</Button><Button size="sm" variant="secondary" onClick={() => setProductPage((page) => page + 1)} disabled={!canGoNext || productsQuery.isFetching}>Next</Button></div></div></> : null}
            </CardBody>
          </Card>
          <Card className="flex min-h-[28rem] min-w-0 flex-col xl:min-h-0"><CardHeader className="shrink-0"><div className="flex items-center justify-between gap-3"><div><CardTitle>Active cart</CardTitle><p className="mt-1 text-xs text-slate-500">{cartLines.length} line{cartLines.length === 1 ? "" : "s"} · {formatQuantity(itemQuantityTotal)} item{itemQuantityTotal === 1 ? "" : "s"}</p></div><Button variant="secondary" size="sm" onClick={clearCart} disabled={cartLines.length === 0}>Clear</Button></div></CardHeader><CardBody className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1">{cartLines.length === 0 ? <div className="rounded-lg border border-dashed border-slate-300 p-8 text-center text-sm text-slate-500">Scan barcode or search a product, then press Add. Stock changes only when checkout completes.</div> : <div className="space-y-3">{cartLines.map((line) => { const max = maxQuantity(line.product); const stock = stockState(line.product); return <div key={line.product.id} className="rounded-lg border border-slate-200 bg-white p-3 shadow-sm"><div className="flex items-start justify-between gap-3"><div className="min-w-0"><p className="truncate text-sm font-semibold text-slate-950">{line.product.name}</p><p className="text-xs text-slate-500">Barcode {line.product.barcode ?? "Not assigned"} · {line.product.unitName} · {formatMoney(line.product.sellingPrice)} each</p><div className="mt-2 flex flex-wrap gap-1.5"><StatusPill tone={stock.tone}>{stock.label}</StatusPill>{line.product.categoryName ? <StatusPill tone="neutral">{line.product.categoryName}</StatusPill> : null}</div>{max !== null && line.quantity >= max ? <p className="mt-2 text-xs font-semibold text-amber-700">Reached known stock limit.</p> : null}</div><Button size="sm" variant="ghost" onClick={() => updateQuantity(line.product.id, 0)}>Remove</Button></div><div className="mt-3 grid gap-3 border-t border-slate-100 pt-3 sm:grid-cols-[auto_1fr_auto] sm:items-center"><div className="inline-flex items-center rounded-md border border-slate-300 bg-slate-50"><Button size="sm" variant="ghost" onClick={() => updateQuantity(line.product.id, Math.max(0, line.quantity - 1))}>−</Button><input className="h-8 w-14 border-x border-slate-200 bg-white text-center text-sm font-semibold text-slate-900 focus:outline-none" type="number" min={0} max={max ?? undefined} value={line.quantity} onChange={(event) => updateQuantity(line.product.id, Number(event.target.value))} /><Button size="sm" variant="ghost" onClick={() => updateQuantity(line.product.id, line.quantity + 1)} disabled={max !== null && line.quantity >= max}>+</Button></div><p className="text-xs text-slate-500">{max === null ? "Untracked item; backend still validates sale." : `${formatQuantity(max)} known stock.`}</p><div className="text-right"><p className="text-xs text-slate-500">Line total</p><p className="text-base font-bold text-slate-950">{formatMoney(line.product.sellingPrice * line.quantity)}</p></div></div></div>; })}</div>}</CardBody></Card>
          <Card className="flex min-h-[28rem] min-w-0 flex-col border-slate-300 xl:min-h-0"><CardHeader className="shrink-0"><CardTitle>Payment / checkout</CardTitle></CardHeader><CardBody className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto overscroll-contain"><div className="rounded-lg bg-slate-950 p-4 text-white"><div className="flex items-center justify-between text-sm text-slate-300"><span>Subtotal</span><span>{cartLines.length} line(s)</span></div><p className="mt-2 text-3xl font-bold tracking-tight">{formatMoney(subtotal)}</p></div><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">Cash tendered</label><TextInput className="min-h-12 text-lg font-semibold" value={cashAmount} onChange={(event) => setCashAmount(event.target.value)} placeholder="0.00" inputMode="decimal" /><div className="mt-2 grid grid-cols-3 gap-2"><Button size="sm" variant="secondary" onClick={() => setCashAmount(subtotal.toFixed(2))} disabled={subtotal <= 0}>Exact</Button><Button size="sm" variant="secondary" onClick={() => setCashAmount("500.00")}>500</Button><Button size="sm" variant="secondary" onClick={() => setCashAmount("1000.00")}>1000</Button></div></div><div className="rounded-lg border border-slate-200 bg-slate-50 p-4"><div className="flex items-center justify-between text-sm text-slate-500"><span>Change due</span><span>{cashCents < subtotalCents ? "Insufficient cash" : "Ready"}</span></div><p className={`mt-1 text-2xl font-bold ${cashCents < subtotalCents ? "text-red-700" : "text-emerald-700"}`}>{fromCents(estimatedChangeCents)}</p></div>{cartValidationMessage ? <Alert tone={completedSale ? "success" : "warning"} title="Checkout readiness">{cartValidationMessage}</Alert> : <Alert tone="success" title="Ready">Press Review Checkout or F9 to verify totals before committing the sale.</Alert>}<div className="mt-auto space-y-3 border-t border-slate-100 pt-3"><Button className="min-h-14 w-full text-base" variant="success" onClick={reviewCheckout} disabled={!canReviewCheckout}>{isSubmittingCheckout ? "Completing..." : "Review Checkout"}</Button><div className="rounded-lg bg-slate-100 p-3 text-xs text-slate-600"><p className="font-semibold text-slate-800">Keyboard hints</p><p>Enter = exact barcode add or Fast Add from scan/search input · F9 = Review Checkout · Enter = Confirm when review is open · F5 = New Sale after completion · Esc = Close/cancel dialog</p><p className="mt-1">Fast Add currently starts from Checkout when an unknown barcode is scanned or entered. Product barcode fields accept manual barcode entry. Barcode-driven receiving from Stock-In or stock adjustment is not active unless implemented later.</p></div></div></CardBody></Card>
        </div>
      )}

      <details className="rounded-lg border border-slate-200 bg-white p-4 text-sm text-slate-600 shadow-card"><summary className="cursor-pointer text-sm font-semibold text-slate-900">Checkout diagnostics</summary><div className="mt-4 grid gap-2 md:grid-cols-2 xl:grid-cols-4"><p>Session: {sessionQuery.isLoading ? "Loading..." : sessionQuery.data?.isAuthenticated ? `Authenticated as ${operatorLabel}` : "Not authenticated"}</p><p>Shift status: {shift?.shiftState ?? "Unknown"}</p><p>Permission required: Sales.Create</p><p>Product source: local product catalog</p><p>Cart line count: {cartLines.length}</p><p>Cart item quantity: {formatQuantity(itemQuantityTotal)}</p><p>Cart total: {formatMoney(subtotal)}</p><p>Cash tendered: {fromCents(cashCents)}</p><p>Local validation: {cartValidationMessage ?? "Ready for review"}</p><p>Submit lock: {isSubmittingCheckout ? "Locked" : "Unlocked"}</p><p>Barcode status: {barcodeStatus ?? "NotAttempted"}</p><p>Fast Add: {showFastAdd ? "Open" : "Closed"}</p><p>Print status: {printResult?.status ?? "NotAttempted"}</p><p>Last checkout HTTP status: {attemptState.httpStatus ?? "—"}</p><p>Last backend code: {attemptState.backendCode ?? "—"}</p><p>Last checkout status: {attemptState.checkoutStatus ?? "—"}</p><p>Last receipt number: {attemptState.receiptNumber ?? "—"}</p><p>Last sale ID: {attemptState.saleId ?? "—"}</p><p>Last refresh: {lastRefreshAt ? formatDateTime(lastRefreshAt) : "—"}</p></div></details>

      {showConfirmCheckout ? <Dialog title="Review Checkout" onClose={() => setShowConfirmCheckout(false)} footer={<><Button variant="secondary" onClick={() => setShowConfirmCheckout(false)} disabled={isSubmittingCheckout}>Cancel</Button><Button variant="success" onClick={() => void handleSubmitCheckout()} disabled={isSubmittingCheckout || !canReviewCheckout}>{isSubmittingCheckout ? "Committing..." : "Confirm Sale"}</Button></>}><div className="space-y-4 text-sm text-slate-700"><Alert tone="warning" title="Final review before sale commit">Sale commits only after Confirm Sale. Printing is attempted only after a committed sale and only when Print Receipt is clicked.</Alert><div className="grid gap-3 sm:grid-cols-2"><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Cart</p><p className="text-lg font-semibold text-slate-950">{cartLines.length} line(s) · {formatQuantity(itemQuantityTotal)} item(s)</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Total</p><p className="text-lg font-semibold text-slate-950">{formatMoney(subtotal)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Cash tendered</p><p className="text-lg font-semibold text-slate-950">{fromCents(cashCents)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Change</p><p className="text-lg font-semibold text-emerald-700">{fromCents(estimatedChangeCents)}</p></div></div><div className="max-h-56 overflow-y-auto rounded-lg border border-slate-200">{cartLines.map((line) => <div key={line.product.id} className="flex justify-between gap-3 border-b border-slate-100 px-3 py-2 last:border-b-0"><div><p className="font-semibold text-slate-900">{line.product.name}</p><p className="text-xs text-slate-500">{formatQuantity(line.quantity)} × {formatMoney(line.product.sellingPrice)}</p></div><p className="font-semibold text-slate-900">{formatMoney(line.quantity * line.product.sellingPrice)}</p></div>)}</div></div></Dialog> : null}
      {showAddCustomer ? <Dialog title="Add Utang customer" onClose={() => setShowAddCustomer(false)} footer={<><Button variant="secondary" onClick={() => setShowAddCustomer(false)}>Cancel</Button><Button onClick={() => void handleAddCustomer()} disabled={!newCustomerName.trim()}>Add and select</Button></>}><div className="grid gap-3"><div><label className="mb-1 block text-xs font-semibold uppercase text-slate-500">Full name</label><TextInput value={newCustomerName} onChange={(event) => setNewCustomerName(event.target.value)} /></div><div><label className="mb-1 block text-xs font-semibold uppercase text-slate-500">Phone</label><TextInput value={newCustomerPhone} onChange={(event) => setNewCustomerPhone(event.target.value)} /></div></div></Dialog> : null}

      {showFastAdd ? <Dialog title="Fast Add Unknown Barcode" onClose={() => setShowFastAdd(false)} footer={<><Button variant="secondary" onClick={() => setShowFastAdd(false)} disabled={isFastAdding}>Cancel</Button><Button variant="success" onClick={() => void handleSubmitFastAdd()} disabled={isFastAdding || unitsQuery.isLoading}>{isFastAdding ? "Creating..." : "Create Product and Add to Cart"}</Button></>}><div className="space-y-4 text-sm text-slate-700"><Alert tone="warning" title="Explicit cashier confirmation required">This creates a minimal active product and optional initial stock entry for the preserved unknown barcode. It does not reduce stock; checkout remains the only sale stock-reducing operation.</Alert>{fastAddError ? <Alert tone="danger" title="Fast Add error">{fastAddError}</Alert> : null}{unitsQuery.isError ? <Alert tone="danger" title="Units unavailable">{describeCheckoutBackendError(unitsQuery.error)}</Alert> : null}<div className="grid gap-3 sm:grid-cols-2"><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">Barcode</label><TextInput value={fastAddBarcode} readOnly aria-readonly="true" /><p className="mt-1 text-xs text-slate-500">Preserved from the unknown scan/search input.</p></div><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">Product name</label><TextInput value={fastAddProductName} onChange={(event) => setFastAddProductName(event.target.value)} placeholder="Product name" /></div><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">Unit</label><select className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900" value={fastAddUnitId} onChange={(event) => setFastAddUnitId(event.target.value)}><option value="">Select unit</option>{(unitsQuery.data ?? []).map((unit) => <option key={unit.id} value={unit.id}>{unit.name}</option>)}</select></div><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">Selling price</label><TextInput value={fastAddSellingPrice} onChange={(event) => setFastAddSellingPrice(event.target.value)} placeholder="0.00" inputMode="decimal" /></div><div><label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">Initial stock quantity</label><TextInput value={fastAddInitialStock} onChange={(event) => setFastAddInitialStock(event.target.value)} placeholder="0" inputMode="decimal" /><p className="mt-1 text-xs text-slate-500">Must be zero or higher. Positive quantity creates an initial stock entry.</p></div></div><p className="rounded-md bg-slate-100 p-3 text-xs text-slate-600">Duplicate barcode is rejected by the local API. Product creation is explicit and uses the local product/inventory write path.</p></div></Dialog> : null}

      {completedSale ? <Dialog title="Sale Completed" onClose={() => setCompletedSale(null)} footer={<><Button className="min-h-12" variant="secondary" onClick={() => void handlePrintCompletedReceipt()} disabled={!completedSale.saleId || isPrintingReceipt}>{isPrintingReceipt ? "Printing..." : "Print Receipt"}</Button><Button className="min-h-12" variant="success" onClick={() => void startNewSale()}>New Sale (F5)</Button></>}><div className="space-y-4 text-sm text-slate-700"><div className="rounded-xl border border-emerald-200 bg-emerald-50 p-5 text-center"><p className="text-xs font-semibold uppercase tracking-wide text-emerald-700">Cash sale committed</p><p className="mt-2 text-2xl font-bold text-emerald-900">{completedSale.receiptNumber ?? "Receipt pending"}</p><p className="mt-1 text-xs text-emerald-800">Keep this receipt number for your store records.</p><details className="mt-3 rounded-md border border-emerald-100 bg-white/70 px-3 py-2 text-left text-xs text-emerald-900"><summary className="cursor-pointer font-medium text-emerald-800">Support details</summary><p className="mt-2">This checkout receipt number is the store reference for this sale (not an official government receipt).</p><p className="mt-1 font-mono">Sale ID: {completedSale.saleId ?? "—"}</p></details></div><div className="grid gap-3 sm:grid-cols-3"><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Total</p><p className="text-lg font-semibold text-slate-950">{fromCents(completedSale.totalCents)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Amount paid</p><p className="text-lg font-semibold text-slate-950">{fromCents(completedSale.amountPaidCents)}</p></div><div className="rounded-lg border border-slate-200 p-3"><p className="text-xs uppercase tracking-wide text-slate-500">Change</p><p className="text-lg font-semibold text-emerald-700">{fromCents(completedSale.changeCents)}</p></div></div><Alert tone={completedSale.fileReceiptStatus === "Written" ? "success" : "warning"} title="Receipt file status">{completedSale.fileReceiptStatus === "Written" ? `Receipt file created${completedSale.fileReceiptPath ? `: ${completedSale.fileReceiptPath}` : "."}` : completedSale.fileReceiptStatus ? `Receipt file status: ${completedSale.fileReceiptStatus}${completedSale.fileReceiptError ? ` — ${completedSale.fileReceiptError}` : ""}` : "Receipt file status was not included in this checkout response."}</Alert><Alert tone="info" title="Saved under Sales & Receipts">The sale is saved under Sales & Receipts for receipt snapshot preview, detail review, and later reprint. Stay on Checkout to print or start a new sale.</Alert>{printResult ? <Alert tone={printTone(printResult.status)} title="Printer adapter status">{printResult.message}{printResult.errorMessage ? ` — ${printResult.errorMessage}` : ""} Source: {printResult.source}. Adapter: {printResult.adapterName}.</Alert> : <Alert tone="info" title="Printer adapter status">Printing is optional. Sale remains committed if printer is disabled, unavailable, or failed.</Alert>}<p className="text-xs text-slate-500">Print Receipt uses the persisted receipt snapshot/file source through the printer adapter. It does not start a new sale. New Sale remains a separate action.</p>{checkoutResult ? <p className="rounded-md bg-slate-100 p-3 text-slate-700">{checkoutResult.checkout.message}</p> : null}</div></Dialog> : null}
    </div>
  );
}
