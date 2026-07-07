export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly payload?: unknown;

  constructor(message: string, status: number, code?: string, payload?: unknown) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.payload = payload;
  }
}

type ErrorResponseBody = {
  error?: string;
  message?: string;
  code?: string;
};

export async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as ErrorResponseBody | null;
    throw new ApiError(
      body?.error || body?.message || `Request failed (${response.status})`,
      response.status,
      body?.code,
      body,
    );
  }

  return response.json() as Promise<T>;
}
