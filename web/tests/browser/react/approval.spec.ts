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
const headers = (user = "admin", key = crypto.randomUUID()) => ({
  "X-User-Id": user,
  "Idempotency-Key": key,
});
async function define(request: APIRequestContext, key: string) {
  const template = JSON.parse(
    await readFile(
      join(
        process.env.APPROVAL_TEST_ROOT!,
        "tests/shared/frontend-parity-flow-v1.json",
      ),
      "utf8",
    ),
  );
  const flow = {
    ...template,
    nodes: {
      start: { id: "start", name: "开始", type: "Start", next: "review" },
      review: {
        id: "review",
        name: "审批",
        type: "Approval",
        mode: "Any",
        approvers: [{ type: "User", value: "dave" }],
        fieldPermissions: { amount: "Editable", secret: "Hidden" },
        next: "end",
      },
      end: { id: "end", name: "结束", type: "End" },
    },
  };
  const response = await request.post(
    base() + `/api/approval/definitions/${key}?name=${key}`,
    { headers: headers(), data: flow },
  );
  expect(response.ok()).toBe(true);
  return flow;
}
async function login(page: Page, user: string) {
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await chooseOption(page.getByRole("combobox", { name: "演示用户" }), user);
  await expect(
    page.getByRole("combobox", { name: "演示用户" }),
  ).toHaveAttribute("data-value", user);
}
async function start(request: APIRequestContext, key: string) {
  const response = await request.post(
    base() + `/api/approval/forms/${key}/start`,
    {
      headers: headers("alice"),
      data: {
        formData: {
          productCode: "P001",
          quantity: 2,
          amount: 200,
          secret: "private-initiator-value",
        },
        comment: "",
      },
    },
  );
  expect(response.ok()).toBe(true);
  return (await response.json()).instanceId as string;
}

test("React waits for the editable form definition before allowing approval confirmation", async ({
  page,
  request,
}) => {
  await define(request, "ui-loading");
  const id = await start(request, "ui-loading");
  let release!: () => void;
  let requested!: () => void;
  const blocked = new Promise<void>((resolve) => {
    release = resolve;
  });
  const arrived = new Promise<void>((resolve) => {
    requested = resolve;
  });
  await page.route(
    `**/api/approval/instances/${id}/definition`,
    async (route) => {
      requested();
      await blocked;
      await route.continue();
    },
  );
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "dave");
  });
  await page.goto("/approvals/" + id);
  await arrived;
  await expect(
    page.getByRole("button", { name: "同意", exact: true }),
  ).toBeDisabled();
  release();
  await expect(
    page.getByRole("button", { name: "同意", exact: true }),
  ).toBeEnabled();
  await page.getByRole("button", { name: "同意", exact: true }).click();
  await page.getByRole("button", { name: "确认同意", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  const instance = await request
    .get(base() + `/api/approval/instances/${id}`, {
      headers: headers("alice"),
    })
    .then((response) => response.json());
  expect(instance.status).toBe("Approved");
  expect(
    instance.logs.filter((log: any) => log.action === "Approve"),
  ).toHaveLength(1);
});

test("React rejects with a required reason and records one terminal rejection without altering form values", async ({
  page,
  request,
}) => {
  await define(request, "ui-reject");
  const id = await start(request, "ui-reject");
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "dave");
  });
  await page.goto("/approvals/" + id);
  await page.getByRole("button", { name: "驳回", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "确认驳回", exact: true }),
  ).toBeDisabled();
  await page.getByRole("textbox", { name: "审批意见" }).fill("条件不满足");
  await page.getByRole("button", { name: "确认驳回", exact: true }).click();
  await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + `/api/approval/instances/${id}`, {
              headers: headers("alice"),
            })
            .then((response) => response.json())
        ).status,
    )
    .toBe("Rejected");
  const instance = await request
    .get(base() + `/api/approval/instances/${id}`, {
      headers: headers("alice"),
    })
    .then((response) => response.json());
  expect(
    instance.logs.filter((log: any) => log.action === "Reject"),
  ).toHaveLength(1);
  expect(
    instance.logs.find((log: any) => log.action === "Reject").comment,
  ).toBe("条件不满足");
  expect(JSON.parse(instance.formDataJson).amount).toBe(200);
});

