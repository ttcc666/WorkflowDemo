import { chooseOption } from "./ui-controls";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
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
let seed:
  | Promise<{ key: string; instanceId: string; certificateNo: string }>
  | undefined;
async function dataset(request: APIRequestContext) {
  return (seed ??= (async () => {
    const key = "page-polish-" + crypto.randomUUID().slice(0, 8);
    const fixture = JSON.parse(
      await readFile(
        join(
          process.env.APPROVAL_TEST_ROOT!,
          "tests/shared/frontend-parity-flow-v1.json",
        ),
        "utf8",
      ),
    );
    const flow = {
      ...fixture,
      settings: {
        startable: true,
        category: "页面验收",
        description: "全部字段的页面布局验收",
        titleTemplate: "界面检查 {form.productCode}",
      },
      nodes: {
        start: { id: "start", name: "提交申请", type: "Start", next: "review" },
        review: {
          id: "review",
          name: "负责人审批",
          type: "Approval",
          approvers: [{ type: "User", value: "dave" }],
          fieldPermissions: { amount: "Editable", secret: "Hidden" },
          next: "end",
        },
        end: { id: "end", name: "完成", type: "End" },
      },
    };
    expect(
      (
        await request.post(
          base() + `/api/approval/definitions/${key}?name=页面布局验收`,
          { headers: headers(), data: flow },
        )
      ).status(),
    ).toBe(200);
    const started = await request.post(
      base() + `/api/approval/forms/${key}/start`,
      {
        headers: headers("alice"),
        data: {
          formData: {
            productCode: "UI-LAYOUT",
            quantity: 2,
            amount: 200,
            secret: "private-page-value",
          },
          comment: "",
        },
      },
    );
    expect(started.status()).toBe(200);
    const { instanceId } = await started.json();
    const certificateNo = "UI-POLISH-" + key.slice(-8);
    const created = await request.post(base() + "/api/certificates", {
      headers: headers("alice"),
      data: {
        certNo: certificateNo,
        name: "页面检查注册证",
        holder: "示例持有人",
        certType: "Normal",
        issueDate: "2026-01-01",
        expireDate: "2027-12-31",
      },
    });
    expect(created.status()).toBe(202);
    const { requestId } = await created.json();
    const detail = await request
      .get(base() + "/api/certificate-requests/" + requestId, {
        headers: headers("alice"),
      })
      .then((response) => response.json());
    for (const user of ["dave", "quinn", "leo"]) {
      const task = await request
        .get(base() + `/api/approval/instances/${detail.approval.id}/my-task`, {
          headers: headers(user),
        })
        .then((response) => response.json());
      expect(
        (
          await request.post(
            base() + `/api/approval/tasks/${task.task.id}/approve`,
            {
              headers: headers(user),
              data: { comment: "页面验收", formData: null },
            },
          )
        ).status(),
      ).toBe(204);
    }
    return { key, instanceId: instanceId as string, certificateNo };
  })());
}
async function login(page: Page, user: string) {
  await page.addInitScript(
    (user) => localStorage.setItem("approval-demo-user", user),
    user,
  );
}
const surfaces = [
  { key: "workbench", path: "workbench", title: /^审批工作台$/, user: "dave" },
  { key: "detail", path: "detail", title: /^界面检查/, user: "dave" },
  { key: "templates", path: "start", title: /^发起申请$/, user: "alice" },
  { key: "request", path: "request", title: /^页面布局验收$/, user: "alice" },
  {
    key: "definitions",
    path: "definitions",
    title: /^流程定义$/,
    user: "admin",
  },
  {
    key: "flow-designer",
    path: "designer",
    title: /^流程设计器$/,
    user: "admin",
  },
  {
    key: "form-designer",
    path: "designer",
    title: /^流程设计器$/,
    user: "admin",
    tab: /表单/,
  },
  {
    key: "settings",
    path: "designer",
    title: /^流程设计器$/,
    user: "admin",
    tab: /^发起设置$/,
  },
  { key: "monitor", path: "monitor", title: /^流程监控$/, user: "admin" },
  {
    key: "statistics",
    path: "monitor",
    title: /^流程监控$/,
    user: "admin",
    tab: /^统计$/,
  },
  {
    key: "handover",
    path: "monitor",
    title: /^流程监控$/,
    user: "admin",
    tab: /^离职交接$/,
  },
  { key: "outbox", path: "outbox", title: /^Outbox 投递记录$/, user: "admin" },
  {
    key: "diagnostics",
    path: "diagnostics",
    title: /^运行诊断$/,
    user: "admin",
  },
  {
    key: "certificates",
    path: "certificates",
    title: /^注册证业务$/,
    user: "admin",
  },
];
for (const surface of surfaces)
  for (const width of [1280, 390]) {
    test(`page layout ${surface.key} at ${width} keeps content and actions inside the viewport`, async ({
      page,
      request,
    }) => {
      const data = await dataset(request);
      await login(page, surface.user);
      await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
      const path =
        surface.path === "detail"
          ? "approvals/" + data.instanceId
          : surface.path === "request"
            ? "start/" + data.key
            : surface.path === "designer"
              ? "designer/" + data.key
              : surface.path;
      await page.goto("/" + path);
      await expect(
        page.getByRole("heading", { level: 1, name: surface.title }),
      ).toBeVisible();
      if (surface.path === "designer")
        await expect(
          page.locator('.react-flow__node[data-id="start"]'),
        ).toBeVisible();
      if (surface.tab)
        await page.getByRole("tab", { name: surface.tab }).click();
      if (surface.key === "form-designer") {
        await expect(
          page.getByText("17 个字段", { exact: true }),
        ).toBeVisible();
        for (const field of [
          "单行文本",
          "计算字段",
          "明细表",
          "附件",
          "说明文字",
        ])
          await expect(
            page.getByRole("button", { name: field, exact: true }),
          ).toBeVisible();
        if (width === 390) {
          await page.getByRole("button", { name: "编辑字段属性", exact: true }).click();
          await expect(page.getByRole("dialog", { name: "字段属性", exact: true })).toBeVisible();
        }
        await expect(
          page.getByRole("textbox", { name: "字段标识", exact: true }),
        ).toBeVisible();
        await expect(page.getByRole("textbox", { name: "字段标识", exact: true })).toHaveValue("productCode");
        if (width === 390) await page.getByRole("button", { name: "关闭抽屉", exact: true }).click();
      }
      if (surface.key === "settings")
        await expect(
          page.getByRole("textbox", { name: "标题模板" }),
        ).toBeVisible();
      if (surface.key === "detail") {
        await expect(
          page.getByRole("spinbutton", { name: "金额", exact: true }),
        ).toBeVisible();
        await expect(
          page.getByRole("button", { name: "同意", exact: true }),
        ).toBeEnabled();
        await expect(
          page.getByText("private-page-value", { exact: true }),
        ).toHaveCount(0);
        await expect(
          page.getByRole("navigation", { name: "详情章节" }),
        ).toBeVisible();
      }
      if (surface.key === "templates")
        await expect(
          page.locator(`a.ar-template-entry[href="/start/${data.key}"]`),
        ).toBeVisible();
      if (surface.key === "definitions")
        await expect(page.getByText(data.key, { exact: true })).toBeVisible();
      if (surface.key === "statistics")
        await expect(
          page.getByRole("heading", { name: "节点处理统计", exact: true }),
        ).toBeVisible();
      if (surface.key === "handover")
        await expect(
          page.getByRole("button", { name: "批量改派", exact: true }),
        ).toBeDisabled();
      if (surface.key === "certificates")
        await expect(
          page.getByText(data.certificateNo, { exact: true }),
        ).toBeVisible();
      await expect(
        page.locator('.ar-data-table[aria-busy="true"]'),
      ).toHaveCount(0);
      await expect(page.locator("details, datalist")).toHaveCount(0);
      await expect(page.locator('input[type="date"]')).toHaveCount(0);
      await expect(
        page.locator('select:not([aria-hidden="true"]):visible'),
      ).toHaveCount(0);
      const bounds = await page.evaluate(() => ({
        content: document.documentElement.scrollWidth,
        viewport: window.innerWidth,
      }));
      expect(bounds.content).toBeLessThanOrEqual(bounds.viewport);
      const primary =
        surface.path === "designer"
          ? page.getByRole("button", { name: "发布", exact: true })
          : surface.key === "request"
            ? page.getByRole("button", { name: "提交申请", exact: true })
            : undefined;
      if (primary) {
        const box = await primary.boundingBox();
        expect(box!.x).toBeGreaterThanOrEqual(0);
        expect(box!.x + box!.width).toBeLessThanOrEqual(width);
      }
      await page.screenshot({
        path:
          process.env.APPROVAL_SMOKE_OUTPUT +
          `/page-${surface.key}-${width}.png`,
        fullPage: true,
      });
    });
  }

