import { chooseOption } from "./ui-controls";
import { expect, test } from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
test("React certificate Demo creates a real request completes three approval stages and shows certificate history", async ({
  page,
  request,
}) => {
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "alice");
  });
  await page.goto("/certificates");
  await page
    .getByRole("button", { name: "申请登记注册证", exact: true })
    .click();
  await page
    .getByRole("textbox", { name: "注册证编号" })
    .fill("REACT-CERT-001");
  await page
    .getByRole("textbox", { name: "注册证名称" })
    .fill("React 注册证验收");
  await page.getByRole("textbox", { name: "持有人" }).fill("测试持有人");
  await page.locator('input[aria-label="发证日期"]').fill("2026-01-01");
  await page.locator('input[aria-label="到期日期"]').fill("2027-12-31");
  await page.getByRole("button", { name: "提交审核", exact: true }).click();
  await page.getByRole("link", { name: "查看审批详情", exact: true }).click();
  await expect(page).toHaveURL(/\/approvals\//);
  const approvalRequests: string[] = [];
  page.on("request", (request) => {
    if (request.method() === "POST" && /\/tasks\/[^/]+\/approve$/.test(new URL(request.url()).pathname))
      approvalRequests.push(request.url());
  });
  for (const [index, user] of ["dave", "quinn", "leo"].entries()) {
    await chooseOption(page.getByRole("combobox", { name: "演示用户" }), user);
    await page.getByRole("button", { name: "同意", exact: true }).click();
    await page.getByRole("textbox", { name: "审批意见" }).fill("同意");
    const submitted = page.waitForResponse((response) =>
      response.request().method() === "POST" && /\/tasks\/[^/]+\/approve$/.test(new URL(response.url()).pathname),
    );
    await page.getByRole("button", { name: "确认同意", exact: true }).click();
    const response = await submitted;
    expect(response.status()).toBe(204);
    expect(approvalRequests).toHaveLength(index + 1);
    await expect(
      page.getByRole("dialog", { name: "同意", exact: true }),
    ).toHaveCount(0);
  }
  await expect
    .poll(async () => {
      const list = await (
        await request.get(base() + "/api/certificates?keyword=REACT-CERT-001", {
          headers: { "X-User-Id": "alice" },
        })
      ).json();
      return list.items[0]?.status;
    })
    .toBe("Active");
  await chooseOption(page.getByRole("combobox", { name: "演示用户" }), "alice");
  await page.goto("/certificates");
  await page
    .getByRole("textbox", { name: "证书关键词" })
    .fill("REACT-CERT-001");
  await page.getByRole("button", { name: "搜索证书" }).click();
  await page
    .getByRole("row")
    .filter({ hasText: "REACT-CERT-001" })
    .getByRole("button", { name: "详情 / 历史" })
    .click();
  await expect(
    page.getByRole("button", { name: "查看申请", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "查看申请", exact: true }).click();
  await expect(
    page.getByText("React 注册证验收", { exact: true }).last(),
  ).toBeVisible();
});
