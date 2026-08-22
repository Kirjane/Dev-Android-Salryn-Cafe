export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly payload?: unknown;
  readonly checkoutStatus?: string;
  readonly checkoutMessage?: string;

  constructor(
    message: string,
    status: number,
    code?: string,
    payload?: unknown,
    checkoutStatus?: string,
    checkoutMessage?: string,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.payload = payload;
    this.checkoutStatus = checkoutStatus;
    this.checkoutMessage = checkoutMessage;
  }
}

type ErrorResponseBody = {
  error?: string;
  message?: string;
  code?: string;
  checkout?: {
    status?: string;
    message?: string;
    receiptNumber?: string | null;
    saleId?: string | null;
    totalCents?: number | null;
    changeCents?: number | null;
  };
};

function readCheckoutFields(body: ErrorResponseBody | null): {
  checkoutStatus?: string;
  checkoutMessage?: string;
} {
  return {
    checkoutStatus: body?.checkout?.status,
    checkoutMessage: body?.checkout?.message,
  };
}

export async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as ErrorResponseBody | null;
    const { checkoutStatus, checkoutMessage } = readCheckoutFields(body);
    throw new ApiError(
      body?.error || body?.message || checkoutMessage || `Request failed (${response.status})`,
      response.status,
      body?.code,
      body,
      checkoutStatus,
      checkoutMessage,
    );
  }

  return response.json() as Promise<T>;
}