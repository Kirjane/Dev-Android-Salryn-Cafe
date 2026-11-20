import {
  UX_B_DATA_SOURCE,
  type ArCustomerAccount,
  type BlindCloseResult,
  type CafeMenuItem,
  type CartLine,
  type CheckoutIntent,
  type CommittedReceipt,
  type DiscountKind,
  type KitchenTicket,
  type KitchenTicketStatus,
  type OrderRecord,
  type OrderStatus,
  type RefundResult,
} from "./types";
import { UnknownOutcomeError } from "./useDurableOperation";

export const MOCK_MENU_ITEMS: CafeMenuItem[] = [
  {
    id: "menu-spanish-latte",
    sku: "ESP-01",
    name: "Spanish Latte",
    category: "Espresso",
    station: "BARISTA",
    basePriceCents: 16500,
    modifierGroups: [
      {
        id: "milk",
        name: "Milk Choice",
        required: true,
        options: [
          { id: "milk-full", label: "Full Cream", priceDeltaCents: 0 },
          { id: "milk-oat", label: "Oat Milk", priceDeltaCents: 3500 },
          { id: "milk-soy", label: "Soy Milk", priceDeltaCents: 2500 },
        ],
      },
      {
        id: "shot",
        name: "Espresso Shot",
        required: false,
        options: [
          { id: "shot-std", label: "Standard Double", priceDeltaCents: 0 },
          { id: "shot-extra", label: "Extra Shot", priceDeltaCents: 4000 },
        ],
      },
    ],
  },
  {
    id: "menu-americano",
    sku: "ESP-02",
    name: "Iced Americano",
    category: "Espresso",
    station: "BARISTA",
    basePriceCents: 13000,
    modifierGroups: [
      {
        id: "sweet",
        name: "Sweetness",
        required: false,
        options: [
          { id: "sw-0", label: "No Sugar", priceDeltaCents: 0 },
          { id: "sw-50", label: "50% Syrup", priceDeltaCents: 1000 },
        ],
      },
    ],
  },
  {
    id: "menu-cold-brew",
    sku: "CBT-01",
    name: "House Cold Brew 500mL",
    category: "Cold Brew & Tea",
    station: "BARISTA",
    basePriceCents: 22000,
    modifierGroups: [],
  },
  {
    id: "menu-ensaymada",
    sku: "PAS-01",
    name: "Brioche Ensaymada",
    category: "Pastries",
    station: "PASTRY",
    basePriceCents: 11500,
    modifierGroups: [
      {
        id: "warm",
        name: "Preparation",
        required: false,
        options: [
          { id: "warm-yes", label: "Warmed", priceDeltaCents: 0 },
          { id: "warm-box", label: "Boxed As-Is", priceDeltaCents: 0 },
        ],
      },
    ],
  },
  {
    id: "menu-tuna-melt",
    sku: "MEAL-01",
    name: "Sourdough Tuna Melt",
    category: "All-Day Meals",
    station: "HOT_KITCHEN",
    basePriceCents: 21000,
    modifierGroups: [],
  },
];

export const MOCK_AR_ACCOUNTS: ArCustomerAccount[] = [
  { id: "ar-1", name: "Kirjane Office Tab", creditLimitCents: 1500000, currentBalanceCents: 218000 },
  { id: "ar-2", name: "Cebu Tech Co-Work", creditLimitCents: 1000000, currentBalanceCents: 95000 },
];

export const INITIAL_MOCK_ORDERS: OrderRecord[] = [
  {
    id: "ord-1041",
    receiptNumber: "CY-1041",
    channel: "DINE_IN",
    tableOrRef: "Table 3",
    summary: "2× Spanish Latte (Oat Milk), 1× Brioche Ensaymada",
    totalDueCents: 51500,
    tenderMode: "CASH",
    status: "PREPARING",
    createdAtLabel: "10:14 AM",
  },
  {
    id: "ord-1042",
    receiptNumber: "CY-1042",
    channel: "TAKEOUT",
    tableOrRef: "Walk-in #12",
    summary: "1× Iced Americano, 1× Sourdough Tuna Melt",
    totalDueCents: 34000,
    tenderMode: "PARTIAL_CASH_AR",
    status: "READY",
    createdAtLabel: "10:19 AM",
  },
  {
    id: "ord-1043",
    receiptNumber: "CY-1043",
    channel: "DELIVERY",
    tableOrRef: "Rider · Grab-992",
    summary: "2× House Cold Brew 500mL",
    totalDueCents: 44000,
    tenderMode: "AR",
    status: "OPEN",
    createdAtLabel: "10:22 AM",
  },
];

