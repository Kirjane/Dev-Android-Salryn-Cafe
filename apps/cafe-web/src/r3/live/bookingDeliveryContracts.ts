import { hostRequest } from "../../api/hostClient";

export type BookingMode = "Event" | "CoffeeCart";
export type DeliveryStatus = "Pending" | "Dispatched" | "Delivered" | "Failed" | "Cancelled";

export type BookingCustomerOption = {
  customerId: string;
  fullName: string;
  phoneNumber?: string | null;
};

export type BookingLineCommand = {
  bookingLineId?: string | null;
  productId: string;
  variantId?: string | null;
  modifierOptionIds?: string[];
  quantityX1000: number;
  lineNote?: string | null;
  displayOrder: number;
};

export type BookingConfigurationCommand = {
  customerId: string;
  bookingMode: BookingMode;
  eventName: string;
  eventReference?: string | null;
  eventStartUtc: string;
  eventEndUtc?: string | null;
  location?: string | null;
  setupAtUtc?: string | null;
  serviceWindowStartUtc?: string | null;
  serviceWindowEndUtc?: string | null;
  headcount?: number | null;
  packageLabel?: string | null;
  menuPublicationRevisionId: string;
  depositRequirementCents: number;
  specialInstructions?: string | null;
  notes?: string | null;
  intendedServiceMode: "DineIn" | "Takeout";
  lines: BookingLineCommand[];
};

export type CreateBookingCommand = {
  operationId: string;
  bookingId: string;
  revisionId: string;
  occurredAtUtc: string;
  configuration: BookingConfigurationCommand;
};

export type AdvanceBookingCommand = {
  operationId: string;
  expectedVersion: number;
  occurredAtUtc: string;
};

export type ReceiveBookingDepositCommand = {
  operationId: string;
  depositFactId: string;
  cashMovementId: string;
  bookingRevisionId: string;
  amountCents: number;
  reference: string;
  occurredAtUtc: string;
};

export type RefundAllocationCommand = {
  receivedDepositFactId: string;
  amountCents: number;
};

export type RefundBookingDepositCommand = {
  operationId: string;
  depositFactId: string;
  cashMovementId: string;
  bookingRevisionId: string;
  amountCents: number;
  reason: string;
  reference: string;
  allocations: RefundAllocationCommand[];
  occurredAtUtc: string;
};

export type CafeBookingSummary = {
  bookingId: string;
  storeId: string;
  bookingVersion: number;
  currentRevisionId: string;
  status: string;
  fulfillmentState: string;
  customerId: string;
  customerNameSnapshot: string;
  bookingMode: BookingMode;
  eventStartUtc: string;
  eventEndUtc: string | null;
  quotedTotalCents?: number;
  depositRequirementCents?: number;
  depositReceivedTotalCents?: number;
  depositRefundedTotalCents?: number;
  depositAppliedTotalCents?: number;
  preFulfillmentDepositCreditCents?: number;
  preFulfillmentOutstandingBalanceCents?: number;
};

export type CafeBookingDepositFactSummary = {
  depositFactId: string;
  bookingRevisionId: string;
  factType: "Received" | "Refunded" | "Applied" | string;
  actorUserId: string;
  amountCents: number;
  allocatedAmountCents: number;
  remainingRefundableCents: number;
  occurredAtUtc: string;
  reference: string;
};

export type CafeBookingDetail = {
  booking: CafeBookingSummary;
  currentRevision: {
    revisionId: string;
    bookingId: string;
    revisionNumber: number;
    eventName: string;
    quotedTotalCents: number;
    depositRequirementCents: number;
  };
  depositFacts?: CafeBookingDepositFactSummary[] | null;
};

export type CafeBookingMutationResult = {
  bookingId: string;
  bookingVersion: number;
  currentRevisionId: string;
  status: string;
  fulfillmentState: string;
  isReplay: boolean;
};

export type CafeBookingDepositMutationResult = {
  bookingId: string;
  bookingRevisionId: string;
  depositFactId: string;
  cashMovementId: string;
  amountCents: number;
  isReplay: boolean;
};

export type CafeDeliverySummary = {
  deliveryId: string;
  orderId: string;
  storeId: string;
  version: number;
  currentRevisionId: string;
  status: DeliveryStatus;
  createdByUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  terminalReason: string | null;
};

export type CafeDeliveryMutationResult = {
  deliveryId: string;
  orderId: string;
  version: number;
  currentRevisionId: string;
  status: DeliveryStatus;
  idempotent: boolean;
};

