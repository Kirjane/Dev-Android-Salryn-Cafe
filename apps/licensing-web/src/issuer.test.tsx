import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { describe, expect, it, vi } from "vitest";
import { IssuerApp } from "./IssuerApp";
import type { LicensingIssuerApi } from "./api/issuerClient";
import type {
  IssuerAuditEntry,
  IssuerCustomerRecord,
  IssuerDashboardSummary,
  IssuerLicenseRequestRecord,
  IssuerMetadata,
  IssuerSessionInfo,
  IssuerStoreRecord,
  SignerStatusSnapshot,
} from "./types";

function makeSigner(overrides: Partial<SignerStatusSnapshot> = {}): SignerStatusSnapshot {
  return {
    isConfigured: false,
    canIssue: false,
    signerMode: "Unconfigured",
    publicKeySpkiSha256: null,
    trustedRuntimeSpkiSha256: "30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4",
    matchesTrustedRuntimeKey: false,
    message: "SIGNER_NOT_CONFIGURED: No authorized external private signing key is configured on the server.",
    ...overrides,
  };
}

function createFakeIssuerApi(signer: SignerStatusSnapshot = makeSigner(), authenticated = false): LicensingIssuerApi {
  let authed = authenticated;
  const customers: IssuerCustomerRecord[] = [];
  const stores: IssuerStoreRecord[] = [];
  const requests: IssuerLicenseRequestRecord[] = [];
  const audits: IssuerAuditEntry[] = [];

  const sessionObj = (): IssuerSessionInfo =>
    authed
      ? {
          isAuthenticated: true,
          csrfToken: "csrf-test-token",
          user: {
            id: "u1",
            username: "kirjane-admin",
            displayName: "Kirjane Admin",
            role: "IssuerAdmin",
            isActive: true,
            createdAtUtc: "2026-11-23T00:00:00Z",
          },
        }
      : { isAuthenticated: false, csrfToken: null, user: null };

  return {
    getSession: vi.fn(async () => sessionObj()),
    login: vi.fn(async () => {
      authed = true;
      return sessionObj();
    }),
    logout: vi.fn(async () => {
      authed = false;
      return sessionObj();
    }),
    getDashboard: vi.fn(async (): Promise<IssuerDashboardSummary> => ({
      draftCount: 0,
      awaitingApprovalCount: requests.filter((r) => r.status === "AwaitingApproval").length,
      approvedCount: requests.filter((r) => r.status === "Approved").length,
      issuedCount: requests.filter((r) => r.status === "Issued").length,
      expiredIssuedCount: 0,
      supersededCount: requests.filter((r) => r.status === "Superseded").length,
      rejectedCount: 0,
      customerCount: customers.length,
      storeCount: stores.length,
      signer,
    })),
    getMetadata: vi.fn(async (): Promise<IssuerMetadata> => ({
      products: ["Salryn.Cafe", "Salryn.POS"],
      licenseTypes: ["Host", "Terminal"],
      editions: ["Trial", "Local", "Professional", "Enterprise"],
      allFeatures: ["Pos", "Rmo", "Reports", "Users", "Backup", "Licensing", "LanTerminals", "ReceivablesUtangLedger"],
      terminalAllowedFeatures: ["LanTerminals"],
      signer,
    })),
    listCustomers: vi.fn(async () => [...customers]),
    createCustomer: vi.fn(async (input) => {
      const rec: IssuerCustomerRecord = {
        id: `cust-${customers.length + 1}`,
        customerName: input.customerName,
        contactEmail: input.contactEmail ?? null,
        notes: input.notes ?? null,
        createdAtUtc: "2026-11-23T00:00:00Z",
        updatedAtUtc: "2026-11-23T00:00:00Z",
      };
      customers.push(rec);
      return rec;
    }),
    listStores: vi.fn(async () => [...stores]),
    createStore: vi.fn(async (input) => {
      const rec: IssuerStoreRecord = {
        id: `store-${stores.length + 1}`,
        customerId: input.customerId,
        storeCode: input.storeCode,
        storeName: input.storeName,
        productId: input.productId,
        createdAtUtc: "2026-11-23T00:00:00Z",
        updatedAtUtc: "2026-11-23T00:00:00Z",
      };
      stores.push(rec);
      return rec;
    }),
    listRequests: vi.fn(async () => [...requests]),
    createDraft: vi.fn(async (input) => {
      const store = stores.find((s) => s.id === input.storeId)!;
      const cust = customers.find((c) => c.id === input.customerId)!;
      const rec: IssuerLicenseRequestRecord = {
        id: `req-${requests.length + 1}`,
        licenseId: `SLRN-CAFE-20261123-0000000${requests.length + 1}`,
        idempotencyKey: null,
        status: "AwaitingApproval",
        customerId: cust.id,
        customerName: cust.customerName,
        storeId: store.id,
        storeCode: store.storeCode,
        storeName: store.storeName,
        applicationId: input.applicationId,
        licenseType: input.licenseType,
        edition: input.edition,
        machineBinding: input.machineBinding,
        features: input.features,
        allowedLanTerminals: input.allowedLanTerminals,
        hostLicenseId: input.hostLicenseId,
        terminalId: input.terminalId,
        issuedAtUtc: "2026-11-23T00:00:00Z",
        expiresAtUtc: input.expiresAtUtc,
        supportExpiresAtUtc: null,
        updatesUntilUtc: null,
        supersedesRequestId: null,
        canonicalSigningPayload: `{"applicationId":"${input.applicationId}","machineBinding":"${input.machineBinding}"}`,
        signedLicenseJson: null,
        signedLicenseSha256: null,
        signerKeySpkiSha256: null,
        signerMode: null,
        verifierPassed: false,
        reviewNote: null,
        createdBy: "kirjane-admin",
        approvedBy: null,
        issuedBy: null,
        createdAtUtc: "2026-11-23T00:00:00Z",
        approvedAtUtc: null,
        issuedAtServerUtc: null,
      };
      requests.push(rec);
      return {
        request: rec,
        warnings: ["Offline license delivery cannot be remotely revoked."],
        isReplay: false,
      };
    }),
    approveRequest: vi.fn(async (id, note) => {
      const rec = requests.find((r) => r.id === id)!;
      rec.status = "Approved";
      rec.approvedBy = "kirjane-admin";
      rec.reviewNote = note ?? null;
      return { ...rec };
    }),
    rejectRequest: vi.fn(async (id) => {
      const rec = requests.find((r) => r.id === id)!;
      rec.status = "Rejected";
      return { ...rec };
    }),
    issueRequest: vi.fn(async (id) => {
      if (!signer.canIssue) {
        throw new Error("SIGNER_NOT_CONFIGURED: No authorized external private signing key is configured.");
      }
      const rec = requests.find((r) => r.id === id)!;
      rec.status = "Issued";
      rec.issuedBy = "kirjane-admin";
      rec.signedLicenseJson = `{"licenseId":"${rec.licenseId}","signature":"TEST_SIG"}`;
      rec.signedLicenseSha256 = "abc123sha256";
      rec.signerKeySpkiSha256 = signer.publicKeySpkiSha256;
      rec.signerMode = signer.signerMode;
      rec.verifierPassed = true;
      return { request: { ...rec }, alreadyIssued: false };
    }),
    reissueRequest: vi.fn(async (id, input) => {
      const prior = requests.find((r) => r.id === id)!;
      const rec: IssuerLicenseRequestRecord = {
        ...prior,
        id: `req-${requests.length + 1}`,
        licenseId: `SLRN-CAFE-20261123-REISSUE1`,
        status: "AwaitingApproval",
        machineBinding: input.machineBinding ?? prior.machineBinding,
        supersedesRequestId: prior.id,
        signedLicenseJson: null,
      };
      requests.push(rec);
      return { request: rec, warnings: [], isReplay: false };
    }),
    downloadUrl: (id) => `/api/issuer/requests/${id}/download`,
    listAudit: vi.fn(async () => audits),
  };
}

