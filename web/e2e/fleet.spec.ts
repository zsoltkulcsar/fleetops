import { test, expect } from "./fixtures";
import type { Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { components } from "@fleetops/api-client";

type Vehicle = components["schemas"]["VehicleListItem"];
type VehicleDetail = components["schemas"]["VehicleDetail"];
const vehicle = (id: string, plate: string, status: Vehicle["status"] = "active"): Vehicle => ({
  id,
  kind: "truck",
  plate,
  make: "Mercedes",
  model: "Actros 1845",
  year: 2024,
  status,
  ownership: "owned",
  version: 1,
});
const detail = (id = "00000000-0000-0000-0000-000000000001", plate = "KS-DC 1177"): VehicleDetail => ({
  ...vehicle(id, plate),
  vin: "WDB00000000000001",
  grossWeightKg: 40000,
  requiredLicenceClass: "C+E",
  leaseEndDate: null,
  trailerType: null,
  odometerKm: 120000,
  depotId: "11111111-1111-1111-1111-111111111111",
  status: "active",
  serviceIntervalKm: 60000,
  serviceIntervalMonths: 12,
  lastTachoDownloadAt: null,
  plateHistory: [],
  requiredDocuments: [],
});
async function list(
  page: Page,
  items: Vehicle[] = [vehicle("00000000-0000-0000-0000-000000000001", "KS-DC 1177")],
  nextCursor: string | null = null,
) {
  await page.route("**/api/v1/fleet/vehicles?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items, nextCursor }),
    }),
  );
}

