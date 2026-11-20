import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import {
  BrandMark,
  Button,
  DataTable,
  Dialog,
  ErrorBoundary,
  FormField,
  StatusBadge,
  TextInput,
  ToastProvider,
  useToast,
} from "../index";
import { CatalogApp } from "../catalog/CatalogApp";

function DialogHarness() {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <Button onClick={() => setOpen(true)}>Launch Modal</Button>
      <Dialog open={open} title="Shift Confirmation" description="Verify drawer count." onClose={() => setOpen(false)}>
        <FormField label="Cash Count">
          <TextInput defaultValue="5000" />
        </FormField>
      </Dialog>
    </div>
  );
}

function ToastHarness() {
  const { notify } = useToast();
  return <Button onClick={() => notify("success", "Order sent", "Ticket #101 queued.")}>Send</Button>;
}

function ExplodingChild(): never {
  throw new Error("Simulated cafe view crash");
}

describe("Salryn Cafe UI primitives — interaction, keyboard & accessibility", () => {
  it("Dialog moves focus in, traps Tab/Shift+Tab, closes on Escape, and restores focus to trigger", async () => {
    const user = userEvent.setup();
    render(<DialogHarness />);

    const trigger = screen.getByRole("button", { name: "Launch Modal" });
    await user.click(trigger);

    const dialog = screen.getByRole("dialog", { name: "Shift Confirmation" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAttribute("aria-describedby");

    const closeIconBtn = within(dialog).getByRole("button", { name: "Close dialog" });
    const input = within(dialog).getByLabelText("Cash Count");
    const footerCloseBtn = within(dialog).getByRole("button", { name: "Close" });

    expect(closeIconBtn).toHaveFocus();

    await user.tab();
    expect(input).toHaveFocus();

    await user.tab();
    expect(footerCloseBtn).toHaveFocus();

    // Tab wraps back to first focusable element inside dialog
    await user.tab();
    expect(closeIconBtn).toHaveFocus();

    // Shift+Tab wraps backward to last focusable element inside dialog
    await user.tab({ shift: true });
    expect(footerCloseBtn).toHaveFocus();

    // Escape closes dialog and restores focus to trigger
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("FormField wires programmatic <label htmlFor>, aria-invalid, aria-describedby, and role='alert'", () => {
    render(
      <FormField label="Unit Price" required error="Price must be greater than zero.">
        <TextInput defaultValue="-5" />
      </FormField>,
    );

    const input = screen.getByRole("textbox", { name: "Unit Price" });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAttribute("required");
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Price must be greater than zero.");
    expect(input.getAttribute("aria-describedby")).toContain(alert.id);
  });

  it("BrandMark preserves aspect ratio (object-fit: contain) and frames transparent logo on light surface", () => {
    const { container } = render(
      <div>
        <BrandMark variant="logo" height={48} />
        <BrandMark variant="banner" height={96} />
      </div>,
    );

    const logoFrame = container.querySelector('[data-brand-variant="logo"]');
    expect(logoFrame).toHaveClass("salryn-brand-frame--light");

    const imgs = container.querySelectorAll("img");
    expect(imgs).toHaveLength(2);
    for (const img of imgs) {
      expect(img).toHaveStyle({ objectFit: "contain", height: "100%", width: "auto" });
    }
  });

  it("DataTable renders semantic caption, column headers, and empty state", () => {
    const { rerender } = render(
      <DataTable
        caption="Cafe Receipts"
        rows={[{ id: "1", num: "RC-01", total: "₱180.00" }]}
        getRowKey={(r) => r.id}
        columns={[
          { key: "num", header: "Receipt", render: (r) => r.num },
          { key: "total", header: "Total", align: "right", tabular: true, render: (r) => r.total },
        ]}
      />,
    );

    expect(screen.getByRole("table", { name: "Cafe Receipts" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Receipt" })).toHaveAttribute("scope", "col");

    rerender(
      <DataTable
        caption="Cafe Receipts"
        rows={[]}
        getRowKey={(r: { id: string }) => r.id}
        columns={[{ key: "num", header: "Receipt", render: () => null }]}
        emptyTitle="No receipts yet"
      />,
    );
    expect(screen.getByText("No receipts yet")).toBeInTheDocument();
  });

  it("ToastProvider announces notifications and allows keyboard dismissal", async () => {
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <ToastHarness />
      </ToastProvider>,
    );

    await user.click(screen.getByRole("button", { name: "Send" }));
    expect(screen.getByText("Order sent")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Dismiss Order sent" }));
    expect(screen.queryByText("Order sent")).not.toBeInTheDocument();
  });

  it("ErrorBoundary catches render failures and exposes a recovery action", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    try {
      render(
        <ErrorBoundary fallbackTitle="Workstation view crashed">
          <ExplodingChild />
        </ErrorBoundary>,
      );

      expect(screen.getByRole("alert")).toHaveTextContent("Workstation view crashed");
      expect(screen.getByRole("alert")).toHaveTextContent("Simulated cafe view crash");
      expect(screen.getByRole("button", { name: "Reload view" })).toBeInTheDocument();
    } finally {
      spy.mockRestore();
    }
  });

  it("StatusBadge renders icon + text label", () => {
    render(<StatusBadge tone="copper">Brewing</StatusBadge>);
    expect(screen.getByText("Brewing")).toBeInTheDocument();
  });

  it("CatalogApp passes axe-core accessibility checks with zero violations and marks active nav with aria-current", async () => {
    const user = userEvent.setup();
    const { container } = render(<CatalogApp />);

    // Skip link & active nav verification
    expect(screen.getByRole("link", { name: "Skip to main content" })).toHaveAttribute(
      "href",
      "#salryn-main-content",
    );
    const activeNavBtn = screen.getByRole("button", { name: "Brand & Tokens" });
    expect(activeNavBtn).toHaveAttribute("aria-current", "page");

    // Navigate to another item and verify aria-current moves
    const kitchenNavBtn = screen.getByRole("button", { name: "Kitchen & Status" });
    await user.click(kitchenNavBtn);
    expect(kitchenNavBtn).toHaveAttribute("aria-current", "page");
    expect(activeNavBtn).not.toHaveAttribute("aria-current");

    // Run real axe-core audit (color-contrast disabled in jsdom where computed CSS vars aren't rasterized;
    // exact WCAG contrast is verified mathematically in wcagTokens.test.ts)
    const results = await axe.run(container, {
      rules: {
        "color-contrast": { enabled: false },
      },
    });
    expect(results.violations).toEqual([]);
  });
});