export const INITIAL_MOCK_TICKETS: KitchenTicket[] = [
  {
    id: "kt-1041",
    ticketNumber: "KT-201",
    receiptNumber: "CY-1041",
    station: "BARISTA",
    channel: "DINE_IN",
    tableOrRef: "Table 3",
    items: [{ name: "Spanish Latte", quantity: 2, modifiers: ["Oat Milk", "Extra Shot"] }],
    status: "IN_PROGRESS",
    elapsedMinutes: 4,
  },
  {
    id: "kt-1042",
    ticketNumber: "KT-202",
    receiptNumber: "CY-1042",
    station: "HOT_KITCHEN",
    channel: "TAKEOUT",
    tableOrRef: "Walk-in #12",
    items: [{ name: "Sourdough Tuna Melt", quantity: 1, modifiers: [], note: "Cut in half" }],
    status: "QUEUED",
    elapsedMinutes: 2,
  },
  {
    id: "kt-1043",
    ticketNumber: "KT-203",
    receiptNumber: "CY-1041",
    station: "PASTRY",
    channel: "DINE_IN",
    tableOrRef: "Table 3",
    items: [{ name: "Brioche Ensaymada", quantity: 1, modifiers: ["Warmed"] }],
    status: "READY",
    elapsedMinutes: 6,
  },
];

export interface CafeWorkstationClient {
  readonly dataSource: typeof UX_B_DATA_SOURCE;
  quoteCart(lines: CartLine[], discount: DiscountKind): {
    dataSource: typeof UX_B_DATA_SOURCE;
    subtotalCents: number;
    discountCents: number;
    totalDueCents: number;
  };
  submitSale(intent: CheckoutIntent): Promise<CommittedReceipt>;
  submitRefund(input: {
    operationId: string;
    receiptNumber: string;
    refundedCents: number;
    reason: string;
  }): Promise<RefundResult>;
  submitBlindClose(input: { operationId: string; countedCashCents: number }): Promise<BlindCloseResult>;
  transitionOrder(input: { operationId: string; orderId: string; nextStatus: OrderStatus }): Promise<OrderRecord>;
  bumpKitchenTicket(input: {
    operationId: string;
    ticketId: string;
    nextStatus: KitchenTicketStatus;
  }): Promise<KitchenTicket>;
  reprintDocument(input: { operationId: string; kind: "RECEIPT" | "KITCHEN"; reference: string }): Promise<{
    printed: boolean;
    reference: string;
  }>;
}

export type MockClientOptions = {
  /** If true, the next submitSale call throws UnknownOutcomeError (simulating lost response after commit). */
  simulateNextCheckoutUnknownOutcome?: boolean;
  /** Artificial latency in ms to test synchronous in-flight guards. */
  delayMs?: number;
};

