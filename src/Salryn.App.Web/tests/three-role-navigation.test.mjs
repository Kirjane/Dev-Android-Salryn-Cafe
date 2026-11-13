import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const testDir = path.dirname(fileURLToPath(import.meta.url));
const webSrcDir = path.resolve(testDir, "../src");

const navigationSource = readFileSync(path.join(webSrcDir, "navigation.ts"), "utf8");
const shellSource = readFileSync(path.join(webSrcDir, "components", "RoleAwareAppShell.tsx"), "utf8");
const permissionsSource = readFileSync(path.join(webSrcDir, "security", "permissions.ts"), "utf8");
const usersPageSource = readFileSync(path.join(webSrcDir, "pages", "UsersPage.tsx"), "utf8");

function parsePrimaryNavItems(source) {
  const match = source.match(/export const navItems:\s*NavItemConfig\[\]\s*=\s*\[([\s\S]*?)\];/);
  assert.ok(match, "Expected navItems array in navigation.ts");
  const items = [];
  const linePattern = /\{\s*id:\s*"([^"]+)",\s*label:\s*"([^"]+)",\s*enabled:\s*true,\s*group:\s*"([^"]+)",\s*domain:\s*"([^"]+)"\s*\}/g;
  for (const entry of match[1].matchAll(linePattern)) {
    const lineStart = match[1].lastIndexOf("\n", entry.index) + 1;
    const prefix = match[1].slice(lineStart, entry.index).trim();
    if (prefix.startsWith("//")) continue;
    items.push({ id: entry[1], label: entry[2], group: entry[3], domain: entry[4] });
  }
  return items;
}

test("NAV-49-01: sidebar navItems are strictly grouped in contiguous Cashier -> Manager -> Admin blocks", () => {
  const items = parsePrimaryNavItems(navigationSource);
  assert.ok(items.length >= 15, "Expected full primary sidebar navigation catalog");

  const groupsEncountered = [];
  for (const item of items) {
    if (groupsEncountered.length === 0 || groupsEncountered[groupsEncountered.length - 1] !== item.group) {
      groupsEncountered.push(item.group);
    }
  }

  assert.deepEqual(
    groupsEncountered,
    ["Cashier", "Manager", "Admin"],
    "Sidebar role groups must be strictly contiguous with no interleaved Cashier item under Manager",
  );

  const salesIndex = items.findIndex((item) => item.id === "sales");
  const customerOrdersIndex = items.findIndex((item) => item.id === "customer-orders");
  assert.ok(salesIndex >= 0 && customerOrdersIndex >= 0, "Expected sales and customer-orders nav items");
  assert.ok(
    salesIndex < customerOrdersIndex,
    "Sales & Receipts (Cashier group) must precede Customer Orders (Manager group)",
  );
});

test("NAV-49-02: RoleAwareAppShell emits group header strictly on contiguous group transitions", () => {
  assert.match(
    shellSource,
    /const firstInGroup = index === 0 \|\| navItems\[index - 1\]\.group !== item\.group;/,
    "RoleAwareAppShell must compute firstInGroup from adjacent item transition",
  );
});

test("ROLES-64-01: frontend permissions and UsersPage enforce the three-role contract (Admin, Manager, Cashier)", () => {
  assert.match(
    permissionsSource,
    /if\s*\(\s*roleId !== RoleIds\.Admin && roleId !== RoleIds\.Manager && roleId !== RoleIds\.Cashier\s*\)\s*\{\s*return false;\s*\}/,
    "canAccessPage must fail closed for any non-product legacy roleId",
  );
  assert.match(
    usersPageSource,
    /\["Cashier",\s*"Manager"\]\.includes\(role\.roleName\)/,
    "UsersPage user creation must restrict selectable roles to active product roles",
  );
  assert.match(
    usersPageSource,
    /An Auditor role is not available in this build\./,
    "UsersPage must retain R3 role access guide disclosure",
  );
});