export type CreateDeliveryCommand = {
  operationId: string;
  deliveryId: string;
  orderId: string;
  occurredAtUtc: string;
  recipientName: string;
  contactPhone?: string | null;
  deliveryAddress: string;
  landmark?: string | null;
  instructions?: string | null;
  deliveryFeeCents: number;
  courierReference?: string | null;
};

export type AdvanceDeliveryCommand = {
  operationId: string;
  expectedVersion: number;
  occurredAtUtc: string;
};

export type ResolveDeliveryCommand = {
  operationId: string;
  expectedVersion: number;
  targetStatus: "Delivered" | "Failed";
  reason?: string | null;
  occurredAtUtc: string;
};

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";

function requireGuid(value: string, label: string): void {
  if (!GUID_RE.test(value) || value === EMPTY_GUID) {
    throw new Error(`${label} must be a non-empty GUID.`);
  }
}

function requireInstant(value: string, label: string): void {
  if (!Number.isFinite(Date.parse(value)) || !/(Z|[+-]\d\d:\d\d)$/.test(value)) {
    throw new Error(`${label} must be an explicit ISO-8601 timestamp.`);
  }
}

export async function readLiveBookings(): Promise<CafeBookingSummary[]> {
  const res = await hostRequest<CafeBookingSummary[]>("/api/cafe/bookings", undefined, "GET");
  return Array.isArray(res) ? res : [];
}

export async function readLiveBookingDetail(bookingId: string): Promise<CafeBookingDetail | null> {
  requireGuid(bookingId, "bookingId");
  return hostRequest<CafeBookingDetail | null>(`/api/cafe/bookings/${bookingId}`, undefined, "GET");
}

type ReceivableCustomerBalanceDto = {
  customer?: {
    id?: string;
    fullName?: string;
    phone?: string | null;
  };
  customerId?: string;
  fullName?: string;
  phoneNumber?: string | null;
};

export async function readBookingCustomers(): Promise<BookingCustomerOption[]> {
  const res = await hostRequest<ReceivableCustomerBalanceDto[]>("/api/cafe/bookings/customers", undefined, "GET");
  if (!Array.isArray(res)) return [];
  const options: BookingCustomerOption[] = [];
  for (const row of res) {
    const id = row?.customer?.id ?? row?.customerId;
    const name = row?.customer?.fullName ?? row?.fullName;
    if (typeof id === "string" && id && typeof name === "string" && name) {
      options.push({
        customerId: id,
        fullName: name,
        phoneNumber: row?.customer?.phone ?? row?.phoneNumber ?? null,
      });
    }
  }
  return options;
}

export async function createBooking(command: CreateBookingCommand): Promise<CafeBookingMutationResult> {
  requireGuid(command.operationId, "operationId");
  requireGuid(command.bookingId, "bookingId");
  requireGuid(command.revisionId, "revisionId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");

  const cfg = command.configuration;
  requireGuid(cfg.customerId, "configuration.customerId");
  if (cfg.bookingMode !== "Event" && cfg.bookingMode !== "CoffeeCart") {
    throw new Error("BookingMode must be 'Event' or 'CoffeeCart'.");
  }
  if (!cfg.eventName.trim()) throw new Error("Event name is required.");
  if (!cfg.menuPublicationRevisionId.trim()) throw new Error("menuPublicationRevisionId is required.");
  requireInstant(cfg.eventStartUtc, "configuration.eventStartUtc");
  if (!Number.isSafeInteger(cfg.depositRequirementCents) || cfg.depositRequirementCents < 0) {
    throw new Error("depositRequirementCents must be a nonnegative integer.");
  }
  if (!cfg.lines.length) throw new Error("At least one booking line is required.");
  for (const line of cfg.lines) {
    requireGuid(line.productId, "line.productId");
    if (!Number.isSafeInteger(line.quantityX1000) || line.quantityX1000 <= 0) {
      throw new Error("Booking line quantityX1000 must be a positive integer.");
    }
  }

  return hostRequest<CafeBookingMutationResult>(
    "/api/cafe/bookings",
    command,
    "POST",
    command.operationId,
  );
}

export async function confirmBooking(
  bookingId: string,
  command: AdvanceBookingCommand,
): Promise<CafeBookingMutationResult> {
  requireGuid(bookingId, "bookingId");
  requireGuid(command.operationId, "operationId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!Number.isSafeInteger(command.expectedVersion) || command.expectedVersion <= 0) {
    throw new Error("expectedVersion must be a positive integer.");
  }
  return hostRequest<CafeBookingMutationResult>(
    `/api/cafe/bookings/${bookingId}/confirm`,
    command,
    "POST",
    command.operationId,
  );
}

