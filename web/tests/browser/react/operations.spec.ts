import { chooseOption } from "./ui-controls";
import {
  formatDateTime,
  taskStatusLabels,
  type ReassignResult,
  type TodoItem,
} from "@approval/shared";
import {
  expect,
  test,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = (user = "admin") => ({
  "X-User-Id": user,
  "Idempotency-Key": crypto.randomUUID(),
});

test("the Certificate business panel approves without a dynamic form using one real request", async ({
  page,
  request,
}) => {
  const certNo = "PANEL-" + crypto.randomUUID().slice(0, 8);
  const created = await request.post(base() + "/api/certificates", {
    headers: headers("alice"),
    data: {
      certNo, name: "业务面板审批验收", holder: "示例持有人", certType: "Normal",
      issueDate: "2026-01-01", expireDate: "2027-12-31",
    },
  });
  expect(created.status()).toBe(202);
  const { requestId } = await created.json();
  const detail = await request.get(base() + "/api/certificate-requests/" + requestId, {
    headers: headers("alice"),
  });
  expect(detail.ok()).toBe(true);
  const { approval: { id } } = await detail.json();
  const myTask = await request.get(base() + `/api/approval/instances/${id}/my-task`, {
    headers: headers("dave"),
  });
  expect(myTask.ok()).toBe(true);
  const { task } = await myTask.json();
  const approvalPath = `/api/approval/tasks/${task.id}/approve`;
  const writes: { key: string | undefined; body: unknown }[] = [];
  page.on("request", (call) => {
    if (call.method() === "POST" && new URL(call.url()).pathname === approvalPath)
      writes.push({ key: call.headers()["idempotency-key"], body: call.postDataJSON() });
  });
  await page.addInitScript(() => localStorage.setItem("approval-demo-user", "dave"));
  await page.goto(`/approvals/${id}`);
  await expect(page.getByRole("heading", { name: "证书申请内容" })).toBeVisible();
  await expect(page.getByText(certNo, { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "同意", exact: true }).click();
  const response = page.waitForResponse((reply) =>
    new URL(reply.url()).pathname === approvalPath && reply.request().method() === "POST",
  );
  await page.getByRole("button", { name: "确认同意", exact: true }).click();
  expect((await response).status()).toBe(204);
  await expect(page.getByText("操作已完成", { exact: true })).toBeVisible();
  expect(writes).toHaveLength(1);
  expect(writes[0]).toEqual({ key: expect.any(String), body: { comment: "", formData: null } });
  await expect(page.getByRole("button", { name: "同意", exact: true })).toHaveCount(0);
});

async function monitorOperations({
  page,
  request,
}: {
  page: Page;
  request: APIRequestContext;
}) {
  const title = "交接预览 " + crypto.randomUUID().slice(0, 8);
  const flow = {
    schemaVersion: 1,
    startNodeId: "start",
    settings: { startable: true, titleTemplate: title },
    form: {
      fields: [
        {
          key: "name",
          label: "名称",
          type: "Text",
          defaultValue: "交接验收",
        },
      ],
    },
    nodes: {
      start: { id: "start", name: "开始", type: "Start", next: "review" },
      review: {
        id: "review",
        name: "审批",
        type: "Approval",
        approvers: [{ type: "User", value: "dave" }],
        next: "cc",
      },
      cc: {
        id: "cc",
        name: "抄送部门",
        type: "Cc",
        approvers: [{ type: "Role", value: "DeptReviewer" }],
        next: "end",
      },
      end: { id: "end", name: "结束", type: "End" },
    },
  };
  const published = await request.post(
    base() + "/api/approval/definitions/ui-operations?name=交接验收",
    { headers: headers(), data: flow },
  );
  expect(published.ok()).toBe(true);
  async function start() {
    const response = await request.post(
      base() + "/api/approval/forms/ui-operations/start",
      {
        headers: headers("alice"),
        data: { formData: { name: "交接验收" }, comment: "" },
      },
    );
    expect(response.ok()).toBe(true);
    return (await response.json()).instanceId;
  }
  const id = await start();
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "admin");
  });
  await page.goto("/monitor");
  await page.getByRole("tab", { name: "离职交接", exact: true }).click();
  const from = page.getByRole("group", { name: "交出人员", exact: true });
  await from.getByRole("combobox").fill("dave");
  const previewResponse = page.waitForResponse((response) =>
    new URL(response.url()).pathname.endsWith("/admin/users/dave/tasks") &&
    response.request().method() === "GET" && response.ok(),
  );
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dave/ })
    .click();
  const preview = await previewResponse;
  const previewQuery = new URL(preview.url()).searchParams;
  expect(Object.fromEntries(previewQuery)).toEqual({ page: "1", pageSize: "20" });
  const { items } = (await preview.json()) as { items: TodoItem[] };
  const taskPreview = items.find((item) => item.instanceId === id)!;
  expect(taskPreview).toMatchObject({ title, nodeName: "审批", initiatorId: "alice" });
  const row = page.getByRole("row").filter({ hasText: title });
  await expect(row.locator('[data-column="flow"]')).toHaveText(taskPreview.definitionName);
  await expect(row.locator('[data-column="node"]')).toHaveText(taskPreview.nodeName);
  await expect(row.locator('[data-column="status"]')).toHaveText(taskStatusLabels[taskPreview.status].text);
  await expect(row.locator('[data-column="initiator"]')).toHaveText(taskPreview.initiatorId);
  await expect(row.locator('[data-column="time"]')).toHaveText(formatDateTime(taskPreview.createdAt));
  expect(await row.evaluate((element) => element.getBoundingClientRect().right)).toBeLessThanOrEqual(
    page.viewportSize()!.width,
  );
  const reason = page.getByRole("textbox", { name: "交接原因" });
  await expect(reason).toHaveValue("离职交接");
  await reason.fill("交".repeat(201));
  await expect(reason).toHaveValue("交".repeat(200));
  const resized = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname.endsWith("/admin/users/dave/tasks") &&
      url.searchParams.get("page") === "1" &&
      url.searchParams.get("pageSize") === "50" &&
      response.ok();
  });
  await chooseOption(page.getByRole("combobox", { name: "每页条数" }), "50");
  const resizedData = await (await resized).json();
  await expect(page.getByText(`共 ${resizedData.total} 条`, { exact: true })).toBeVisible();
  const to = page.getByRole("group", { name: "接收人员", exact: true });
  await to.getByRole("combobox").fill("dan");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dan/ })
    .click();
  await reason.fill("  交接验收  ");
  const handoverRequests: { key: string | undefined; body: unknown }[] = [];
  page.on("request", (request) => {
    if (
      request.method() === "POST" &&
      /\/admin\/users\/[^/]+\/reassign$/.test(new URL(request.url()).pathname)
    )
      handoverRequests.push({
        key: request.headers()["idempotency-key"],
        body: request.postDataJSON() as unknown,
      });
  });
  // Selecting the receiver as the new sender must clear the conflicting receiver.
  await from.getByRole("combobox").fill("dan");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dan/ })
    .click();
  await expect(to.getByRole("button", { name: "删除 Dan" })).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "批量改派", exact: true }),
  ).toBeDisabled();
  expect(handoverRequests).toHaveLength(0);
  await from.getByRole("combobox").fill("dave");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dave/ })
    .click();
  await to.getByRole("combobox").fill("dan");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dan/ })
    .click();
  const reassignPath = "/api/approval/admin/users/dave/reassign";
  let committedResult: ReassignResult | undefined;
  await page.route("**" + reassignPath, async (route) => {
    const committed = await route.fetch();
    expect(committed.ok()).toBe(true);
    committedResult = await committed.json();
    await route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      json: { title: "交接响应丢失", status: 503 },
    });
  }, { times: 1 });
  const handover = page.getByRole("button", { name: "批量改派", exact: true });
  await handover.click();
  const confirmation = page.getByRole("alertdialog", { name: "批量交接", exact: true });
  await expect(confirmation).toContainText("交出人员：dave");
  await expect(confirmation).toContainText("接收人员：dan");
  await expect(confirmation).toContainText("交接原因：交接验收");
  await confirmation.getByRole("button", { name: "确认", exact: true }).click();
  await expect(page.getByText("交接响应丢失", { exact: true })).toBeVisible();
  await expect(handover).toBeDisabled();
  const assigned = await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + `/api/approval/instances/${id}/my-task`, {
              headers: headers("dan"),
            })
            .then((response) => response.json())
        ).task?.assigneeValue,
    )
    .toBe("dan");
  void assigned;
  expect(handoverRequests).toHaveLength(1);
  expect(handoverRequests[0]!.body).toEqual({
    toUserId: "dan",
    reason: "交接验收",
  });
  expect(handoverRequests[0]!.key).toBeTruthy();
  const replay = page.waitForResponse((response) =>
    new URL(response.url()).pathname === reassignPath &&
    response.request().method() === "POST" && response.ok(),
  );
  await page.getByRole("button", { name: "按原内容重试上次操作", exact: true }).click();
  const replayedResult = (await (await replay).json()) as ReassignResult;
  expect(replayedResult).toEqual(committedResult);
  await expect(
    page.getByRole("heading", { name: "交接结果", exact: true }),
  ).toBeVisible();
  expect(handoverRequests).toEqual([handoverRequests[0], handoverRequests[0]]);
  await expect(
    page.getByRole("listitem").filter({ hasText: "已改派任务" })
      .getByText(String(replayedResult.count), { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("listitem").filter({ hasText: "跳过任务" })
      .getByText(String(replayedResult.skipped.length), { exact: true }),
  ).toBeVisible();
  await expect(page.getByText("交接响应丢失", { exact: true })).toHaveCount(0);
  const task = await (
    await request.get(base() + `/api/approval/instances/${id}/my-task`, {
      headers: headers("dan"),
    })
  ).json();
  expect(
    await request
      .post(base() + `/api/approval/tasks/${task.task.id}/approve`, {
        headers: headers("dan"),
        data: { comment: "同意" },
      })
      .then((response) => response.ok()),
  ).toBe(true);
  const copies = await (
    await request.get(base() + "/api/approval/carbon-copies", {
      headers: headers("dave"),
    })
  ).json();
  const copy = copies.items.find((copy: any) => copy.instanceId === id);
  expect(copy.readAt).toBeNull();
  await chooseOption(page.getByRole("combobox", { name: "演示用户" }), "dave");
  await page.goto("/workbench?tab=cc");
  await expect(
    page.getByRole("combobox", { name: "演示用户" }),
  ).toHaveAttribute("data-value", "dave");
  const copyRow = page.getByRole("row").filter({ hasText: copy.businessKey });
  await expect(copyRow).toHaveCount(1);
  await copyRow
    .getByRole("button", {
      name: copy.title || copy.businessKey,
      exact: true,
    })
    .click();
  await expect(page).toHaveURL(new RegExp("/approvals/" + id));
  const daveCopies = await (
    await request.get(base() + "/api/approval/carbon-copies", {
      headers: headers("dave"),
    })
  ).json();
  const danCopies = await (
    await request.get(base() + "/api/approval/carbon-copies", {
      headers: headers("dan"),
    })
  ).json();
  expect(
    daveCopies.items.find((item: any) => item.id === copy.id).readAt,
  ).not.toBeNull();
  expect(
    danCopies.items.find((item: any) => item.id === copy.id).readAt,
  ).toBeNull();
  await chooseOption(page.getByRole("combobox", { name: "演示用户" }), "admin");
  await page.goto("/diagnostics");
  await expect(
    page.getByRole("heading", { name: "运行诊断", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "风险发现", exact: true }),
  ).toBeVisible();
  await page.goto("/monitor");
  await page.getByRole("tab", { name: "统计", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "统计摘要", exact: true }),
  ).toBeVisible();
}

for (const width of [1280, 390]) {
  test(`React monitor performs real bulk handover and diagnostics and carbon copy reads stay per user at ${width}`, async ({ page, request }) => {
    await page.setViewportSize({ width, height: 900 });
    await monitorOperations({ page, request });
  });
}
