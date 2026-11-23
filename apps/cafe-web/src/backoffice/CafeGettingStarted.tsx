import { useEffect, useState } from "react";
import { Alert, Button, Card, CardBody, CardHeader, StatusBadge } from "@salryn/cafe-ui";
import type { BackofficeApi, MenuCatalogDraft } from "./types";

type Step = {
  title: string;
  description: string;
  action: string;
  onClick: () => void;
  state: "done" | "needed" | "review" | "unknown";
};

export function CafeGettingStarted({
  api,
  ingredientCount,
  recipeCount,
  linkedMenuItemCount,
  supplierCount,
  customerCount,
  onNavigate,
  onOpenWorkstation,
}: {
  api: BackofficeApi;
  ingredientCount: number;
  recipeCount: number;
  linkedMenuItemCount: number;
  supplierCount: number;
  customerCount: number;
  onNavigate: (page: string) => void;
  onOpenWorkstation?: () => void;
}) {
  const [menu, setMenu] = useState<MenuCatalogDraft | null>(null);
  const [menuError, setMenuError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    void api.getMenuCatalog().then(
      (value) => { if (active) { setMenu(value); setMenuError(null); setLoading(false); } },
      (error) => { if (active) {
        setMenu(null);
        setMenuError(error instanceof Error ? error.message : "Menu setup status unavailable");
        setLoading(false);
      } },
    );
    return () => { active = false; };
  }, [api]);

  const menuState: Step["state"] = loading || menuError ? "unknown" :
    menu?.items.length && menu.currentPublication ? "done" : "needed";
  const steps: Step[] = [
    {
      title: "1. Create your menu",
      description: "Add coffee, food and merchandise. Set prices and sizes, then publish the menu that cashiers will use.",
      action: "Open Menu Setup",
      onClick: () => onNavigate("recipes"),
      state: menuState,
    },
    {
      title: "2. Add ingredients and recipes",
      description: "Add ingredients and recipes, then link each prepared item to its recipe. Ingredient records alone do not establish recipe coverage.",
      action: "Open Recipes",
      onClick: () => onNavigate("recipes"),
      state: ingredientCount > 0 && recipeCount > 0 && linkedMenuItemCount > 0 ? "review" : "needed",
    },
    {
      title: "3. Prepare your stock",
      description: "Create suppliers and record purchase receipts. Stock movements must be recorded, not edited directly.",
      action: "Open Inventory",
      onClick: () => onNavigate("inventory"),
      state: supplierCount > 0 ? "review" : "needed",
    },
    {
      title: "4. Set up customer credit, if used",
      description: "Add AR/Utang customers before using credit or Partial Cash + AR. Cash sales do not require a customer.",
      action: "Open Customer Accounts",
      onClick: () => onNavigate("cash-ar"),
      state: customerCount > 0 ? "review" : "unknown",
    },
    {
      title: "5. Set up your receipt and kitchen printers",
      description: "Register logical printer stations and choose the exact Windows printer name in Store Settings. Test real hardware separately.",
      action: "Open Printer Settings",
      onClick: () => onNavigate("audit"),
      state: "unknown",
    },
    {
      title: "6. Start the cashier",
      description: "Open your cash shift, add menu items, send the order to preparation, mark it ready, and collect Cash or authorized AR.",
      action: "Go to Cashier",
      onClick: () => onOpenWorkstation ? onOpenWorkstation() : window.location.assign("/cafe/"),
      state: "unknown",
    },
  ];

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <Card>
        <CardHeader title="Getting Started — Operate Your Cafe" subtitle="Use this checklist to prepare Salryn Cafe without editing a database or running developer commands." />
        <CardBody>
          <div style={{ display: "grid", gap: 10 }}>
            <p style={{ margin: 0, lineHeight: 1.6 }}>
              Start with your menu, ingredients and store settings. The cashier uses the
              <strong> published menu</strong>, not unsaved drafts. This page is a guide:
              all changes are written to the real Windows Host. A record count does not prove operational readiness.
            </p>
            {menuError ? <Alert tone="warning" title="Cannot confirm menu setup" description={menuError + ". Open Menu Setup to check the Host."} /> : null}
            <StatusBadge tone="info">{menuState === "done" ? "Published menu detected" : menuState === "needed" ? "Menu needs setup or publication" : "Checking menu publication"}</StatusBadge>
          </div>
        </CardBody>
      </Card>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 340px), 1fr))", gap: 14 }}>
        {steps.map((step) => (
          <Card key={step.title}>
            <CardHeader
              title={step.title}
              subtitle={step.description}
              action={<StatusBadge tone={step.state === "done" ? "success" : step.state === "needed" ? "warning" : step.state === "review" ? "info" : "neutral"}>
                {step.state === "done" ? "Publication detected" : step.state === "needed" ? "Needs attention" : step.state === "review" ? "Records found — verify" : "Check in module"}
              </StatusBadge>}
            />
            <CardBody>
              <Button variant="primary" onClick={step.onClick}>{step.action}</Button>
            </CardBody>
          </Card>
        ))}
      </div>
      <Alert tone="info" title="Before real transactions" description="This is a QA candidate. Complete authorized Host licensing and initial administrator setup first. Financial, printer, device and Android acceptance are separate release gates; avoid live customer data until formally approved." />
    </div>
  );
}
