import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { expect, test } from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = () => ({
  "X-User-Id": "admin",
  "Idempotency-Key": crypto.randomUUID(),
});
test("React previews a JavaScript action with synthetic data and leaves draft and external systems untouched", async ({
  page,
  request,
}) => {
  const flow = JSON.parse(
    await readFile(
      join(
        process.env.APPROVAL_TEST_ROOT!,
        "tests/shared/frontend-parity-flow-v1.json",
      ),
      "utf8",
    ),
  );
  expect(
    await request
      .post(base() + "/api/approval/definitions/preview-ui?name=预演验收", {
        headers: headers(),
        data: flow,
      })
      .then((response) => response.ok()),
  ).toBe(true);
  const before = await request
    .get(base() + "/api/approval/definitions/preview-ui/governance", {
      headers: headers(),
    })
    .then((response) => response.json());
  const exchanges = await request
    .get(base() + "/api/test/http/received", { headers: headers() })
    .then((response) => response.json());
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "admin");
  });
  await page.goto("/designer/preview-ui");
  await page.locator('.react-flow__node[data-id="prep"]').click();
  await page
    .getByRole("button", { name: "预演节点并定位步骤", exact: true })
    .click();
  const dialog = page.getByRole("dialog", {
    name: "单节点 / 步骤预演",
    exact: true,
  });
  await dialog
    .getByRole("textbox", { name: "模拟表单数据 JSON" })
    .fill('{"productCode":"P001","amount":500,"quantity":2}');
  const pending = page.waitForResponse((response) =>
    response.url().endsWith("/api/approval/definitions/simulate"),
  );
  await dialog.getByRole("button", { name: "执行模拟" }).click();
  const simulated = await (await pending).json();
  expect(
    simulated.steps
      .flatMap((step: any) => step.actions ?? [])
      .map((action: any) => action.output),
  ).toContainEqual({ amount: 500, score: 2 });
  await expect(dialog.locator('[data-selected-action="risk"]')).toContainText(
    '"amount": 500',
  );
  await expect(dialog.locator('[data-selected-action="risk"]')).toContainText(
    '"score": 2',
  );
  for (const error of simulated.errors)
    await expect(dialog.getByText(error, { exact: true })).toBeVisible();
  const after = await request
    .get(base() + "/api/approval/definitions/preview-ui/governance", {
      headers: headers(),
    })
    .then((response) => response.json());
  expect(after.revision).toBe(before.revision);
  expect(after.draftJson).toBe(before.draftJson);
  expect(
    await request
      .get(base() + "/api/test/http/received", { headers: headers() })
      .then((response) => response.json()),
  ).toEqual(exchanges);
});
