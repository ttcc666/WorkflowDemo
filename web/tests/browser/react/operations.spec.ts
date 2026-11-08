import { chooseOption } from "./ui-controls";
import { expect, test } from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = (user = "admin") => ({
  "X-User-Id": user,
  "Idempotency-Key": crypto.randomUUID(),
});
test("React monitor performs real bulk handover and diagnostics and carbon copy reads stay per user", async ({
  page,
  request,
}) => {
  const flow = {
    schemaVersion: 1,
    startNodeId: "start",
    settings: { startable: true },
    form: {
      fields: [
        { key: "name", label: "名称", type: "Text", defaultValue: "交接验收" },
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
  await page
    .getByRole("button", { name: copy.title || copy.businessKey, exact: true })
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
});