test("application templates combine category and keyword filters and keep draft resume reachable", async ({
  page,
  request,
}) => {
  const data = await dataset(request);
  const draft = await request
    .put(base() + `/api/approval/forms/${data.key}/draft`, {
      headers: headers("alice"),
      data: {
        draftId: null,
        formData: { productCode: "DRAFT-UI" },
        comment: "待继续的草稿",
      },
    })
    .then((response) => response.json());
  await login(page, "alice");
  await page.goto("/start");
  await chooseOption(
    page.getByRole("combobox", { name: "申请分类", exact: true }),
    "页面验收",
  );
  await page
    .getByRole("textbox", { name: "搜索流程", exact: true })
    .fill("全部字段");
  await expect(page.locator(`a.ar-template-entry[href="/start/${data.key}"]`)).toBeVisible();
  await expect(page.locator('a[href="/start/expense"]')).toHaveCount(0);
  await page
    .getByRole("textbox", { name: "搜索流程", exact: true })
    .fill("没有这个流程");
  await expect(
    page.getByText("没有匹配的申请，请调整搜索或分类", { exact: true }),
  ).toBeVisible();
  await page.getByRole("tab", { name: /^我的草稿/ }).click();
  await expect(
    page.locator(`a[href="/start/${data.key}?draft=${draft.draftId}"]`),
  ).toBeVisible();
  await page
    .locator(`a[href="/start/${data.key}?draft=${draft.draftId}"]`)
    .click();
  await expect(
    page.getByRole("textbox", { name: "产品编码", exact: true }),
  ).toHaveValue("DRAFT-UI");
});

