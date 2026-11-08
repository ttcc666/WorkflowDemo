import { chooseOption } from "./ui-controls";
import {
  expect,
  test,
  type APIRequestContext,
  type Page,
} from "@playwright/test";

const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = (user = "admin") => ({
  "X-User-Id": user,
  "Idempotency-Key": crypto.randomUUID(),
});
async function fixture(
  request: APIRequestContext,
  key: string,
  subject: string,
  user = "alice",
  approver = "dave",
) {
  const flow = {
    schemaVersion: 1,
    startNodeId: "start",
    settings: { startable: true, titleTemplate: "{form.subject}" },
    form: {
      fields: [
        { key: "subject", label: "申请事由", type: "Text" },
        { key: "amount", label: "金额", type: "Money", defaultValue: 1280 },
      ],
    },
    nodes: {
      start: { id: "start", name: "提交申请", type: "Start", next: "cc" },
      cc: {
        id: "cc",
        name: "抄送负责人",
        type: "Cc",
        approvers: [{ type: "User", value: "dave" }],
        next: "review",
      },
      review: {
        id: "review",
        name: "部门负责人审批",
        type: "Approval",
        approvers: [{ type: "User", value: approver }],
        timeout: { minutes: 60, action: "Remind", maxReminders: 1 },
        next: "end",
      },
      end: { id: "end", name: "审批完成", type: "End" },
    },
  };
  expect(
    (
      await request.post(
        base() + `/api/approval/definitions/${key}?name=工作台验收`,
        { headers: headers(), data: flow },
      )
    ).status(),
  ).toBe(200);
  const response = await request.post(
    base() + `/api/approval/forms/${key}/start`,
    {
      headers: headers(user),
      data: { formData: { subject, amount: 1280 }, comment: "" },
    },
  );
  expect(response.status(), await response.text()).toBe(200);
  return (await response.json()).instanceId as string;
}
async function open(page: Page, url = "/workbench") {
  await page.addInitScript(() =>
    localStorage.setItem("approval-demo-user", "dave"),
  );
  await page.goto(url);
  await expect(
    page.getByRole("combobox", { name: "演示用户" }),
  ).toHaveAttribute("data-value", "dave");
}
async function metric(page: Page, label: string, value: number) {
  await expect(page.locator(`[aria-label="${label}数量"]`)).toHaveText(
    `${value}项`,
  );
}

test("workbench summaries match personal API totals and shortcuts restore their complete filter scope", async ({
  page,
  request,
}) => {
  await fixture(
    request,
    "workbench-summaries",
    "研发设备采购申请",
    "dave",
    "dan",
  );
  await fixture(request, "workbench-incoming", "跨部门协作申请");
  const total = async (path: string) => {
    const response = await request.get(base() + path, {
      headers: headers("dave"),
    });
    expect(response.status()).toBe(200);
    return response.json();
  };
  const [todos, overdue, started, unread] = await Promise.all([
    total("/api/approval/todo/count"),
    total("/api/approval/todo?overdueOnly=true&pageSize=1"),
    total("/api/approval/started?status=Running&pageSize=1"),
    total("/api/approval/carbon-copies?unreadOnly=true&pageSize=1"),
  ]);
  expect(todos.count).toBeGreaterThan(0);
  expect(started.total).toBeGreaterThan(0);
  expect(unread.total).toBeGreaterThan(0);
  await open(
    page,
    "/workbench?todo_keyword=does-not-match&todo_page=3&done_page=7",
  );
  await metric(page, "待我审批", todos.count);
  await metric(page, "超期待办", overdue.total);
  await metric(page, "申请进行中", started.total);
  await metric(page, "未读抄送", unread.total);
  await page.getByRole("button", { name: "查看超期待办", exact: true }).click();
  await expect(page.getByRole("checkbox", { name: "只看超期" })).toBeChecked();
  const params = new URL(page.url()).searchParams;
  expect(params.get("todo_keyword")).toBeNull();
  expect(params.get("todo_page")).toBeNull();
  expect(params.get("done_page")).toBe("7");
  await page
    .getByRole("button", { name: "查看申请进行中", exact: true })
    .click();
  await expect(
    page.getByRole("combobox", { name: "状态筛选" }),
  ).toHaveAttribute("data-value", "Running");
  await expect(
    page.getByRole("button", { name: "研发设备采购申请", exact: true }),
  ).toBeVisible();
  await page.getByRole("textbox", { name: "关键词" }).fill("不存在的申请");
  await page.getByRole("button", { name: "搜索", exact: true }).click();
  await expect(
    page.getByText("当前筛选条件下没有记录，请调整或重置筛选。"),
  ).toBeVisible();
  await page.getByRole("button", { name: "重置", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "研发设备采购申请", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "查看未读抄送", exact: true }).click();
  await expect(page.getByRole("checkbox", { name: "只看未读" })).toBeChecked();
  expect(new URL(page.url()).searchParams.get("tab")).toBe("cc");
});

