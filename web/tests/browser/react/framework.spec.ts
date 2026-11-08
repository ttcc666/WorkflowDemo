import { readFile } from "node:fs/promises";
import { join } from "node:path";
import {
  expect,
  test,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
import { chooseOption } from "./ui-controls";
const base = () => process.env.APPROVAL_SMOKE_API!;
async function publish(request: APIRequestContext, full = false) {
  const key = "framework-ui-" + crypto.randomUUID().slice(0, 8);
  const flow = full
    ? JSON.parse(
        await readFile(
          join(
            process.env.APPROVAL_TEST_ROOT!,
            "tests/shared/frontend-parity-flow-v1.json",
          ),
          "utf8",
        ),
      )
    : {
        schemaVersion: 1,
        startNodeId: "start",
        settings: { startable: true },
        form: {
          fields: [
            { key: "date", label: "申请日期", type: "Date" },
            {
              key: "kind",
              label: "申请类型",
              type: "Select",
              options: [
                { value: "a", label: "普通" },
                { value: "b", label: "特殊" },
              ],
            },
          ],
        },
        nodes: {
          start: { id: "start", name: "提交", type: "Start", next: "review" },
          review: {
            id: "review",
            name: "审批",
            type: "Approval",
            approvers: [{ type: "Role", value: "DeptReviewer" }],
            next: "end",
          },
          end: { id: "end", name: "结束", type: "End" },
        },
      };
  const response = await request.post(
    base() + `/api/approval/definitions/${key}?name=框架UI验收`,
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

for (const width of [1280, 390])
  test(`framework select and calendar preserve input values and popup bounds at ${width}`, async ({
    page,
    request,
  }) => {
    const key = await publish(request);
    await login(page, "alice");
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/start/" + key);
    const choice = page.getByRole("combobox", {
      name: "申请类型",
      exact: true,
    });
    await expect(choice).toHaveAttribute("data-slot", "select-trigger");
    await choice.click();
    await page.screenshot({path: process.env.APPROVAL_SMOKE_OUTPUT + `/framework-select-${width}.png`, fullPage: true, animations: 'disabled'});
    await page.getByRole('listbox').press('Escape');
    await chooseOption(choice, "b");
    await expect(choice).toContainText("特殊");
    const date = page.getByRole("textbox", { name: "申请日期", exact: true });
    await date.fill("2026-11-06");
    await page
      .getByRole("button", { name: "选择申请日期", exact: true })
      .click();
    const calendar = page.getByRole("dialog", {
      name: "申请日期日历",
      exact: true,
    });
    await expect(calendar).toBeVisible();
    await expect(
      calendar.getByRole("button", { name: "2026-11-07", exact: true }),
    ).toBeEnabled();
    const box = await calendar.boundingBox();
    expect(box!.x).toBeGreaterThanOrEqual(8);
    expect(box!.x + box!.width).toBeLessThanOrEqual(width - 8);
    await page.screenshot({
      path:
        process.env.APPROVAL_SMOKE_OUTPUT + `/framework-calendar-${width}.png`,
      fullPage: true,
      animations: 'disabled',
    });
    await calendar
      .getByRole("button", { name: "2026-11-07", exact: true })
      .click();
    await expect(date).toHaveValue("2026-11-07");
    await expect(calendar).toHaveCount(0);
    await page
      .getByRole("button", { name: "选择申请日期", exact: true })
      .click();
    await page.getByRole("button", { name: "清空日期", exact: true }).click();
    await expect(date).toHaveValue("");
    await expect(
      page.locator('input[type="date"], details, datalist'),
    ).toHaveCount(0);
  });

test("designer Accordion hides and restores an invalid mapping buffer through keyboard interaction", async ({
  page,
  request,
}) => {
  const key = await publish(request, true);
  await login(page, "admin");
  await page.goto("/designer/" + key);
  await page.locator('.react-flow__node[data-id="prep"]').click();
  await page.getByRole("button", { name: "3. map", exact: true }).click();
  const trigger = page
    .getByRole("button", { name: "默认值与转换选项", exact: true })
    .first();
  await trigger.click();
  const input = page
    .getByRole("textbox", { name: "默认值 JSON", exact: true })
    .first();
  await input.fill("not-json");
  await trigger.click();
  await expect(input).toBeHidden();
  await trigger.press("Enter");
  await expect(input).toBeVisible();
  await expect(input).toHaveValue("not-json");
  await expect(
    page.getByText("请输入合法的 JSON 值", { exact: true }),
  ).toBeVisible();
  await expect(page.locator("details")).toHaveCount(0);
});

test("designer role suggestions use a framework search popup and retain custom host roles", async ({
  page,
  request,
}) => {
  const key = await publish(request);
  await login(page, "admin");
  await page.goto("/designer/" + key);
  await page.locator('.react-flow__node[data-id="review"]').click();
  await page.getByRole("button", { name: "角色选项", exact: true }).click();
  await page
    .getByRole("combobox", { name: "角色搜索", exact: true })
    .fill("Finance");
  await page.getByRole("option", { name: "Finance", exact: true }).click();
  const input = page.getByRole("textbox", { name: "角色", exact: true });
  await expect(input).toHaveValue("Finance");
  await input.fill("HostCustomRole");
  await expect(input).toHaveValue("HostCustomRole");
  await expect(page.locator("datalist")).toHaveCount(0);
});