test("definition keyword search matches names and keys without discarding the server category filter", async ({
  page,
  request,
}) => {
  const data = await dataset(request);
  await login(page, "admin");
  await page.goto("/definitions");
  await expect(page.getByText(data.key, { exact: true })).toBeVisible();
  await chooseOption(
    page.getByRole("combobox", { name: "分类筛选", exact: true }),
    "页面验收",
  );
  await page
    .getByRole("textbox", { name: "流程关键词", exact: true })
    .fill("页面布局");
  await expect(page.getByText(data.key, { exact: true })).toBeVisible();
  await page
    .getByRole("textbox", { name: "流程关键词", exact: true })
    .fill(data.key);
  await expect(page.getByText(data.key, { exact: true })).toBeVisible();
  await page
    .getByRole("textbox", { name: "流程关键词", exact: true })
    .fill("unknown-key");
  await expect(
    page.getByText("没有匹配的流程，请调整名称或 Key", { exact: true }),
  ).toBeVisible();
  await page
    .getByRole("textbox", { name: "流程关键词", exact: true })
    .fill("expense");
  await expect(
    page.getByText("没有匹配的流程，请调整名称或 Key", { exact: true }),
  ).toBeVisible();
  await chooseOption(
    page.getByRole("combobox", { name: "分类筛选", exact: true }),
    "",
  );
  await expect(page.getByText("expense", { exact: true })).toBeVisible();
});