export function createMockCafeClient(options: MockClientOptions = {}): CafeWorkstationClient & {
  setSimulateNextUnknownOutcome: (enabled: boolean) => void;
  getSubmittedOperationIds: () => string[];
} {
  let receiptSeq = 1044;
  let ticketSeq = 204;
  let simulateUnknown = Boolean(options.simulateNextCheckoutUnknownOutcome);
  const delayMs = options.delayMs ?? 0;

  const committedByOpId = new Map<string, CommittedReceipt>();
  const submittedOpIds: string[] = [];
  const orders = new Map<string, OrderRecord>(INITIAL_MOCK_ORDERS.map((o) => [o.id, { ...o }]));
  const tickets = new Map<string, KitchenTicket>(INITIAL_MOCK_TICKETS.map((t) => [t.id, { ...t }]));

  async function maybeDelay() {
    if (delayMs > 0) {
      await new Promise((resolve) => setTimeout(resolve, delayMs));
    }
  }

  function quoteCart(lines: CartLine[], discount: DiscountKind) {
    const subtotalCents = lines.reduce((sum, line) => {
      const modSum = line.selectedModifiers.reduce((acc, m) => acc + m.priceDeltaCents, 0);
      return sum + (line.item.basePriceCents + modSum) * line.quantity;
    }, 0);

    let discountCents = 0;
    if (discount === "SENIOR_CITIZEN" || discount === "PWD") {
      discountCents = Math.round(subtotalCents * 0.2);
    } else if (discount === "PROMO_10") {
      discountCents = Math.round(subtotalCents * 0.1);
    }

    return {
      dataSource: UX_B_DATA_SOURCE,
      subtotalCents,
      discountCents,
      totalDueCents: Math.max(0, subtotalCents - discountCents),
    };
  }

  return {
    dataSource: UX_B_DATA_SOURCE,

    setSimulateNextUnknownOutcome(enabled: boolean) {
      simulateUnknown = enabled;
    },

    getSubmittedOperationIds() {
      return [...submittedOpIds];
    },

    quoteCart,

    async submitSale(intent: CheckoutIntent): Promise<CommittedReceipt> {
      submittedOpIds.push(intent.operationId);
      await maybeDelay();

      if (intent.lines.length === 0) {
        throw new Error("Add at least one cafe item before tendering.");
      }

      const existing = committedByOpId.get(intent.operationId);
      if (existing) {
        return { ...existing, replayedIdempotently: true };
      }

      const quote = quoteCart(intent.lines, intent.discount);
      if (intent.tenderMode === "CASH") {
        if (intent.cashTenderedCents < quote.totalDueCents) {
          throw new Error("Cash tendered is less than the server-computed total due.");
        }
      } else if (intent.tenderMode === "AR") {
        if (!intent.arCustomerId) {
          throw new Error("Select an approved AR customer account.");
        }
      } else if (intent.tenderMode === "PARTIAL_CASH_AR") {
        if (!intent.arCustomerId) {
          throw new Error("Select an approved AR customer account for partial Cash + AR.");
        }
        if (intent.cashTenderedCents <= 0 || intent.arChargeCents <= 0) {
          throw new Error("Partial Cash + AR requires both positive cash and positive AR portions.");
        }
        if (intent.cashTenderedCents + intent.arChargeCents < quote.totalDueCents) {
          throw new Error("Combined Cash + AR tender does not cover the total due.");
        }
      }

      const receiptNumber = `CY-${receiptSeq++}`;
      const kitchenTicketNumber = `KT-${ticketSeq++}`;
      const arChargeCents =
        intent.tenderMode === "CASH"
          ? 0
          : intent.tenderMode === "AR"
            ? quote.totalDueCents
            : intent.arChargeCents;
      const cashTenderedCents = intent.tenderMode === "AR" ? 0 : intent.cashTenderedCents;
      const changeCents = Math.max(0, cashTenderedCents + arChargeCents - quote.totalDueCents);

      const committed: CommittedReceipt = {
        dataSource: UX_B_DATA_SOURCE,
        operationId: intent.operationId,
        receiptNumber,
        kitchenTicketNumber,
        channel: intent.channel,
        tableOrRef: intent.tableOrRef || intent.channel,
        tenderMode: intent.tenderMode,
        totalDueCents: quote.totalDueCents,
        cashTenderedCents,
        changeCents,
        arChargeCents,
        replayedIdempotently: false,
        committedAtIso: new Date().toISOString(),
      };

      // Persist in idempotency ledger BEFORE optional simulated timeout, modeling real lost-response recovery.
      committedByOpId.set(intent.operationId, committed);

      if (simulateUnknown) {
        simulateUnknown = false;
        throw new UnknownOutcomeError(
          intent.operationId,
          "Connection dropped before confirmation arrived. Retry with the same operation ID to reconcile without double-charging.",
        );
      }

      return committed;
    },

    async submitRefund(input) {
      await maybeDelay();
      if (!input.reason.trim()) {
        throw new Error("Refund reason is required for audit compliance.");
      }
      if (input.refundedCents <= 0) {
        throw new Error("Refund amount must be positive.");
      }
      return {
        dataSource: UX_B_DATA_SOURCE,
        operationId: input.operationId,
        receiptNumber: input.receiptNumber,
        refundedCents: input.refundedCents,
        reason: input.reason.trim(),
      };
    },

    async submitBlindClose(input) {
      await maybeDelay();
      if (input.countedCashCents < 0) {
        throw new Error("Counted cash cannot be negative.");
      }
      const expectedCashCents = 1845000;
      return {
        dataSource: UX_B_DATA_SOURCE,
        operationId: input.operationId,
        countedCashCents: input.countedCashCents,
        expectedCashCents,
        varianceCents: input.countedCashCents - expectedCashCents,
        closedAtIso: new Date().toISOString(),
      };
    },

    async transitionOrder(input) {
      await maybeDelay();
      const order = orders.get(input.orderId);
      if (!order) throw new Error(`Order ${input.orderId} not found.`);
      const updated = { ...order, status: input.nextStatus };
      orders.set(input.orderId, updated);
      return updated;
    },

    async bumpKitchenTicket(input) {
      await maybeDelay();
      const ticket = tickets.get(input.ticketId);
      if (!ticket) throw new Error(`Ticket ${input.ticketId} not found.`);
      const updated = { ...ticket, status: input.nextStatus };
      tickets.set(input.ticketId, updated);
      return updated;
    },

    async reprintDocument(input) {
      await maybeDelay();
      return { printed: true, reference: input.reference };
    },
  };
}
