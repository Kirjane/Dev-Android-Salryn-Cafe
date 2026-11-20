import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { describe, expect, it, vi } from "vitest";
import { createMockCafeClient } from "./mockCafeClient";
import { WorkstationApp } from "./WorkstationApp";

const UUID_V4_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

describe("Salryn Cafe UX-B — Cashier, Orders & Kitchen Workstation (MOCKED_UI)", () => {
  it("supports modifier selection, Cash tender, and server-authoritative receipt commitment", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    expect(screen.getByText("MOCKED_UI")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Spanish Latte/i }));
    const dialog = screen.getByRole("dialog", { name: "Configure Spanish Latte" });

    await user.selectOptions(within(dialog).getByLabelText(/Milk Choice/i), "milk-oat");
    await user.click(within(dialog).getByRole("button", { name: "Add to Ticket" }));

    expect(screen.getAllByText("₱200.00").length).toBeGreaterThanOrEqual(1);

    await user.click(screen.getByRole("button", { name: /Complete Sale \(Cash Only\)/i }));
    expect(await screen.findByText(/Committed Receipt · CY-1044/i)).toBeInTheDocument();
  });

  it("enforces Cash / AR / Partial Cash+AR tenders only and commits Partial Cash + AR", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    const tenderSelect = screen.getByLabelText("Tender Mode") as HTMLSelectElement;
    const optionValues = Array.from(tenderSelect.options).map((o) => o.value);
    expect(optionValues).toEqual(["CASH", "AR", "PARTIAL_CASH_AR"]);

    await user.click(screen.getByRole("button", { name: /House Cold Brew/i }));
    await user.selectOptions(tenderSelect, "PARTIAL_CASH_AR");

    await user.type(screen.getByRole("textbox", { name: "Cash Tendered (PHP)" }), "120");
    await user.type(screen.getByRole("textbox", { name: "AR Portion (PHP)" }), "100");

    await user.click(screen.getByRole("button", { name: /Complete Sale \(Partial Cash \+ AR\)/i }));
    expect(await screen.findByText(/Committed Receipt · CY-1044/i)).toBeInTheDocument();
  });

  it("reuses the EXACT same RFC-4122 Guid operationId on retry after Unknown-Outcome failure and locks edits until Retry/Discard", async () => {
    const user = userEvent.setup();
    const client = createMockCafeClient({ simulateNextCheckoutUnknownOutcome: true });

    render(<WorkstationApp client={client} />);

    await user.click(screen.getByRole("button", { name: /House Cold Brew/i }));
    await user.click(screen.getByRole("button", { name: /Complete Sale/i }));

    expect(await screen.findByText(/Unknown Checkout Outcome · Operation/i)).toBeInTheDocument();
    // Edits are locked while unknownOutcome is active
    expect(screen.getByLabelText("Tender Mode")).toBeDisabled();
    expect(screen.getByLabelText("Discount Policy")).toBeDisabled();

    await user.click(screen.getByRole("button", { name: /Retry & Verify Same Operation ID/i }));
    expect(await screen.findByText(/Idempotent Recovery \(No Duplicate Charge\)/i)).toBeInTheDocument();

    const submittedIds = client.getSubmittedOperationIds();
    expect(submittedIds).toHaveLength(2);
    expect(submittedIds[0]).toMatch(UUID_V4_REGEX);
    expect(submittedIds[0]).toBe(submittedIds[1]);
  });

  it("blocks rapid synchronous double-submit via ref-based in-flight guard (single act tick)", async () => {
    const user = userEvent.setup();
    const client = createMockCafeClient({ delayMs: 80 });
    const submitSpy = vi.spyOn(client, "submitSale");

    render(<WorkstationApp client={client} />);

    await user.click(screen.getByRole("button", { name: /House Cold Brew/i }));
    const submitBtn = screen.getByRole("button", { name: /Complete Sale/i });

    // Both clicks run synchronously BEFORE React flushes disabled state — proves inFlightRef guard.
    act(() => {
      submitBtn.click();
      submitBtn.click();
    });

    await waitFor(() => {
      expect(screen.getByText(/Committed Receipt · CY-1044/i)).toBeInTheDocument();
    });
    expect(submitSpy).toHaveBeenCalledTimes(1);
  });

  it("locks Checkout, Audited Refund, and Blind Shift Close when terminal is REVOKED", () => {
    render(<WorkstationApp initialTrustState="REVOKED" />);
    expect(screen.getByRole("alert")).toHaveTextContent(/Terminal Revoked — Checkout Locked/i);
    expect(screen.getByRole("button", { name: /Complete Sale/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Audited Refund/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Blind Shift Close/i })).toBeDisabled();
  });

  it("supports Blind Shift Close without exposing expected totals (text or placeholder) before submission", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    await user.click(screen.getByRole("button", { name: "Blind Shift Close" }));
    const dialog = screen.getByRole("dialog", { name: "Blind Shift Close" });
    const countInput = within(dialog).getByRole("textbox", { name: "Counted Drawer Cash (PHP)" });

    expect(within(dialog).queryByText(/18,450\.00/)).not.toBeInTheDocument();
    expect(countInput).not.toHaveAttribute("placeholder", expect.stringMatching(/18450/));
    expect(countInput).toHaveAttribute("placeholder", "0.00");

    await user.type(countInput, "18400");
    await user.click(within(dialog).getByRole("button", { name: "Submit Blind Count" }));

    expect(await within(dialog).findByText(/Shift Variance Computed by Server/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/-₱50\.00/)).toBeInTheDocument();
  });

  it("transitions Order lifecycle and bumps Kitchen Display tickets", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    await user.click(screen.getByRole("button", { name: /Orders & Lifecycle/i }));
    expect(screen.getByRole("table", { name: "Cafe Order Lifecycle" })).toBeInTheDocument();

    const startPrepBtns = screen.getAllByRole("button", { name: "Start Prep" });
    await user.click(startPrepBtns[0]);
    expect(await screen.findByText(/Order CY-1043 → PREPARING/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Kitchen & Barista/i }));
    expect(screen.getByText(/KT-201 · Table 3/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Mark Ready" }));
    expect(await screen.findByText(/Ticket KT-201 → READY/i)).toBeInTheDocument();
  });

  it("passes axe-core accessibility audit with zero violations", async () => {
    const { container } = render(<WorkstationApp />);
    const results = await axe.run(container, {
      rules: {
        "color-contrast": { enabled: false },
      },
    });
    expect(results.violations).toEqual([]);
  });
});