test("statistics renders authoritative percentage units and keeps a missing approval rate unknown", async ({
  page,
  request,
}) => {
  await dataset(request);
  const actual = await request
    .get(
      base() +
        "/api/approval/admin/statistics?definitionKey=certificate.create",
      { headers: headers() },
    )
    .then((response) => response.json());
  expect(actual.approvalRate).toBe(100);
  await login(page, "admin");
  await page.goto("/monitor");
  await page.getByRole("tab", { name: "统计", exact: true }).click();
  await page
    .getByRole("textbox", { name: "统计流程 Key", exact: true })
    .fill("certificate.create");
  await page.getByRole("button", { name: "刷新统计", exact: true }).click();
  await expect(page.getByText(/^通过率/)).toHaveText("通过率 100.0%");
  await page.route("**/api/approval/admin/statistics*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ...actual, approvalRate: null }),
    }),
  );
  await page.getByRole("button", { name: "刷新统计", exact: true }).click();
  await expect(page.getByText(/^通过率/)).toHaveText("通过率 —");
});

test("the scoped theme renders rounded small controls and filter surfaces", async ({
  page,
  request,
}) => {
  const data = await dataset(request);
  await login(page, "admin");
  await page.goto("/definitions");
  await expect(page.getByText(data.key, { exact: true })).toBeVisible();
  const radii = await page.evaluate(() =>
    [
      ".ar-page-filter-toolbar",
      ".ar-data-table",
      '.ar-data-table button[data-size="sm"]',
    ].map((selector) => {
      const element = document.querySelector(selector);
      if (!element) throw new Error("Missing theme surface: " + selector);
      return parseFloat(getComputedStyle(element).borderRadius);
    }),
  );
  expect(radii).toHaveLength(3);
  for (const radius of radii) expect(radius).toBeGreaterThan(0);
});

for (const width of [1280, 390]) {
  test(`outbox payload presents readable JSON and preserves original or invalid content at ${width}`, async ({
    page,
    request,
  }) => {
    await dataset(request);
    await login(page, "admin");
    await page.setViewportSize({ width, height: 900 });
    let raw =
      '{"message":"\\u5ba1\\u6279\\u63d0\\u9192","meta":{"enabled":false,"reason":null},"tags":["研发"],"value":0}';
    const original = raw;
    await page.route("**/api/approval/outbox/*/payload", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ payloadJson: raw }),
      }),
    );
    await page.goto("/outbox");
    const open = page
      .getByRole("button", { name: "查看 Payload", exact: true })
      .first();
    await open.click();
    const dialog = page.getByRole("dialog", {
      name: "投递 Payload",
      exact: true,
    });
    const content = dialog.locator('pre[aria-label="投递内容"]');
    await expect(content).toContainText("审批提醒");
    const formatted = await content.textContent();
    expect(formatted).toContain("\n");
    expect(formatted).not.toContain("\\u5ba1");
    expect(JSON.parse(formatted!)).toEqual({
      message: "审批提醒",
      meta: { enabled: false, reason: null },
      tags: ["研发"],
      value: 0,
    });
    await dialog.getByText("原始内容", { exact: true }).click();
    expect(
      await dialog.locator('pre[aria-label="原始投递内容"]').textContent(),
    ).toBe(original);
    const box = await dialog.boundingBox();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(width);
    await dialog.getByRole("button", { name: "关闭", exact: true }).click();
    raw = "未完成的投递内容\n{not-json";
    await open.click();
    await expect(content).toContainText("未完成的投递内容");
    expect(await content.textContent()).toBe(raw);
    await dialog.getByText("原始内容", { exact: true }).click();
    expect(
      await dialog.locator('pre[aria-label="原始投递内容"]').textContent(),
    ).toBe(raw);
    await dialog.getByRole("button", { name: "关闭", exact: true }).click();
    await expect(dialog).toHaveCount(0);
  });
}
