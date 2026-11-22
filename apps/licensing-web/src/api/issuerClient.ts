import type {
  CreateLicenseDraftInput,
  DraftCreateResponse,
  IssueResponse,
  IssuerAuditEntry,
  IssuerCustomerRecord,
  IssuerDashboardSummary,
  IssuerLicenseRequestRecord,
  IssuerMetadata,
  IssuerSessionInfo,
  IssuerStoreRecord,
} from "../types";

export class IssuerApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = "IssuerApiError";
    this.status = status;
    this.code = code;
  }
}

export interface LicensingIssuerApi {
  getSession(): Promise<IssuerSessionInfo>;
  login(username: string, password: string): Promise<IssuerSessionInfo>;
  logout(): Promise<IssuerSessionInfo>;
  getDashboard(): Promise<IssuerDashboardSummary>;
  getMetadata(): Promise<IssuerMetadata>;
  listCustomers(): Promise<IssuerCustomerRecord[]>;
  createCustomer(input: { customerName: string; contactEmail?: string; notes?: string }): Promise<IssuerCustomerRecord>;
  listStores(): Promise<IssuerStoreRecord[]>;
  createStore(input: { customerId: string; storeCode: string; storeName: string; productId: string }): Promise<IssuerStoreRecord>;
  listRequests(): Promise<IssuerLicenseRequestRecord[]>;
  createDraft(input: CreateLicenseDraftInput): Promise<DraftCreateResponse>;
  approveRequest(id: string, note?: string): Promise<IssuerLicenseRequestRecord>;
  rejectRequest(id: string, note?: string): Promise<IssuerLicenseRequestRecord>;
  issueRequest(id: string): Promise<IssueResponse>;
  reissueRequest(id: string, input: { machineBinding?: string; expiresAtUtc?: string | null; note?: string }): Promise<DraftCreateResponse>;
  downloadUrl(id: string): string;
  listAudit(limit?: number): Promise<IssuerAuditEntry[]>;
}

const CSRF_HEADER = "X-Salryn-Issuer-Csrf";

export function createBrowserIssuerApi(baseUrl = ""): LicensingIssuerApi {
  let csrfToken: string | null = null;

  async function request<T>(path: string, init: RequestInit = {}, includeCsrf = false): Promise<T> {
    const headers = new Headers(init.headers ?? {});
    if (init.body && !headers.has("Content-Type")) {
      headers.set("Content-Type", "application/json");
    }
    if (includeCsrf && csrfToken) {
      headers.set(CSRF_HEADER, csrfToken);
    }

    const resp = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers,
      credentials: "same-origin",
    });

    if (!resp.ok) {
      let code = `HTTP_${resp.status}`;
      let message = `Issuer request failed (${resp.status}).`;
      try {
        const body = (await resp.json()) as { code?: string; message?: string };
        if (body.code) code = body.code;
        if (body.message) message = body.message;
      } catch {
        // fallback message
      }
      throw new IssuerApiError(resp.status, code, message);
    }

    return (await resp.json()) as T;
  }

  return {
    async getSession() {
      const s = await request<IssuerSessionInfo>("/api/issuer/auth/session");
      csrfToken = s.csrfToken;
      return s;
    },
    async login(username, password) {
      const s = await request<IssuerSessionInfo>("/api/issuer/auth/login", {
        method: "POST",
        body: JSON.stringify({ username, password }),
      });
      csrfToken = s.csrfToken;
      return s;
    },
    async logout() {
      const s = await request<IssuerSessionInfo>(
        "/api/issuer/auth/logout",
        { method: "POST", body: JSON.stringify({}) },
        true,
      );
      csrfToken = null;
      return s;
    },
    getDashboard: () => request<IssuerDashboardSummary>("/api/issuer/dashboard"),
    getMetadata: () => request<IssuerMetadata>("/api/issuer/metadata"),
    listCustomers: () => request<IssuerCustomerRecord[]>("/api/issuer/customers"),
    createCustomer: (input) =>
      request<IssuerCustomerRecord>(
        "/api/issuer/customers",
        { method: "POST", body: JSON.stringify(input) },
        true,
      ),
    listStores: () => request<IssuerStoreRecord[]>("/api/issuer/stores"),
    createStore: (input) =>
      request<IssuerStoreRecord>(
        "/api/issuer/stores",
        { method: "POST", body: JSON.stringify(input) },
        true,
      ),
    listRequests: () => request<IssuerLicenseRequestRecord[]>("/api/issuer/requests"),
    createDraft: (input) =>
      request<DraftCreateResponse>(
        "/api/issuer/requests",
        { method: "POST", body: JSON.stringify(input) },
        true,
      ),
    approveRequest: (id, note) =>
      request<IssuerLicenseRequestRecord>(
        `/api/issuer/requests/${encodeURIComponent(id)}/approve`,
        { method: "POST", body: JSON.stringify({ note: note ?? null }) },
        true,
      ),
    rejectRequest: (id, note) =>
      request<IssuerLicenseRequestRecord>(
        `/api/issuer/requests/${encodeURIComponent(id)}/reject`,
        { method: "POST", body: JSON.stringify({ note: note ?? null }) },
        true,
      ),
    issueRequest: (id) =>
      request<IssueResponse>(
        `/api/issuer/requests/${encodeURIComponent(id)}/issue`,
        { method: "POST", body: JSON.stringify({}) },
        true,
      ),
    reissueRequest: (id, input) =>
      request<DraftCreateResponse>(
        `/api/issuer/requests/${encodeURIComponent(id)}/reissue`,
        { method: "POST", body: JSON.stringify(input) },
        true,
      ),
    downloadUrl: (id) => `${baseUrl}/api/issuer/requests/${encodeURIComponent(id)}/download`,
    listAudit: (limit = 100) => request<IssuerAuditEntry[]>(`/api/issuer/audit?limit=${limit}`),
  };
}
