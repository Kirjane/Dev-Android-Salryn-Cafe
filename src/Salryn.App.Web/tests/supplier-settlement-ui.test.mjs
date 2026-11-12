import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const page = readFileSync(new URL("../src/pages/ProcureToPayPage.tsx", import.meta.url), "utf8");
const voucher = readFileSync(new URL("../src/components/documents/PrintableChequeDisbursementModal.tsx", import.meta.url), "utf8");
const ap = readFileSync(new URL("../src/pages/AccountsPayablePage.tsx", import.meta.url), "utf8");
const api = readFileSync(new URL("../src/api/procureToPay.ts", import.meta.url), "utf8");

test("alternate supplier settlement dialog uses persisted facts and guards duplicate submission", () => {
  for (const persistedFact of [
    "paymentToApprove.supplierName",
    "paymentToApprove.invoiceNumber",
    "paymentToApprove.paymentNumber",
    "paymentToApprove.amountCents",
    "paymentToApprove.paymentMethod",
    "paymentToApprove.disbursementSource",
    "paymentToApprove.referenceNumber",
    "paymentToApprove.paymentDate",
    "paymentToApprove.status",
  ]) {
    assert.match(page, new RegExp(persistedFact.replaceAll(".", "\\.")));
  }
  assert.match(page, /paymentSettlementConfirmation\.trim\(\) !== "YES"/);
  assert.match(page, /paymentSettlementBusy \|\| paymentSettlementConfirmation/);
  assert.match(page, /if \(!paymentToApprove \|\| paymentSettlementBusy \|\| !isValidActualFundsTransferDate\(paymentActualDisbursementDate\)\) return/);
  assert.match(page, /disabled=\{paymentSettlementBusy\} onClick=\{\(\) => setPaymentApproveModalOpen\(false\)\}/);
  assert.match(page, /Salryn does not verify bank or payment-provider clearing/);
  assert.match(page, /paymentActualDisbursementDate/);
  assert.match(page, /Actual funds-transfer date/);
  assert.match(page, /approveSupplierPayment\(paymentToApprove\.id, paymentSettlementConfirmation, paymentActualDisbursementDate/);
  assert.match(ap, /isValidActualFundsTransferDate\(actualDisbursementDate\)/);
  assert.match(ap, /Actual funds-transfer date/);
  assert.match(api, /actualDisbursementDate/);
});

test("payment voucher distinguishes payee, persisted custody, status, and unsigned acknowledgment", () => {
  assert.match(voucher, /Payee \(Supplier\)/);
  assert.match(voucher, /payment\.recordedByUserId/);
  assert.match(voucher, /payment\.approvedByUserId/);
  assert.match(voucher, /payment\.approvedAt/);
  assert.match(voucher, /Pending Settlement — Not Yet Disbursed/);
  assert.match(voucher, /Operator-Confirmed Disbursement/);
  assert.match(voucher, /Recipient Acknowledgment \(Unsigned\)/);
  assert.match(voucher, /Actual funds-transfer date:/);
  assert.match(voucher, /Recorded \/ requested date:/);
  assert.match(voucher, /Unknown \/ Not recorded/);
  assert.match(voucher, /Not yet disbursed/);
  assert.match(voucher, /Bank Name:/);
  assert.doesNotMatch(voucher, /Bank Account:/);
  assert.doesNotMatch(voucher, /Official Partner Bank/);
  assert.doesNotMatch(voucher, /Payee Signature \/ Received By/);
  assert.doesNotMatch(voucher, /Admin \/ Manager/);
});