test("fleet lists vehicles with plate and status", async ({ page }) => {
  await list(page);
  await page.goto("/fleet");
  await expect(page.getByText("KS-DC 1177")).toBeVisible();
  await expect(page.getByText("Active")).toBeVisible();
});
test("fleet list shows labels for wire values", async ({ page }) => {
  await list(page, [
    {
      ...vehicle("wire-1", "B-TR 1001", "retired"),
      kind: "trailer",
      ownership: "rented_in",
    },
  ]);
  await page.goto("/fleet");
  await expect(page.getByText("Rented in")).toBeVisible();
  await expect(page.getByText("Trailer")).toBeVisible();
  await expect(page.getByText("Retired")).toBeVisible();
});
test("load more appends the next page by cursor", async ({ page }) => {
  let calls = 0;
  await page.route("**/api/v1/fleet/vehicles?*", (route) => {
    calls += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(
        calls === 1
          ? { items: [vehicle("v-1", "KS-DC 1177")], nextCursor: "next" }
          : { items: [vehicle("v-2", "KS-DC 2002")], nextCursor: null },
      ),
    });
  });
  await page.goto("/fleet");
  await page.getByRole("button", { name: "Load more" }).click();
  await expect(page.getByText("KS-DC 2002")).toBeVisible();
});
test("search sends the term to the server and shows the result", async ({ page }) => {
  let search = "";
  await page.route("**/api/v1/fleet/vehicles?*", (route) => {
    search = new URL(route.request().url()).searchParams.get("search") ?? "";
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: search ? [vehicle("v-2", "SEARCH-1")] : [vehicle("v-1", "KS-DC 1177")],
        nextCursor: null,
      }),
    });
  });
  await page.goto("/fleet");
  await page.getByPlaceholder("Search by plate").fill("search");
  await expect(page.getByText("SEARCH-1")).toBeVisible();
  expect(search).toBe("search");
});
test("kind filter sends the kind to the server", async ({ page }) => {
  let kind = "";
  await page.route("**/api/v1/fleet/vehicles?*", (route) => {
    kind = new URL(route.request().url()).searchParams.get("kind") ?? "";
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], nextCursor: null }),
    });
  });
  await page.goto("/fleet");
  await page.getByLabel("Vehicle kind").selectOption("trailer");
  await expect(page.getByText("No vehicles yet.")).toBeVisible();
  expect(kind).toBe("trailer");
});
test("empty fleet offers add vehicle to a dispatcher", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], nextCursor: null }),
    }),
  );
  await page.goto("/fleet");
  await expect(page.getByRole("button", { name: "Add vehicle" }).first()).toBeVisible();
});
test("hr sees no add vehicle button", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ employeeId: "hr", roles: ["hr"] }),
    }),
  );
  await page.route("**/api/v1/fleet/vehicles?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [], nextCursor: null }),
    }),
  );
  await page.goto("/fleet");
  await expect(page.getByRole("button", { name: "Add vehicle" })).toHaveCount(0);
});
test("a failed list shows a banner with try again", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles?*", (route) => route.fulfill({ status: 500 }));
  await page.goto("/fleet");
  await expect(page.getByText("Data could not be loaded. Check the connection and try again.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Try again" })).toBeVisible();
});
test("row opens the record at its own url", async ({ page }) => {
  await list(page);
  await page.goto("/fleet");
  await page.locator("tbody tr").click();
  await expect(page).toHaveURL(/\/fleet\/v-1$/);
});
test("record deep link loads directly", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles/v-1", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(detail()) }),
  );
  await page.goto("/fleet/v-1");
  await expect(page.getByText("KS-DC 1177")).toBeVisible();
  await expect(page.getByRole("tab", { name: "Overview" })).toBeVisible();
});
test("vehicle record shows plate history and required documents", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles/v-wire", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        ...detail("v-wire"),
        plateHistory: ["B-XY 1", "B-XY 2"],
        requiredDocuments: [
          { type: "registration", needsConfirmation: false },
          { type: "insurance", needsConfirmation: true },
        ],
      }),
    }),
  );
  await page.goto("/fleet/v-wire");
  await page.getByRole("tab", { name: "History" }).click();
  await expect(page.getByText("B-XY 1")).toBeVisible();
  await expect(page.getByText("B-XY 2")).toBeVisible();
  await page.getByRole("tab", { name: "Documents" }).click();
  const registration = page.getByRole("listitem").filter({ hasText: "registration" });
  const insurance = page.getByRole("listitem").filter({ hasText: "insurance" });
  await expect(registration).toBeVisible();
  await expect(insurance).toBeVisible();
  await expect(insurance).toContainText("Confirmation required");
  await expect(registration).not.toContainText("Confirmation required");
});
test("unknown vehicle shows a not found panel", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles/unknown", (route) => route.fulfill({ status: 404, body: "{}" }));
  await page.goto("/fleet/unknown");
  await expect(page.getByRole("heading", { name: "Vehicle not found" })).toBeVisible();
});
test("add vehicle creates and shows a success toast", async ({ page }) => {
  await list(page, []);
  await page.goto("/fleet");
  await page.getByRole("button", { name: "Add vehicle" }).first().click();
  await page.getByLabel("Plate").fill("KS-DC 2002");
  await page.getByLabel("Make").fill("MAN");
  await page.getByLabel("Model").fill("TGX");
  await page.getByLabel("Year").fill("2024");
  await page.route("**/api/v1/fleet/vehicles", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(detail("v-2", "KS-DC 2002")),
    }),
  );
  await page.getByRole("button", { name: "Add vehicle", exact: true }).last().click();
  await expect(page.getByTestId("toast")).toContainText("Vehicle added successfully.");
});
test("add trailer sends a body the API accepts", async ({ page }) => {
  await list(page, []);
  await page.goto("/fleet");
  await page.getByRole("button", { name: "Add vehicle" }).first().click();
  await page.getByLabel("Kind").selectOption("trailer");
  await page.getByLabel("Trailer type").selectOption("box");
  await page.getByLabel("Plate").fill("KS-DC 3003");
  await page.getByLabel("Make").fill("SCHMITZ");
  await page.getByLabel("Model").fill("S.KO");
  await page.getByLabel("Year").fill("2024");
  await page.getByLabel("Gross weight (kg)").fill("8000");
  let body: Record<string, unknown> | undefined;
  await page.route("**/api/v1/fleet/vehicles", (route) => {
    body = JSON.parse(route.request().postData() ?? "{}");
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(detail("v-trailer", "KS-DC 3003")),
    });
  });
  await page.getByRole("button", { name: "Add vehicle", exact: true }).last().click();
  await expect.poll(() => body).toBeDefined();
  expect(body?.kind).toBe("trailer");
  expect(body?.trailerType).toBe("box");
  expect(body?.ownership).toBe("owned");
  for (const field of [
    "vin",
    "requiredLicenceClass",
    "leaseEndDate",
    "odometerKm",
    "depotId",
    "serviceIntervalKm",
    "serviceIntervalMonths",
    "lastTachoDownloadAt",
  ]) {
    expect(body?.[field]).toBeNull();
  }
});
test("add vehicle shows field errors from the server", async ({ page }) => {
  await list(page, []);
  await page.goto("/fleet");
  await page.getByRole("button", { name: "Add vehicle" }).first().click();
  await page.route("**/api/v1/fleet/vehicles", (route) =>
    route.fulfill({
      status: 400,
      contentType: "application/json",
      body: JSON.stringify({ errors: { plate: ["Plate is required"] } }),
    }),
  );
  await page.getByRole("button", { name: "Add vehicle", exact: true }).last().click();
  await expect(page.getByText("Plate is required")).toBeVisible();
});
test("add vehicle shows a failure that has no field errors", async ({ page }) => {
  await list(page, []);
  await page.goto("/fleet");
  await page.getByRole("button", { name: "Add vehicle" }).first().click();
  await page.getByLabel("Plate").fill("KS-DC 4004");
  await page.route("**/api/v1/fleet/vehicles", (route) => route.fulfill({ status: 500 }));
  await page.getByRole("button", { name: "Add vehicle", exact: true }).last().click();
  await expect(
    page.getByText("Vehicle could not be saved. Check the connection and try again."),
  ).toBeVisible();
  await expect(page.getByLabel("Plate")).toHaveValue("KS-DC 4004");
});
test("edit requires a reason", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles/v-1", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(detail()) }),
  );
  await page.goto("/fleet/v-1");
  await page.getByRole("button", { name: "Edit" }).click();
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByText("Reason is required when editing a vehicle.")).toBeVisible();
});
test("edit conflict keeps the input and explains", async ({ page }) => {
  await page.route("**/api/v1/fleet/vehicles/v-1", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(detail()) }),
  );
  await page.goto("/fleet/v-1");
  await page.getByRole("button", { name: "Edit" }).click();
  await page.getByLabel("Make").fill("Changed");
  await page.getByLabel("Reason").fill("Correction");
  await page.route("**/api/v1/fleet/vehicles/v-1", (route) =>
    route.fulfill({
      status: 409,
      contentType: "application/json",
      body: JSON.stringify({ code: "version_conflict" }),
    }),
  );
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(
    page.getByText("This vehicle changed elsewhere. Review your input and try again."),
  ).toBeVisible();
  await expect(page.getByLabel("Make")).toHaveValue("Changed");
});
test("slash focuses the fleet search", async ({ page }) => {
  await list(page);
  await page.goto("/fleet");
  await expect(page.getByPlaceholder("Search by plate")).toBeVisible();
  await page.keyboard.press("/");
  await expect(page.getByPlaceholder("Search by plate")).toBeFocused();
});
test("fleet screens have no WCAG violations in both themes", async ({ page }) => {
  await list(page);
  for (const colorScheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme });
    await page.goto("/fleet");
    await expect(page.getByRole("heading", { level: 1, name: "Fleet" })).toBeVisible();
    const result = await new AxeBuilder({
      page: page as unknown as import("playwright-core").Page,
    })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(result.violations, JSON.stringify(result.violations)).toEqual([]);
  }
});
