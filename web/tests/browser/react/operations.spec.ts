import { chooseOption } from "./ui-controls";
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
async function monitorOperations({
  page,
  request,
}: {
  page: Page;
  request: APIRequestContext;
}) {
  const flow = {
    schemaVersion: 1,
    startNodeId: "start",
    settings: { startable: true },
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
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dave/ })
    .click();
  const to = page.getByRole("group", { name: "接收人员", exact: true });
  await to.getByRole("combobox").fill("dan");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dan/ })
    .click();
  await page.getByRole("textbox", { name: "交接原因" }).fill("交接验收");
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
  await page.getByRole("button", { name: "批量改派", exact: true }).click();
  await page
    .getByRole("alertdialog", { name: "批量交接", exact: true })
    .getByRole("button", { name: "确认", exact: true })
    .click();
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
  await expect(
    page.getByRole("heading", { name: "交接结果", exact: true }),
  ).toBeVisible();
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
