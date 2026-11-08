import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { expect, test } from "@playwright/test";

const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = (user = "admin") => ({
  "X-User-Id": user,
  "Idempotency-Key": crypto.randomUUID(),
});
test("React and Vue preserve all field node and Action configuration on both editing directions", async ({
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
  const valid = await request.post(
    base() + "/api/approval/definitions/validate",
    { headers: headers(), data: flow },
  );
  expect(valid.ok()).toBe(true);
  expect((await valid.json()).errors).toEqual([]);
  const published = await request.post(
    base() + "/api/approval/definitions/full-parity?name=全部能力验收",
    { headers: headers(), data: flow },
  );
  expect(published.ok()).toBe(true);
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "admin");
  });
  await page.goto("/designer/full-parity");
  await expect(
    page.getByRole("textbox", { name: "流程名称", exact: true }),
  ).toHaveValue("全部能力验收");
  await page.getByRole("tab", { name: /表单/ }).click();
  await expect(page.getByText("17 个字段")).toBeVisible();
  await page.getByRole("button", { name: "实时预览" }).click();
  await expect(page.getByRole("textbox", { name: "产品编码" })).toHaveValue(
    "P001",
  );
  await expect(page.getByRole("button", { name: "上传附件" })).toBeVisible();
  await page.getByRole("button", { name: "返回编辑" }).click();
  await page.getByRole("button", { name: "保存草稿", exact: true }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "设计草稿已保存" }),
  ).toBeVisible();
  const first = await (
    await request.get(
      base() + "/api/approval/definitions/full-parity/governance",
      { headers: headers() },
    )
  ).json();
  const reactFlow = JSON.parse(first.draftJson);
  expect(
    new Set(Object.values(reactFlow.nodes).map((node: any) => node.type)).size,
  ).toBe(8);
  expect(
    new Set(reactFlow.form.fields.map((field: any) => field.type)).size,
  ).toBe(16);
  expect(reactFlow.nodes.prep.actions).toEqual(flow.nodes.prep.actions);
  expect(reactFlow.nodes.delivery.actions).toEqual(flow.nodes.delivery.actions);
  await page.goto(process.env.APPROVAL_VUE_URL + "/designer/full-parity");
  await expect(
    page.locator(".vue-flow__node").filter({ hasText: "计算查询映射" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "保存草稿", exact: true }).click();
  const second = await expect
    .poll(async () => {
      const current = await (
        await request.get(
          base() + "/api/approval/definitions/full-parity/governance",
          { headers: headers() },
        )
      ).json();
      return current.revision;
    })
    .toBe(first.revision + 1);
  void second;
  const after = await (
    await request.get(
      base() + "/api/approval/definitions/full-parity/governance",
      { headers: headers() },
    )
  ).json();
  expect(JSON.parse(after.draftJson)).toEqual(reactFlow);
  await page.goto(process.env.APPROVAL_REACT_URL + "/designer/full-parity");
  await expect(
    page.locator(".react-flow__node").filter({ hasText: "计算查询映射" }),
  ).toBeVisible();
});