export async function receiveBookingDeposit(
  bookingId: string,
  command: ReceiveBookingDepositCommand,
): Promise<CafeBookingDepositMutationResult> {
  requireGuid(bookingId, "bookingId");
  requireGuid(command.operationId, "operationId");
  requireGuid(command.depositFactId, "depositFactId");
  requireGuid(command.cashMovementId, "cashMovementId");
  requireGuid(command.bookingRevisionId, "bookingRevisionId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!Number.isSafeInteger(command.amountCents) || command.amountCents <= 0) {
    throw new Error("Deposit amountCents must be a positive integer.");
  }
  if (!command.reference.trim()) throw new Error("Deposit reference is required.");
  return hostRequest<CafeBookingDepositMutationResult>(
    `/api/cafe/bookings/${bookingId}/deposits/receive`,
    command,
    "POST",
    command.operationId,
  );
}

export async function refundBookingDeposit(
  bookingId: string,
  command: RefundBookingDepositCommand,
): Promise<CafeBookingDepositMutationResult> {
  requireGuid(bookingId, "bookingId");
  requireGuid(command.operationId, "operationId");
  requireGuid(command.depositFactId, "depositFactId");
  requireGuid(command.cashMovementId, "cashMovementId");
  requireGuid(command.bookingRevisionId, "bookingRevisionId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!Number.isSafeInteger(command.amountCents) || command.amountCents <= 0) {
    throw new Error("Refund amountCents must be a positive integer.");
  }
  if (!command.reason.trim() || !command.reference.trim()) {
    throw new Error("Refund reason and reference are required.");
  }
  if (!command.allocations.length) {
    throw new Error("At least one received-deposit allocation is required.");
  }
  let sum = 0;
  for (const alloc of command.allocations) {
    requireGuid(alloc.receivedDepositFactId, "allocation.receivedDepositFactId");
    if (!Number.isSafeInteger(alloc.amountCents) || alloc.amountCents <= 0) {
      throw new Error("Allocation amountCents must be a positive integer.");
    }
    sum += alloc.amountCents;
  }
  if (sum !== command.amountCents) {
    throw new Error("Refund allocation sum must equal total refund amountCents.");
  }
  return hostRequest<CafeBookingDepositMutationResult>(
    `/api/cafe/bookings/${bookingId}/deposits/refund`,
    command,
    "POST",
    command.operationId,
  );
}

export async function readLiveDeliveries(): Promise<CafeDeliverySummary[]> {
  const res = await hostRequest<CafeDeliverySummary[]>("/api/cafe/deliveries", undefined, "GET");
  return Array.isArray(res) ? res : [];
}

export async function createDelivery(command: CreateDeliveryCommand): Promise<CafeDeliveryMutationResult> {
  requireGuid(command.operationId, "operationId");
  requireGuid(command.deliveryId, "deliveryId");
  requireGuid(command.orderId, "orderId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!command.recipientName.trim() || !command.deliveryAddress.trim()) {
    throw new Error("Recipient name and delivery address are required.");
  }
  if (!Number.isSafeInteger(command.deliveryFeeCents) || command.deliveryFeeCents < 0) {
    throw new Error("deliveryFeeCents must be a nonnegative integer.");
  }
  return hostRequest<CafeDeliveryMutationResult>(
    "/api/cafe/deliveries",
    command,
    "POST",
    command.operationId,
  );
}

export async function dispatchDelivery(
  deliveryId: string,
  command: AdvanceDeliveryCommand,
): Promise<CafeDeliveryMutationResult> {
  requireGuid(deliveryId, "deliveryId");
  requireGuid(command.operationId, "operationId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!Number.isSafeInteger(command.expectedVersion) || command.expectedVersion <= 0) {
    throw new Error("expectedVersion must be a positive integer.");
  }
  return hostRequest<CafeDeliveryMutationResult>(
    `/api/cafe/deliveries/${deliveryId}/dispatch`,
    command,
    "POST",
    command.operationId,
  );
}

export async function resolveDelivery(
  deliveryId: string,
  command: ResolveDeliveryCommand,
): Promise<CafeDeliveryMutationResult> {
  requireGuid(deliveryId, "deliveryId");
  requireGuid(command.operationId, "operationId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (command.targetStatus !== "Delivered" && command.targetStatus !== "Failed") {
    throw new Error("Resolve targetStatus must be 'Delivered' or 'Failed'.");
  }
  if (!Number.isSafeInteger(command.expectedVersion) || command.expectedVersion <= 0) {
    throw new Error("expectedVersion must be a positive integer.");
  }
  return hostRequest<CafeDeliveryMutationResult>(
    `/api/cafe/deliveries/${deliveryId}/resolve`,
    command,
    "POST",
    command.operationId,
  );
}

