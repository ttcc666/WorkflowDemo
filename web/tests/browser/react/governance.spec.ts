import { chooseOption } from "./ui-controls";
import { expect, test } from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = () => ({
  "X-User-Id": "admin",
  "Idempotency-Key": crypto.randomUUID(),
});
test("React definition governance creates publishes compares selects versions and imports a real export", async ({
  page,
  request,
}, info) => {
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "admin");
  });
  await page.goto("/definitions");
  await page.getByRole("button", { name: "新建流程", exact: true }).click();
  await page
    .getByRole("textbox", { name: "流程名称", exact: true })
    .fill("治理验收");
  await page
    .getByRole("textbox", { name: "流程 Key", exact: true })
    .fill("ui-governance");
  await page.getByRole("button", { name: "确认新建", exact: true }).click();
  await expect(page).toHaveURL(/designer\/ui-governance/);
  await page.getByRole("button", { name: "发布", exact: true }).click();
  await page
    .getByRole("textbox", { name: "发布说明", exact: true })
    .fill("第一版");
  await page.getByRole("button", { name: "确认发布", exact: true }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "新版本已发布" }),
  ).toBeVisible();
  const v1 = await (
    await request.get(base() + "/api/approval/definitions/ui-governance", {
      headers: headers(),
    })
  ).json();
  const flow = JSON.parse(v1.definitionJson);
  flow.nodes.end.name = "第二版结束";
  const published = await request.post(
    base() + "/api/approval/definitions/ui-governance?name=治理验收",
    { headers: headers(), data: flow },
  );
  expect(published.ok()).toBe(true);
  const v2 = await published.json();
  await page.goto("/definitions");
  await page
    .getByRole("row")
    .filter({ hasText: "ui-governance" })
    .getByRole("button", { name: "版本 / 治理" })
    .click();
  const dialog = page.getByRole("dialog", {
    name: "ui-governance · 版本与治理",
    exact: true,
  });
  await dialog.getByRole("tab", { name: "版本对比", exact: true }).click();
  await chooseOption(dialog.getByRole("combobox", { name: "基准版本" }), v1.id);
  await chooseOption(dialog.getByRole("combobox", { name: "目标版本" }), v2.id);
  await expect(
    dialog.getByText("/nodes/end/name", { exact: true }),
  ).toBeVisible();
  await dialog.getByRole("button", { name: "交换版本" }).click();
  await expect(
    dialog.getByRole("combobox", { name: "基准版本" }),
  ).toHaveAttribute("data-value", v2.id);
  await dialog.getByRole("tab", { name: "治理", exact: true }).click();
  await dialog
    .getByRole("textbox", { name: "治理变更说明" })
    .fill("恢复旧版本");
  await chooseOption(
    dialog.getByRole("combobox", { name: "选用历史版本" }),
    v1.id,
  );
  await dialog.getByRole("button", { name: "选用此版本" }).click();
  const refreshedGovernance = page.waitForResponse(response =>
    response.url().endsWith('/definitions/ui-governance/governance') && response.request().method() === 'GET');
  await page
    .getByRole("alertdialog", { name: "确认治理操作", exact: true })
    .getByRole("button", { name: "确认", exact: true })
    .click();
  await expect
    .poll(
      async () =>
        (
          await request
            .get(
              base() + "/api/approval/definitions/ui-governance/governance",
              { headers: headers() },
            )
            .then((response) => response.json())
        ).activeDefinitionId,
    )
    .toBe(v1.id);
  const refreshed = await refreshedGovernance;
  expect(refreshed.ok()).toBe(true);
  expect((await refreshed.json()).activeDefinitionId).toBe(v1.id);
  await expect(page.getByRole("alertdialog", { name: "确认治理操作", exact: true })).toHaveCount(0);
  await dialog.getByRole("button", { name: "停用流程" }).click();
  await page
    .getByRole("alertdialog", { name: "确认治理操作", exact: true })
    .getByRole("button", { name: "确认", exact: true })
    .click();
  await expect(dialog.getByRole("button", { name: "启用流程" })).toBeVisible();
  await expect(page.getByRole("alertdialog", { name: "确认治理操作", exact: true })).toHaveCount(0);
  await dialog.getByRole("button", { name: "启用流程" }).click();
  await page
    .getByRole("alertdialog", { name: "确认治理操作", exact: true })
    .getByRole("button", { name: "确认", exact: true })
    .click();
  await expect(dialog.getByRole("button", { name: "停用流程" })).toBeVisible();
  await expect(page.getByRole("alertdialog", { name: "确认治理操作", exact: true })).toHaveCount(0);
  await dialog.getByRole("tab", { name: "审计", exact: true }).click();
  await expect(
    dialog.getByText("恢复旧版本", { exact: true }).first(),
  ).toBeVisible();
  await dialog.getByRole("tab", { name: "版本", exact: true }).click();
  const pending = page.waitForEvent("download");
  await dialog
    .getByRole("button", { name: "导出", exact: true })
    .first()
    .click();
  const download = await pending;
  const file = info.outputPath("definition-package.json");
  await download.saveAs(file);
  await dialog.getByRole("button", { name: "关闭", exact: true }).click();
  await page.getByRole("button", { name: "导入流程定义", exact: true }).click();
  await page
    .locator('input[type="file"][aria-label="流程定义文件"]')
    .setInputFiles(file);
  await page.getByRole("textbox", { name: "目标流程标识" }).fill("ui-imported");
  await page.getByRole("button", { name: "导入预检查" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "导入预检查通过" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "确认导入" }).click();
  await expect(page).toHaveURL(/designer\/ui-imported/);
  const imported = await (
    await request.get(
      base() + "/api/approval/definitions/ui-imported/governance",
      { headers: headers() },
    )
  ).json();
  expect(JSON.parse(imported.draftJson).nodes.end.name).toBe("第二版结束");
});
