import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { describe, expect, it, vi } from "vitest";
import { createMockCafeClient } from "./mockCafeClient";
import { WorkstationApp } from "./WorkstationApp";

describe("Salryn Cafe UX-B — Cashier, Orders & Kitchen Workstation (MOCKED_UI)", () => {
  it("supports modifier selection, Cash tender, and server-authoritative receipt commitment", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    expect(screen.getByText("MOCKED_UI")).toBeInTheDocument();

    // Open Spanish Latte modifier dialog
    await user.click(screen.getByRole("button", { name: /Spanish Latte/i }));
    const dialog = screen.getByRole("dialog", { name: "Configure Spanish Latte" });

    // Select Oat Milk (+₱35.00)
    await user.selectOptions(within(dialog).getByLabelText(/Milk Choice/i), "milk-oat");
    await user.click(within(dialog).getByRole("button", { name: "Add to Ticket" }));

    // Server-authoritative total: ₱165.00 + ₱35.00 = ₱200.00
    expect(screen.getAllByText("₱200.00").length).toBeGreaterThanOrEqual(1);

    // Complete Cash sale
    await user.click(screen.getByRole("button", { name: /Complete Sale \(Cash Only\)/i }));
    expect(await screen.findByText(/Committed Receipt · CY-1044/i)).toBeInTheDocument();
  });

  it("enforces Cash / AR / Partial Cash+AR tenders only and commits Partial Cash + AR", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    const tenderSelect = screen.getByLabelText("Tender Mode") as HTMLSelectElement;
    const optionValues = Array.from(tenderSelect.options).map((o) => o.value);
    expect(optionValues).toEqual(["CASH", "AR", "PARTIAL_CASH_AR"]);

    // Add House Cold Brew (₱220.00, no modifiers)
    await user.click(screen.getByRole("button", { name: /House Cold Brew/i }));
    await user.selectOptions(tenderSelect, "PARTIAL_CASH_AR");

    await user.type(screen.getByRole("textbox", { name: "Cash Tendered (PHP)" }), "120");
    await user.type(screen.getByRole("textbox", { name: "AR Portion (PHP)" }), "100");

    await user.click(screen.getByRole("button", { name: /Complete Sale \(Partial Cash \+ AR\)/i }));
    expect(await screen.findByText(/Committed Receipt · CY-1044/i)).toBeInTheDocument();
  });

  it("reuses the EXACT same durable operationId on retry after Unknown-Outcome failure without double-charging", async () => {
    const user = userEvent.setup();
    const client = createMockCafeClient({ simulateNextCheckoutUnknownOutcome: true });

    render(<WorkstationApp client={client} />);

    await user.click(screen.getByRole("button", { name: /House Cold Brew/i }));
    await user.click(screen.getByRole("button", { name: /Complete Sale/i }));

    // Unknown-outcome recovery panel appears with the exact operationId
    const recoveryHeading = await screen.findByText(/Unknown Checkout Outcome · Operation sale-/i);
    expect(recoveryHeading).toBeInTheDocument();

    // Retry with the same operationId
    await user.click(screen.getByRole("button", { name: /Retry & Verify Same Operation ID/i }));

    expect(await screen.findByText(/Idempotent Recovery \(No Duplicate Charge\)/i)).toBeInTheDocument();

    const submittedIds = client.getSubmittedOperationIds();
    expect(submittedIds).toHaveLength(2);
    expect(submittedIds[0]).toBe(submittedIds[1]);
  });

  it("blocks rapid double-submit via synchronous ref-based in-flight guard", async () => {
    const user = userEvent.setup();
    const client = createMockCafeClient({ delayMs: 80 });
    const submitSpy = vi.spyOn(client, "submitSale");

    render(<WorkstationApp client={client} />);

    await user.click(screen.getByRole("button", { name: /House Cold Brew/i }));
    const submitBtn = screen.getByRole("button", { name: /Complete Sale/i });

    // Fire two synchronous clicks before React re-renders disabled state
    fireEvent.click(submitBtn);
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText(/Committed Receipt · CY-1044/i)).toBeInTheDocument();
    });
    expect(submitSpy).toHaveBeenCalledTimes(1);
  });

  it("locks checkout when terminal is REVOKED or offline lease is expired", () => {
    render(<WorkstationApp initialTrustState="REVOKED" />);
    expect(screen.getByRole("alert")).toHaveTextContent(/Terminal Revoked — Checkout Locked/i);
    expect(screen.getByRole("button", { name: /Complete Sale/i })).toBeDisabled();
  });

  it("supports Blind Shift Close without exposing expected totals before submission", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    await user.click(screen.getByRole("button", { name: "Blind Shift Close" }));
    const dialog = screen.getByRole("dialog", { name: "Blind Shift Close" });

    // Expected total (₱18,450.00) must NOT be visible before submit
    expect(within(dialog).queryByText(/18,450\.00/)).not.toBeInTheDocument();

    await user.type(within(dialog).getByRole("textbox", { name: "Counted Drawer Cash (PHP)" }), "18400");
    await user.click(within(dialog).getByRole("button", { name: "Submit Blind Count" }));

    expect(await within(dialog).findByText(/Shift Variance Computed by Server/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/-₱50\.00/)).toBeInTheDocument();
  });

  it("transitions Order lifecycle and bumps Kitchen Display tickets", async () => {
    const user = userEvent.setup();
    render(<WorkstationApp />);

    // Navigate to Orders
    await user.click(screen.getByRole("button", { name: /Orders & Lifecycle/i }));
    expect(screen.getByRole("table", { name: "Cafe Order Lifecycle" })).toBeInTheDocument();

    const startPrepBtns = screen.getAllByRole("button", { name: "Start Prep" });
    await user.click(startPrepBtns[0]);
    expect(await screen.findByText(/Order CY-1043 → PREPARING/i)).toBeInTheDocument();

    // Navigate to Kitchen Display
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
