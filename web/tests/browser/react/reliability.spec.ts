import { chooseOption } from "./ui-controls";
import { expect, test } from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = (user = "admin", key?: string) => ({
  "X-User-Id": user,
  ...(key ? { "Idempotency-Key": key } : {}),
});
test("missing keys forbidden editable fields stale revisions and identity changes cannot produce partial writes", async ({
  page,
  request,
}) => {
  const flow = {
    schemaVersion: 1,
    startNodeId: "start",
    settings: { startable: true },
    form: {
      fields: [
        { key: "amount", label: "金额", type: "Money", defaultValue: 200 },
        { key: "secret", label: "隐藏", type: "Text" },
      ],
    },
    nodes: {
      start: { id: "start", name: "开始", type: "Start", next: "review" },
      review: {
        id: "review",
        name: "审批",
        type: "Approval",
        approvers: [{ type: "User", value: "dave" }],
        fieldPermissions: { amount: "Editable", secret: "Hidden" },
        next: "end",
      },
      end: { id: "end", name: "结束", type: "End" },
    },
  };
  expect(
    await request
      .post(base() + "/api/approval/definitions/negative-ui?name=权限验收", {
        headers: headers("admin", crypto.randomUUID()),
        data: flow,
      })
      .then((response) => response.ok()),
  ).toBe(true);
  const started = await request
    .post(base() + "/api/approval/forms/negative-ui/start", {
      headers: headers("alice", crypto.randomUUID()),
      data: { formData: { amount: 200, secret: "private-value" }, comment: "" },
    })
    .then((response) => response.json());
  const current = await request
    .get(base() + `/api/approval/instances/${started.instanceId}/my-task`, {
      headers: headers("dave"),
    })
    .then((response) => response.json());
  const missing = await request.post(
    base() + `/api/approval/tasks/${current.task.id}/approve`,
    { headers: headers("dave"), data: { comment: "" } },
  );
  expect(missing.status()).toBe(400);
  expect((await missing.json()).code).toBe("OperationIdRequired");
  const denied = await request.post(
    base() + `/api/approval/tasks/${current.task.id}/approve`,
    {
      headers: headers("dave", crypto.randomUUID()),
      data: { comment: "", formData: { secret: "tampered" } },
    },
  );
  expect(denied.status()).toBeGreaterThanOrEqual(400);
  expect(denied.status()).toBeLessThan(500);
  const unchanged = await request
    .get(base() + `/api/approval/instances/${started.instanceId}`, {
      headers: headers("alice"),
    })
    .then((response) => response.json());
  expect(unchanged.status).toBe("Running");
  expect(JSON.parse(unchanged.formDataJson)).toMatchObject({
    amount: 200,
    secret: "private-value",
  });
  expect(unchanged.logs.some((log: any) => log.action === "Approve")).toBe(
    false,
  );
  const gov = await request
    .get(base() + "/api/approval/definitions/negative-ui/governance", {
      headers: headers(),
    })
    .then((response) => response.json());
  const conflict = await request.put(
    base() + "/api/approval/definitions/negative-ui/draft",
    {
      headers: headers("admin", crypto.randomUUID()),
      data: {
        name: "stale",
        definitionJson: JSON.stringify(flow),
        sourceDefinitionId: gov.activeDefinitionId,
        expectedRevision: gov.revision - 1,
      },
    },
  );
  expect(conflict.status()).toBe(409);
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "dave");
  });
  await page.goto("/approvals/" + started.instanceId);
  await expect(
    page.getByRole("button", { name: "同意", exact: true }),
  ).toBeVisible();
  await chooseOption(page.getByRole("combobox", { name: "演示用户" }), "bob");
  await expect(
    page.getByText("审批不存在或无权查看", { exact: true }),
  ).toBeVisible();
  expect(
    await page.getByRole("button", { name: "同意", exact: true }).count(),
  ).toBe(0);
  expect(await page.getByText("private-value", { exact: true }).count()).toBe(
    0,
  );
  expect(
    await page.getByRole("link", { name: "流程定义", exact: true }).count(),
  ).toBe(0);
  expect(
    await request
      .get(base() + "/api/approval/definitions", {
        headers: headers("not-a-user"),
      })
      .then((response) => response.status()),
  ).toBe(401);
});