test("React saves and restores a draft submits a form and recovers an approval after the committed response is lost", async ({
  page,
  request,
}) => {
  await define(request, "ui-recovery");
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "alice");
  });
  await page.goto("/start/ui-recovery");
  await page
    .getByRole("textbox", { name: "说明", exact: true })
    .fill("React 草稿内容");
  await page
    .getByRole("textbox", { name: "仅发起人可见", exact: true })
    .fill("private-initiator-value");
  await page.getByRole("button", { name: "保存草稿", exact: true }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "草稿已保存" }),
  ).toBeVisible();
  const url = page.url();
  expect(new URL(url).searchParams.get("draft")).toBeTruthy();
  await page.reload();
  await expect(
    page.getByRole("textbox", { name: "说明", exact: true }),
  ).toHaveValue("React 草稿内容");
  await page.getByRole("button", { name: "提交申请", exact: true }).click();
  await expect(page).toHaveURL(/\/approvals\//);
  const id = new URL(page.url()).pathname.split("/").pop()!;
  await login(page, "dave");
  await expect(
    page.getByRole("spinbutton", { name: "金额", exact: true }),
  ).toBeVisible();
  expect(
    await page.getByText("private-initiator-value", { exact: true }).count(),
  ).toBe(0);
  await page.getByRole("spinbutton", { name: "金额", exact: true }).fill("250");
  const sent: { key: string | undefined; body: string | null }[] = [];
  let lost = false;
  await page.route("**/api/approval/tasks/*/approve", async (route) => {
    sent.push({
      key: route.request().headers()["idempotency-key"],
      body: route.request().postData(),
    });
    if (!lost) {
      lost = true;
      const response = await route.fetch();
      expect(response.ok()).toBe(true);
      await route.abort("failed");
    } else await route.continue();
  });
  await page.getByRole("button", { name: "同意", exact: true }).click();
  await page.getByRole("textbox", { name: "审批意见" }).fill("同意并修改金额");
  await page.getByRole("button", { name: "确认同意", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "按原内容重试上次操作" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "按原内容重试上次操作" }).click();
  await expect(
    page.getByRole("button", { name: "按原内容重试上次操作" }),
  ).toHaveCount(0);
  expect(sent).toHaveLength(2);
  expect(sent[0]).toEqual(sent[1]);
  expect(sent[0]!.key).toBeTruthy();
  const result = await (
    await request.get(base() + `/api/approval/instances/${id}`, {
      headers: headers("alice"),
    })
  ).json();
  expect(result.status).toBe("Approved");
  expect(JSON.parse(result.formDataJson).amount).toBe(250);
  expect(
    result.logs.filter((log: any) => log.action === "Approve"),
  ).toHaveLength(1);
  expect(
    await request
      .get(base() + `/api/approval/instances/${id}`, {
        headers: headers("bob"),
      })
      .then((response) => response.status()),
  ).toBe(404);
});

test("React returns an approval restores the original values and resubmits the edited form", async ({
  page,
  request,
}) => {
  await define(request, "ui-return");
  const id = await start(request, "ui-return");
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "dave");
  });
  await page.goto("/approvals/" + id);
  await page.getByRole("button", { name: "退回", exact: true }).click();
  const targets = page.getByRole("combobox", { name: "退回节点" });
  await targets.click();
  const targetOptions = page.getByRole("listbox").getByRole("option");
  await expect(targetOptions).toHaveCount(2);
  const target = await targetOptions.last().getAttribute("data-option-value");
  await targetOptions.last().click();
  await expect(targets).toHaveAttribute("data-value", target!);
  await page.getByRole("textbox", { name: "审批意见" }).fill("补充申请内容");
  await page.getByRole("button", { name: "确认退回", exact: true }).click();
  await login(page, "alice");
  await page.getByRole("button", { name: "重新提交", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "重新提交" });
  await expect(
    dialog.getByRole("spinbutton", { name: "金额", exact: true }),
  ).toHaveValue("200");
  await dialog
    .getByRole("spinbutton", { name: "金额", exact: true })
    .fill("300");
  await dialog
    .getByRole("button", { name: "确认重新提交", exact: true })
    .click();
  await login(page, "dave");
  await page.getByRole("button", { name: "同意", exact: true }).click();
  await page.getByRole("button", { name: "确认同意", exact: true }).click();
  const result = await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + `/api/approval/instances/${id}`, {
              headers: headers("alice"),
            })
            .then((response) => response.json())
        ).status,
    )
    .toBe("Approved");
  void result;
  const instance = await (
    await request.get(base() + `/api/approval/instances/${id}`, {
      headers: headers("alice"),
    })
  ).json();
  expect(JSON.parse(instance.formDataJson).amount).toBe(300);
  expect(instance.logs.map((log: any) => log.action)).toEqual(
    expect.arrayContaining(["Return", "Resubmit", "Approve"]),
  );
});

