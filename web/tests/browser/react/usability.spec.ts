import { readFile } from "node:fs/promises";
import { join } from "node:path";
import {
  expect,
  test,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
async function fixture(request: APIRequestContext) {
  const key = "usability-" + crypto.randomUUID().slice(0, 8);
  const flow = JSON.parse(
    await readFile(
      join(
        process.env.APPROVAL_TEST_ROOT!,
        "tests/shared/frontend-parity-flow-v1.json",
      ),
      "utf8",
    ),
  );
  const response = await request.post(
    base() + `/api/approval/definitions/${key}?name=操作体验验收`,
    {
      headers: { "X-User-Id": "admin", "Idempotency-Key": crypto.randomUUID() },
      data: flow,
    },
  );
  expect(response.status()).toBe(200);
  return key;
}
async function login(page: Page, user: string) {
  await page.addInitScript(
    (user) => localStorage.setItem("approval-demo-user", user),
    user,
  );
}

test("script formatting cannot replace edits made while the formatting modules load", async ({
  page,
  request,
}) => {
  const key = await fixture(request);
  await login(page, "admin");
  let release!: () => void;
  let entered!: () => void;
  const wait = new Promise<void>((resolve) => (release = resolve));
  const arrived = new Promise<void>((resolve) => (entered = resolve));
  await page.route("**/assets/standalone-*.js", async (route) => {
    entered();
    await wait;
    await route.continue();
  });
  await page.goto("/designer/" + key);
  await page.locator('.react-flow__node[data-id="prep"]').click();
  const editor = page.getByRole("textbox", { name: "JavaScript", exact: true });
  await expect(editor).toBeVisible();
  const button = page.getByRole("button", { name: "格式化代码", exact: true });
  await button.click();
  await arrived;
  await expect(button).toBeDisabled();
  const newCode =
    'function execute(context) { return { preserved: "new edit" }; }';
  await editor.fill(newCode);
  release();
  await expect(
    page.getByText("代码已变化，未应用旧的格式化结果，请重新格式化。", {
      exact: true,
    }),
  ).toBeVisible();
  await expect(editor).toHaveText(newCode);
  await expect(button).toBeEnabled();
  await page.screenshot({
    path: process.env.APPROVAL_SMOKE_OUTPUT + "/format-preserved.png",
    fullPage: true,
    animations: "disabled",
  });
});

test("a pending or failed workbench query stays unknown and an empty recovery reports zero", async ({
  page,
  request,
}) => {
  const key = await fixture(request);
  await login(page, "dave");
  let release!: () => void;
  let entered!: () => void;
  const wait = new Promise<void>((resolve) => (release = resolve));
  const arrived = new Promise<void>((resolve) => (entered = resolve));
  const match = /\/api\/approval\/todo(?:\?.*)?$/;
  await page.route(match, async (route) => {
    entered();
    await wait;
    await route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "模拟查询失败", status: 503 }),
    });
  });
  await page.goto(`/workbench?todo_definitionKey=${key}`);
  await arrived;
  const count = page.locator(".ar-pagination").getByRole("status");
  await expect(count).toHaveText("总数加载中…");
  await expect(page.getByText("暂无待办审批", { exact: true })).toHaveCount(0);
  release();
  await expect(
    page.getByText("列表加载失败，请重试", { exact: true }),
  ).toBeVisible();
  await expect(count).toHaveText("总数未知");
  await expect(
    page.getByRole("button", { name: "下一页", exact: true }),
  ).toBeDisabled();
  await page.screenshot({
    path: process.env.APPROVAL_SMOKE_OUTPUT + "/list-unavailable.png",
    fullPage: true,
    animations: "disabled",
  });
  await page.unroute(match);
  await page.getByRole("button", { name: "刷新列表", exact: true }).click();
  await expect(count).toHaveText("共 0 条");
});

test("form designer keyboard selection reorder copy and delete retain fields and focus", async ({
  page,
  request,
}) => {
  const key = await fixture(request);
  await login(page, "admin");
  await page.goto("/designer/" + key);
  await page.getByRole("tab", { name: /表单/ }).click();
  const first = page.getByRole("group", {
    name: "字段：产品编码（productCode）",
    exact: true,
  });
  await first.focus();
  await first.press("ArrowDown");
  const description = page.getByRole("group", {
    name: "字段：说明（description）",
    exact: true,
  });
  await expect(description).toBeFocused();
  await expect(
    page.getByRole("textbox", { name: "字段标识", exact: true }),
  ).toHaveValue("description");
  await description.press("Alt+ArrowUp");
  await expect(page.locator(".ar-field-card").first()).toHaveAttribute(
    "aria-label",
    "字段：说明（description）",
  );
  await description.press("Alt+ArrowDown");
  await description.press("Control+d");
  const copy = page.getByRole("group", { name: /^字段：说明（副本）/ });
  await expect(copy).toBeFocused();
  await expect(
    page.getByRole("textbox", { name: "字段名称", exact: true }),
  ).toHaveValue("说明（副本）");
  await copy.press("Delete");
  await expect(copy).toHaveCount(0);
  await expect(
    page.getByRole("group", { name: "字段：数量（quantity）", exact: true }),
  ).toBeFocused();
  await expect(page.locator(".ar-field-card")).toHaveCount(17);
  await page.screenshot({
    path: process.env.APPROVAL_SMOKE_OUTPUT + "/keyboard-fields.png",
    fullPage: true,
    animations: "disabled",
  });
});

test("invalid typed dates are rejected before a start request or any approval instance is written", async ({
  page,
  request,
}) => {
  const key = await fixture(request);
  await login(page, "alice");
  const sent: string[] = [];
  await page.route(`**/api/approval/forms/${key}/start`, async (route) => {
    sent.push(route.request().postData() ?? "");
    await route.continue();
  });
  await page.goto("/start/" + key);
  await page
    .getByRole("textbox", { name: "日期", exact: true })
    .fill("2026-02-30");
  await page.getByRole("button", { name: "提交申请", exact: true }).click();
  await expect(
    page.getByText("日期应为日期（yyyy-MM-dd）", { exact: true }),
  ).toBeVisible();
  expect(sent).toEqual([]);
  const instances = await request.get(
    base() + `/api/approval/admin/instances?definitionKey=${key}`,
    { headers: { "X-User-Id": "admin" } },
  );
  expect(instances.status()).toBe(200);
  expect(await instances.json()).toMatchObject({ items: [], total: 0 });
});