describe("Kirjane Labs Standalone Licensing Issuer Web UI", () => {
  it("gates all admin screens behind sign-in and surfaces honest SIGNER_NOT_CONFIGURED posture after login", async () => {
    const user = userEvent.setup();
    const api = createFakeIssuerApi(makeSigner(), false);

    render(<IssuerApp api={api} />);
    expect(await screen.findByText("Kirjane Labs Licensing Issuer")).toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/Administrator Username/i), "kirjane-admin");
    await user.type(screen.getByLabelText(/Password/i), "Synthetic-Issuer!2026");
    await user.click(screen.getByRole("button", { name: "Sign In to Licensing Issuer" }));

    expect(await screen.findByText(/Signing Blocked — Unconfigured/i)).toBeInTheDocument();
    expect(screen.getByText(/SIGNER_NOT_CONFIGURED/i)).toBeInTheDocument();
  });

  it("supports end-to-end Customer -> Store -> Draft -> Approve -> Issue -> Download & Reissue workflow", async () => {
    const user = userEvent.setup();
    const api = createFakeIssuerApi(
      makeSigner({
        isConfigured: true,
        canIssue: true,
        signerMode: "TestUntrustedKey",
        publicKeySpkiSha256: "testspki000111222",
        message: "NON-PRODUCTION TEST SIGNER ACTIVE.",
      }),
      true,
    );

    render(<IssuerApp api={api} />);
    expect(await screen.findByText("Non-Production Test Signer Active (TestUntrustedKey)")).toBeInTheDocument();

    // Register customer + store
    await user.click(screen.getByRole("button", { name: /Customers & Stores/i }));
    await user.type(screen.getByLabelText(/Customer \/ Organization Name/i), "Cafe Yza");
    await user.click(screen.getByRole("button", { name: "Register Customer" }));
    expect(await screen.findByText(/Registered customer "Cafe Yza"/i)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Store Code/i), "CAFE-YZA-01");
    await user.type(screen.getByLabelText(/Store Display Name/i), "Cafe Yza Main");
    await user.click(screen.getByRole("button", { name: "Register Store" }));
    expect(await screen.findByText(/Registered store "CAFE-YZA-01"/i)).toBeInTheDocument();

    // Create draft
    await user.click(screen.getByRole("button", { name: /New License Draft/i }));
    await user.type(screen.getByLabelText(/Customer Workstation Machine Code/i), "SALRYN-MC-F36B-76CD-77FB-03EC");
    await user.click(screen.getByRole("button", { name: "Create Draft for Review" }));

    expect(await screen.findByRole("button", { name: "Approve for Issuance" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Approve for Issuance" }));

    expect(await screen.findByRole("button", { name: "Issue & Sign Offline License" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Issue & Sign Offline License" }));

    expect(await screen.findByRole("button", { name: "Download Signed JSON" })).toBeInTheDocument();
    expect(screen.getByText(/abc123sha256/i)).toBeInTheDocument();

    // Reissue creates a linked draft
    await user.click(screen.getByRole("button", { name: "Create Reissue Draft" }));
    expect(await screen.findByText(/Created reissue draft SLRN-CAFE-20261123-REISSUE1/i)).toBeInTheDocument();
  });

  it("has zero critical or serious axe-core accessibility violations on Sign-In and authenticated Dashboard", async () => {
    const loginView = render(<IssuerApp api={createFakeIssuerApi(makeSigner(), false)} />);
    await screen.findByText("Kirjane Labs Licensing Issuer");
    const loginAudit = await axe.run(loginView.container);
    const loginSevere = loginAudit.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    expect(loginSevere).toEqual([]);
    loginView.unmount();

    const authedView = render(<IssuerApp api={createFakeIssuerApi(makeSigner(), true)} />);
    await screen.findByText("Key Custody & Offline Verification Boundary");
    const authedAudit = await axe.run(authedView.container);
    const authedSevere = authedAudit.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    expect(authedSevere).toEqual([]);
  });
});