test("React add-sign and transfer use real directory users and preserve the original approval sequence", async ({
  page,
  request,
}) => {
  await define(request, "ui-assignment");
  const id = await start(request, "ui-assignment");
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "dave");
  });
  await page.goto("/approvals/" + id);
  await page.getByRole("button", { name: "加签", exact: true }).click();
  await page
    .getByRole("dialog")
    .getByRole("combobox", { name: "搜索人员" })
    .fill("bob");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Bob/ })
    .click();
  await page.getByRole("button", { name: "确认加签", exact: true }).click();
  await login(page, "bob");
  await page.getByRole("button", { name: "同意", exact: true }).click();
  await page.getByRole("button", { name: "确认同意", exact: true }).click();
  await login(page, "dave");
  await page.getByRole("button", { name: "转交", exact: true }).click();
  await page
    .getByRole("dialog")
    .getByRole("combobox", { name: "搜索人员" })
    .fill("dan");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dan/ })
    .click();
  await page.getByRole("button", { name: "确认转交", exact: true }).click();
  await login(page, "dan");
  await page.getByRole("button", { name: "同意", exact: true }).click();
  await page.getByRole("button", { name: "确认同意", exact: true }).click();
  await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + `/api/approval/instances/${id}`, {
              headers: headers("alice"),
            })
            .then((response) => response.json())
        ).status,
    )
    .toBe("Approved");
  const instance = await (
    await request.get(base() + `/api/approval/instances/${id}`, {
      headers: headers("alice"),
    })
  ).json();
  expect(instance.logs.map((log: any) => log.action)).toEqual(
    expect.arrayContaining(["AddSign", "Transfer", "Approve"]),
  );
});

for (const mode of ["After", "Parallel"])
  test(`React ${mode} add-sign keeps the required approval participants`, async ({
    page,
    request,
  }) => {
    const key = "ui-sign-" + mode.toLowerCase();
    await define(request, key);
    const id = await start(request, key);
    await page.addInitScript(() => {
      if (!localStorage.getItem("approval-demo-user"))
        localStorage.setItem("approval-demo-user", "dave");
    });
    await page.goto("/approvals/" + id);
    await page.getByRole("button", { name: "加签", exact: true }).click();
    await chooseOption(page.getByRole("combobox", { name: "加签方式" }), mode);
    await page
      .getByRole("dialog")
      .getByRole("combobox", { name: "搜索人员" })
      .fill("bob");
    await page
      .getByRole("listbox", { name: "人员搜索结果" })
      .getByRole("option", { name: /Bob/ })
      .click();
    await page.getByRole("button", { name: "确认加签", exact: true }).click();
    await login(page, "bob");
    await page.getByRole("button", { name: "同意", exact: true }).click();
    await page.getByRole("button", { name: "确认同意", exact: true }).click();
    if (mode === "Parallel") {
      expect(
        (
          await request
            .get(base() + `/api/approval/instances/${id}`, {
              headers: headers("alice"),
            })
            .then((response) => response.json())
        ).status,
      ).toBe("Running");
      await login(page, "dave");
      await page.getByRole("button", { name: "同意", exact: true }).click();
      await page.getByRole("button", { name: "确认同意", exact: true }).click();
    }
    await expect
      .poll(
        async () =>
          (
            await request
              .get(base() + `/api/approval/instances/${id}`, {
                headers: headers("alice"),
              })
              .then((response) => response.json())
          ).status,
      )
      .toBe("Approved");
    const instance = await request
      .get(base() + `/api/approval/instances/${id}`, {
        headers: headers("alice"),
      })
      .then((response) => response.json());
    expect(
      instance.tasks.some(
        (task: any) =>
          task.kind === `AddSign${mode}` && task.status === "Approved",
      ),
    ).toBe(true);
  });

test("React withdraw terminate and administrator reassign persist their real instance outcomes", async ({
  page,
  request,
}) => {
  await define(request, "ui-management");
  const withdrawn = await start(request, "ui-management");
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "alice");
  });
  await page.goto("/approvals/" + withdrawn);
  await page.getByRole("button", { name: "撤回", exact: true }).click();
  await page.getByRole("button", { name: "确认撤回", exact: true }).click();
  await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + `/api/approval/instances/${withdrawn}`, {
              headers: headers("alice"),
            })
            .then((response) => response.json())
        ).status,
    )
    .toBe("Withdrawn");
  const terminated = await start(request, "ui-management");
  await login(page, "admin");
  await page.goto("/approvals/" + terminated);
  await page.getByRole("button", { name: "终止", exact: true }).click();
  await page.getByRole("textbox", { name: "审批意见" }).fill("管理终止");
  await page.getByRole("button", { name: "确认终止", exact: true }).click();
  await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + `/api/approval/instances/${terminated}`, {
              headers: headers("admin"),
            })
            .then((response) => response.json())
        ).status,
    )
    .toBe("Terminated");
  const reassigned = await start(request, "ui-management");
  await page.goto("/approvals/" + reassigned);
  await page.getByRole("button", { name: "改派 dave", exact: true }).click();
  await page
    .getByRole("dialog")
    .getByRole("combobox", { name: "搜索人员" })
    .fill("dan");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dan/ })
    .click();
  await page.getByRole("textbox", { name: "审批意见" }).fill("管理改派");
  await page.getByRole("button", { name: "确认改派", exact: true }).click();
  await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + `/api/approval/instances/${reassigned}/my-task`, {
              headers: headers("dan"),
            })
            .then((response) => response.json())
        ).task?.assigneeValue,
    )
    .toBe("dan");
});