export type ReviseBookingCommand = {
  operationId: string;
  revisionId: string;
  expectedVersion: number;
  occurredAtUtc: string;
  configuration: BookingConfigurationCommand;
};

export type CancelBookingCommand = {
  operationId: string;
  expectedVersion: number;
  reason: string;
  occurredAtUtc: string;
};

export type StartBookingFulfillmentCommand = {
  operationId: string;
  orderId: string;
  expectedVersion: number;
  occurredAtUtc: string;
};

export type BookingFulfillmentResult = {
  bookingId: string;
  orderId: string;
  bookingVersion: number;
  status: string;
  fulfillmentState: string;
  isReplay?: boolean;
};

export type ReviseDeliveryCommand = {
  operationId: string;
  expectedVersion: number;
  occurredAtUtc: string;
  recipientName: string;
  contactPhone?: string | null;
  deliveryAddress: string;
  landmark?: string | null;
  instructions?: string | null;
  deliveryFeeCents: number;
  courierReference?: string | null;
};

export type CancelDeliveryCommand = {
  operationId: string;
  expectedVersion: number;
  reason: string;
  occurredAtUtc: string;
};

export async function reviseBooking(
  bookingId: string,
  command: ReviseBookingCommand,
): Promise<CafeBookingMutationResult> {
  requireGuid(bookingId, "bookingId");
  requireGuid(command.operationId, "operationId");
  requireGuid(command.revisionId, "revisionId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  return hostRequest<CafeBookingMutationResult>(
    `/api/cafe/bookings/${bookingId}/revision`,
    command,
    "POST",
    command.operationId,
  );
}

export async function markBookingReadyForFulfillment(
  bookingId: string,
  command: AdvanceBookingCommand,
): Promise<CafeBookingMutationResult> {
  requireGuid(bookingId, "bookingId");
  requireGuid(command.operationId, "operationId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  return hostRequest<CafeBookingMutationResult>(
    `/api/cafe/bookings/${bookingId}/ready-for-fulfillment`,
    command,
    "POST",
    command.operationId,
  );
}

export async function startBookingFulfillment(
  bookingId: string,
  command: StartBookingFulfillmentCommand,
): Promise<BookingFulfillmentResult> {
  requireGuid(bookingId, "bookingId");
  requireGuid(command.operationId, "operationId");
  requireGuid(command.orderId, "orderId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  return hostRequest<BookingFulfillmentResult>(
    `/api/cafe/bookings/${bookingId}/fulfillment/start`,
    command,
    "POST",
    command.operationId,
  );
}

export async function cancelBooking(
  bookingId: string,
  command: CancelBookingCommand,
): Promise<CafeBookingMutationResult> {
  requireGuid(bookingId, "bookingId");
  requireGuid(command.operationId, "operationId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!command.reason.trim()) throw new Error("Cancellation reason is required.");
  return hostRequest<CafeBookingMutationResult>(
    `/api/cafe/bookings/${bookingId}/cancel`,
    command,
    "POST",
    command.operationId,
  );
}

export async function reviseDelivery(
  deliveryId: string,
  command: ReviseDeliveryCommand,
): Promise<CafeDeliveryMutationResult> {
  requireGuid(deliveryId, "deliveryId");
  requireGuid(command.operationId, "operationId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!command.recipientName.trim() || !command.deliveryAddress.trim()) {
    throw new Error("Recipient name and delivery address are required.");
  }
  return hostRequest<CafeDeliveryMutationResult>(
    `/api/cafe/deliveries/${deliveryId}/revision`,
    command,
    "POST",
    command.operationId,
  );
}

export async function cancelDelivery(
  deliveryId: string,
  command: CancelDeliveryCommand,
): Promise<CafeDeliveryMutationResult> {
  requireGuid(deliveryId, "deliveryId");
  requireGuid(command.operationId, "operationId");
  requireInstant(command.occurredAtUtc, "occurredAtUtc");
  if (!command.reason.trim()) throw new Error("Cancellation reason is required.");
  return hostRequest<CafeDeliveryMutationResult>(
    `/api/cafe/deliveries/${deliveryId}/cancel`,
    command,
    "POST",
    command.operationId,
  );
}