test("workbench count failures remain unknown and recover without leaking a previous identity", async ({
  page,
  request,
}) => {
  let fail = true;
  await page.route("**/api/approval/todo/count", async (route) => {
    if (fail && route.request().headers()["x-user-id"] === "dave")
      await route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify({ status: 503, title: "个人待办数量暂不可用" }),
      });
    else await route.continue();
  });
  await open(page);
  await expect(page.getByText("获取失败", { exact: true })).toBeVisible();
  await expect(page.locator('[aria-label="待我审批数量"]')).toHaveText("—项");
  fail = false;
  await page.getByRole("button", { name: "重试", exact: true }).click();
  const dave = await request
    .get(base() + "/api/approval/todo/count", { headers: headers("dave") })
    .then((response) => response.json());
  await metric(page, "待我审批", dave.count);
  await expect(page.getByText("获取失败", { exact: true })).toHaveCount(0);
  await chooseOption(page.getByRole("combobox", { name: "演示用户" }), "bob");
  const bob = await request
    .get(base() + "/api/approval/todo/count", { headers: headers("bob") })
    .then((response) => response.json());
  await metric(page, "待我审批", bob.count);
  await expect(
    page.getByRole("link", { name: "流程定义", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "研发设备采购申请", exact: true }),
  ).toHaveCount(0);
});

test("workbench unread recovery reuses the original operation key and updates the personal summary", async ({
  page,
  request,
}) => {
  const id = await fixture(request, "workbench-read", "研发差旅费用报销");
  const before = await request
    .get(base() + "/api/approval/carbon-copies?unreadOnly=true", {
      headers: headers("dave"),
    })
    .then((response) => response.json());
  const copy = before.items.find(
    (item: { instanceId: string }) => item.instanceId === id,
  );
  expect(copy.readAt).toBeNull();
  const sent: { key: string; body: string | null }[] = [];
  await page.route(
    `**/api/approval/carbon-copies/${copy.id}/read`,
    async (route) => {
      sent.push({
        key: route.request().headers()["idempotency-key"]!,
        body: route.request().postData(),
      });
      if (sent.length === 1) {
        const response = await route.fetch();
        expect(response.status()).toBe(204);
        await route.abort("failed");
      } else await route.continue();
    },
  );
  await open(page, "/workbench?tab=cc");
  await page
    .getByRole("button", { name: "研发差旅费用报销", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "按原内容重试上次操作" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "按原内容重试上次操作" }).click();
  await expect(
    page.getByRole("button", { name: "按原内容重试上次操作" }),
  ).toHaveCount(0);
  await expect(page.getByRole("alert")).toHaveCount(0);
  expect(sent).toHaveLength(2);
  expect(sent[0]).toEqual(sent[1]);
  expect(sent[0]!.key).toMatch(/\S+/);
  await metric(page, "未读抄送", before.total - 1);
  const after = await request
    .get(base() + `/api/approval/instances/${id}/carbon-copies`, {
      headers: headers("dave"),
    })
    .then((response) => response.json());
  expect(
    after.find((item: { id: string }) => item.id === copy.id).readAt,
  ).toMatch(/^\d{4}-/);
});

test("workbench responsive layouts keep long titles task context and detail actions within the viewport", async ({
  page,
  request,
}) => {
  const title = "研发中心十一月设备采购与跨部门协作项目的长标题审批申请";
  const id = await fixture(request, "workbench-mobile", title);
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, "/workbench?todo_definitionKey=workbench-mobile");
  const action = page.getByRole("button", {
    name: `查看审批：${title}`,
    exact: true,
  });
  await expect(action).toBeVisible();
  const bounds = await action.boundingBox();
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(1280);
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(1280);
  await page.screenshot({
    path: process.env.APPROVAL_SMOKE_OUTPUT + "/workbench-desktop.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(
    page.getByRole("button", { name: title, exact: true }),
  ).toBeVisible();
  for (const text of ["部门负责人审批", "待处理", "截止时间", "发起人"])
    await expect(page.getByText(text, { exact: true }).first()).toBeVisible();
  const width = await page.evaluate(() => ({
    content: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }));
  expect(width.content).toBeLessThanOrEqual(width.viewport);
  await page.screenshot({
    path: process.env.APPROVAL_SMOKE_OUTPUT + "/workbench-mobile.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: title, exact: true }).click();
  await expect(page).toHaveURL(new RegExp("/approvals/" + id));
  await expect(
    page.getByRole("heading", { name: title, exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "同意", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "审批轨迹", exact: true }),
  ).toBeVisible();
  await page.screenshot({
    path: process.env.APPROVAL_SMOKE_OUTPUT + "/approval-detail-mobile.png",
    fullPage: true,
  });
});
