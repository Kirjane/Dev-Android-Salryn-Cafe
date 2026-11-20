export const UX_B_DATA_SOURCE = "MOCKED_UI" as const;

export type TenderMode = "CASH" | "AR" | "PARTIAL_CASH_AR";
export type ServiceChannel = "DINE_IN" | "TAKEOUT" | "DELIVERY";
export type DiscountKind = "NONE" | "SENIOR_CITIZEN" | "PWD" | "PROMO_10";
export type StationTrustState =
  | "PAIRED_ONLINE"
  | "OFFLINE_LEASE_VALID"
  | "OFFLINE_LEASE_EXPIRED"
  | "REVOKED";

export type KitchenStation = "BARISTA" | "HOT_KITCHEN" | "PASTRY";
export type OrderStatus = "OPEN" | "PREPARING" | "READY" | "SERVED" | "VOIDED";
export type KitchenTicketStatus = "QUEUED" | "IN_PROGRESS" | "READY" | "RECALLED";

export type ModifierOption = {
  id: string;
  label: string;
  priceDeltaCents: number;
};

export type ModifierGroup = {
  id: string;
  name: string;
  required: boolean;
  options: ModifierOption[];
};

export type CafeMenuItem = {
  id: string;
  sku: string;
  name: string;
  category: "Espresso" | "Cold Brew & Tea" | "Pastries" | "All-Day Meals";
  station: KitchenStation;
  basePriceCents: number;
  modifierGroups: ModifierGroup[];
};

export type ArCustomerAccount = {
  id: string;
  name: string;
  creditLimitCents: number;
  currentBalanceCents: number;
};

export type CartLine = {
  lineId: string;
  item: CafeMenuItem;
  quantity: number;
  selectedModifiers: ModifierOption[];
  note?: string;
};

export type ServerQuoteResponse = {
  dataSource: typeof UX_B_DATA_SOURCE;
  subtotalCents: number;
  discountCents: number;
  totalDueCents: number;
};

export type CheckoutIntent = {
  operationId: string;
  channel: ServiceChannel;
  tableOrRef: string;
  discount: DiscountKind;
  tenderMode: TenderMode;
  cashTenderedCents: number;
  arChargeCents: number;
  arCustomerId?: string;
  lines: CartLine[];
};

export type CommittedReceipt = {
  dataSource: typeof UX_B_DATA_SOURCE;
  operationId: string;
  receiptNumber: string;
  kitchenTicketNumber: string;
  channel: ServiceChannel;
  tableOrRef: string;
  tenderMode: TenderMode;
  totalDueCents: number;
  cashTenderedCents: number;
  changeCents: number;
  arChargeCents: number;
  replayedIdempotently: boolean;
  committedAtIso: string;
};

export type OrderRecord = {
  id: string;
  receiptNumber: string;
  channel: ServiceChannel;
  tableOrRef: string;
  summary: string;
  totalDueCents: number;
  tenderMode: TenderMode;
  status: OrderStatus;
  createdAtLabel: string;
};

export type KitchenTicket = {
  id: string;
  ticketNumber: string;
  receiptNumber: string;
  station: KitchenStation;
  channel: ServiceChannel;
  tableOrRef: string;
  items: { name: string; quantity: number; modifiers: string[]; note?: string }[];
  status: KitchenTicketStatus;
  elapsedMinutes: number;
};

export type BlindCloseResult = {
  dataSource: typeof UX_B_DATA_SOURCE;
  operationId: string;
  countedCashCents: number;
  expectedCashCents: number;
  varianceCents: number;
  closedAtIso: string;
};

export type RefundResult = {
  dataSource: typeof UX_B_DATA_SOURCE;
  operationId: string;
  receiptNumber: string;
  refundedCents: number;
  reason: string;
};

export function formatPhp(cents: number): string {
  const sign = cents < 0 ? "-" : "";
  return `${sign}₱${(Math.abs(cents) / 100).toLocaleString("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}
