import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { describe, expect, it } from "vitest";
import { BackofficeConsoleApp } from "./BackofficeConsoleApp";
import { createMockBackofficeClient } from "./mockBackofficeClient";

describe("Salryn Cafe UX-C — Back-Office, Owner & Device Management (MOCKED_UI)", () => {
  it("renders Owner Reports & COGS in Central Owner View and gates Host-Only controls", async () => {
    const user = userEvent.setup();
    render(<BackofficeConsoleApp initialSurface="CENTRAL_OWNER" />);

    expect(screen.getByText("MOCKED_UI")).toBeInTheDocument();
    expect(screen.getByText("₱412,850.00")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Recipe COGS & Margin" })).toBeInTheDocument();

    // Navigate to Host Backup & License in Central Owner View -> MUST be gated
    await user.click(screen.getByRole("button", { name: "Host Backup & License" }));
    expect(screen.getByRole("alert")).toHaveTextContent(/Host-Only Control — Unavailable in Central Owner View/i);

    // Switch to Windows Host View -> Host controls unlock
    await user.click(screen.getByRole("button", { name: "Windows Host View" }));
    expect(screen.getByRole("button", { name: /Create & Verify Local Backup/i })).toBeInTheDocument();
  });

  it("issues 10-min Salryn.Cafe join code and approves pending Android device", async () => {
    const user = userEvent.setup();
    render(<BackofficeConsoleApp />);

    await user.click(screen.getByRole("button", { name: /Device Trust & Health/i }));
    await user.click(screen.getByRole("button", { name: /Issue 10-Min Join Code/i }));
    expect(await screen.findByText(/Active Join Code: CAFE-7492-X9/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Approve" }));
    expect(await screen.findByText(/Device approved · Coffee Cart Tablet #2/i)).toBeInTheDocument();
  });

  it("reuses the EXACT same durable operationId on Device Revocation Unknown-Outcome recovery", async () => {
    const user = userEvent.setup();
    const client = createMockBackofficeClient({ simulateNextRevokeUnknownOutcome: true });
    render(<BackofficeConsoleApp client={client} />);

    await user.click(screen.getByRole("button", { name: /Device Trust & Health/i }));

    // Trigger Revoke on Counter Tablet #1
    const revokeBtns = screen.getAllByRole("button", { name: "Revoke" });
    await user.click(revokeBtns[0]);

    const dialog = screen.getByRole("dialog", { name: /Revoke Counter Tablet #1/i });
    await user.click(within(dialog).getByRole("button", { name: "Revoke device" }));

    // Unknown-outcome recovery panel appears
    expect(await screen.findByText(/Unknown Revocation Outcome · Operation dev-revoke-/i)).toBeInTheDocument();

    // Retry with same operationId
    await user.click(screen.getByRole("button", { name: /Retry & Confirm Revocation/i }));
    expect(await screen.findByText(/Device revoked · Counter Tablet #1/i)).toBeInTheDocument();

    const opIds = client.getSubmittedOpIds();
    expect(opIds).toHaveLength(2);
    expect(opIds[0]).toBe(opIds[1]);
  });

  it("posts inventory purchase/waste movements and Cash AR settlements", async () => {
    const user = userEvent.setup();
    render(<BackofficeConsoleApp />);

    // Inventory purchase (+2 kg on 8.5 kg -> 10.5 kg)
    await user.click(screen.getByRole("button", { name: "Inventory & Waste" }));
    await user.click(screen.getByRole("button", { name: "Post Stock Movement" }));
    expect(await screen.findByText(/New on-hand: 10.5 kg/i)).toBeInTheDocument();

    // Cash AR settlement (₱2,180.00 - ₱500.00 -> ₱1,680.00)
    await user.click(screen.getByRole("button", { name: "Cash, Expense & AR" }));
    await user.click(screen.getByRole("button", { name: "Post Cash Settlement" }));
    expect(await screen.findByText(/Remaining: ₱1,680\.00/i)).toBeInTheDocument();
  });

  it("passes axe-core accessibility audit with zero violations", async () => {
    const { container } = render(<BackofficeConsoleApp />);
    const results = await axe.run(container, {
      rules: {
        "color-contrast": { enabled: false },
      },
    });
    expect(results.violations).toEqual([]);
  });
});