test("React publishes every Action and shared backend retries HTTP 503 with one stable execution key", async ({
  page,
  request,
}) => {
  test.setTimeout(90000);
  const flow = JSON.parse(
    await readFile(
      join(
        process.env.APPROVAL_TEST_ROOT!,
        "tests/shared/frontend-parity-flow-v1.json",
      ),
      "utf8",
    ),
  );
  const publish = await request.post(
    base() + "/api/approval/definitions/action-execution?name=动作执行",
    { headers: headers(), data: flow },
  );
  expect(publish.ok()).toBe(true);
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "admin");
  });
  await page.goto("/designer/action-execution");
  await page.locator('.react-flow__node[data-id="prep"]').click();
  await page.getByRole("button", { name: "4. host", exact: true }).click();
  await page
    .getByRole("textbox", { name: "label", exact: true })
    .fill("react-host-created");
  await page.getByRole("button", { name: "发布", exact: true }).click();
  await page
    .getByRole("textbox", { name: "发布说明", exact: true })
    .fill("React 编辑器创建的完整动作流程");
  await page.getByRole("button", { name: "确认发布", exact: true }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "新版本已发布" }),
  ).toBeVisible();
  const created = await (
    await request.get(base() + "/api/approval/definitions/action-execution", {
      headers: headers(),
    })
  ).json();
  expect(created.version).toBe(2);
  expect(
    JSON.parse(created.definitionJson).nodes.prep.actions.find(
      (step: any) => step.type === "HostEcho",
    ).config.label,
  ).toBe("react-host-created");
  const started = await request.post(
    base() + "/api/approval/forms/action-execution/start",
    {
      headers: headers("alice"),
      data: {
        formData: {
          productCode: "P001",
          quantity: 2,
          amount: 200,
          description: "说明",
          date: "2026-11-06",
          period: ["2026-11-06", "2026-11-07"],
          choice: "a",
          choices: ["a", "b"],
          radio: "b",
          accepted: true,
          person: "bob",
          people: ["alice", "bob"],
          items: [
            {
              name: "项目",
              quantity: 1,
              price: 25,
              date: "2026-11-06",
              choice: "a",
            },
          ],
          attachments: [],
          secret: "only-initiator",
        },
        comment: "执行验收",
      },
    },
  );
  expect(started.ok()).toBe(true);
  const { instanceId } = await started.json();
  const dave = await (
    await request.get(
      base() + `/api/approval/instances/${instanceId}/my-task`,
      { headers: headers("dave") },
    )
  ).json();
  const fiona = await (
    await request.get(
      base() + `/api/approval/instances/${instanceId}/my-task`,
      { headers: headers("fiona") },
    )
  ).json();
  expect(dave.task.nodeId).toBe("review");
  expect(fiona.task.nodeId).toBe("finance");
  const hidden = await (
    await request.get(base() + `/api/approval/instances/${instanceId}`, {
      headers: headers("dave"),
    })
  ).json();
  expect(JSON.parse(hidden.formDataJson).secret).toBeUndefined();
  expect(
    await request
      .post(base() + "/api/test/http/fail-next?count=1", { headers: headers() })
      .then((response) => response.status()),
  ).toBe(204);
  expect(
    await request
      .post(base() + `/api/approval/tasks/${dave.task.id}/approve`, {
        headers: headers("dave"),
        data: { comment: "同意", formData: { amount: 201 } },
      })
      .then((response) => response.ok()),
  ).toBe(true);
  expect(
    await request
      .post(base() + `/api/approval/tasks/${fiona.task.id}/approve`, {
        headers: headers("fiona"),
        data: { comment: "同意" },
      })
      .then((response) => response.ok()),
  ).toBe(true);
  try {
    await expect
      .poll(
        async () => {
          const exchanges = await (
            await request.get(base() + "/api/test/http/received", {
              headers: headers(),
            })
          ).json();
          return exchanges
            .filter((exchange: any) => exchange.method === "POST")
            .map((exchange: any) => exchange.status);
        },
        { timeout: 65000 },
      )
      .toEqual([503, 200]);
  } catch (error) {
    console.log(
      "Outbox diagnostic evidence",
      await request
        .get(base() + "/api/approval/diagnostics", { headers: headers() })
        .then((response) => response.json()),
    );
    throw error;
  }
  const exchanges = await (
    await request.get(base() + "/api/test/http/received", {
      headers: headers(),
    })
  ).json();
  const posts = exchanges.filter((exchange: any) => exchange.method === "POST");
  expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey);
  expect(posts[0].body).toBe(posts[1].body);
  expect(JSON.parse(posts[1].body)).toEqual({
    order: { productCode: "P001", quantity: 30, risk: 2 },
    host: "react-host-created",
    read: 42,
  });
  const instance = await (
    await request.get(base() + `/api/approval/instances/${instanceId}`, {
      headers: headers("alice"),
    })
  ).json();
  expect(instance.status).toBe("Approved");
  expect(
    instance.logs
      .filter((log: any) => log.action === "ActionExecuted")
      .every((log: any) => log.details === null),
  ).toBe(true);
  const sensitive = await (
    await request.get(base() + `/api/approval/instances/${instanceId}`, {
      headers: headers(),
    })
  ).json();
  expect(
    sensitive.logs
      .filter((log: any) => log.action === "ActionExecuted")
      .map((log: any) => JSON.parse(log.details).type),
  ).toEqual(
    expect.arrayContaining([
      "JavaScript",
      "Sql",
      "Mapping",
      "HostEcho",
      "Http",
      "Notification",
    ]),
  );
  expect(JSON.parse(instance.formDataJson).total).toBe(427);
  const denied = await request.get(base() + "/api/test/http/received", {
    headers: headers("bob"),
  });
  expect(denied.status()).toBe(403);
});
