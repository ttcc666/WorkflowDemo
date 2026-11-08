import { chooseOption } from "./ui-controls";
import { expect, test, type Locator, type Page } from "@playwright/test";

async function connect(page: Page, source: Locator, target: Locator) {
  await target.hover();
  await source.hover();
  const from = await source.boundingBox();
  const to = await target.boundingBox();
  if (!from || !to) throw new Error("Connection handles are missing");
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
  await page.mouse.down();
  await page.mouse.move(
    from.x + from.width / 2 + 3,
    from.y + from.height / 2 + 3,
  );
  await expect(source).toHaveClass(/connectingfrom/);
  await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, {
    steps: 12,
  });
  await expect(target).toHaveClass(/connectingto/);
  await page.mouse.up();
}
test("React canvas connects approval nodes saves a real draft and Vue can edit the same definition", async ({
  page,
  request,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "admin");
  });
  await page.goto("/designer/react-canvas-parity?name=React画布验收");
  await expect(
    page.getByRole("button", { name: "添加审批", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "添加审批", exact: true }).click();
  const approval = page
    .locator(".react-flow__node")
    .filter({ hasText: "或签" });
  await expect(approval).toHaveCount(1);
  await connect(
    page,
    page.locator('[data-id="start"] .react-flow__handle.source'),
    approval.locator(".react-flow__handle.target"),
  );
  const reviewId = await approval.getAttribute("data-id");
  await expect(
    page.getByRole("group", {
      name: `Edge from start to ${reviewId}`,
      exact: true,
    }),
  ).toHaveCount(1);
  await connect(
    page,
    approval.locator(".react-flow__handle.source"),
    page.locator('[data-id="end"] .react-flow__handle.target'),
  );
  await expect(
    page.getByRole("group", {
      name: `Edge from ${reviewId} to end`,
      exact: true,
    }),
  ).toHaveCount(1);
  await approval.click();
  await chooseOption(
    page.getByRole("combobox", { name: "规则 1类型" }),
    "User",
  );
  await page.getByRole("combobox", { name: "搜索人员" }).fill("dave");
  await page
    .getByRole("listbox", { name: "人员搜索结果" })
    .getByRole("option", { name: /Dave/ })
    .click();
  await page.getByRole("button", { name: "保存草稿", exact: true }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "设计草稿已保存" }),
  ).toBeVisible();
  const response = await request.get(
    process.env.APPROVAL_SMOKE_API +
      "/api/approval/definitions/react-canvas-parity/governance",
    { headers: { "X-User-Id": "admin" } },
  );
  expect(response.ok()).toBe(true);
  const saved = await response.json();
  const definition = JSON.parse(saved.draftJson);
  expect(definition.schemaVersion).toBe(1);
  const review = Object.values(definition.nodes).find(
    (node: any) => node.type === "Approval",
  ) as any;
  expect(definition.nodes.start.next).toBe(review.id);
  expect(review.next).toBe("end");
  expect(review.approvers).toEqual([{ type: "User", value: "dave" }]);
  await page.getByRole("button", { name: "校验", exact: true }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "校验完成" }),
  ).toBeVisible();
  expect(await page.getByRole("alert").count()).toBe(0);
  await page.goto(
    process.env.APPROVAL_VUE_URL + "/designer/react-canvas-parity",
  );
  await expect(
    page.locator(".vue-flow__node").filter({ hasText: "审批" }),
  ).toBeVisible();
  expect(errors).toEqual([]);
});
