import { expect, test } from "@playwright/test";
const base = () => process.env.APPROVAL_SMOKE_API!;
const headers = () => ({
  "X-User-Id": "admin",
  "Idempotency-Key": crypto.randomUUID(),
});
test("React palette drop inserts on a conditional edge and undo redo preserves its default branch and ordering", async ({
  page,
  request,
}) => {
  const flow = {
    schemaVersion: 1,
    startNodeId: "start",
    nodes: {
      start: {
        id: "start",
        name: "开始",
        type: "Start",
        next: "condition",
        position: { x: 100, y: 0 },
      },
      condition: {
        id: "condition",
        name: "分支",
        type: "Condition",
        branches: [
          { priority: 1, expression: "true", next: "first" },
          { priority: 99, expression: null, next: "end" },
        ],
        position: { x: 100, y: 180 },
      },
      first: {
        id: "first",
        name: "第一分支",
        type: "Cc",
        approvers: [{ type: "Initiator", value: "" }],
        next: "end",
        position: { x: 400, y: 350 },
      },
      end: {
        id: "end",
        name: "结束",
        type: "End",
        position: { x: 100, y: 520 },
      },
    },
  };
  expect(
    await request
      .post(base() + "/api/approval/definitions/drag-ui?name=拖拽验收", {
        headers: headers(),
        data: flow,
      })
      .then((response) => response.ok()),
  ).toBe(true);
  await page.addInitScript(() => {
    if (!localStorage.getItem("approval-demo-user"))
      localStorage.setItem("approval-demo-user", "admin");
  });
  await page.goto("/designer/drag-ui");
  await page.locator(".react-flow__controls-fitview").click();
  const edge = page.locator(
    '.react-flow__edge[data-id="condition->end"] .react-flow__edge-path',
  );
  await expect(edge).toBeAttached();
  const canvas = page.locator(".ar-flow-canvas");
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  const point = await edge.evaluate((element) => {
    const path = element as SVGPathElement;
    const middle = path.getPointAtLength(path.getTotalLength() / 2);
    const mapped = new DOMPoint(middle.x, middle.y).matrixTransform(
      path.getScreenCTM()!,
    );
    return { x: mapped.x, y: mapped.y };
  });
  await page
    .getByRole("button", { name: "添加抄送", exact: true })
    .dragTo(canvas, {
      targetPosition: { x: point.x - box!.x, y: point.y - box!.y },
    });
  await expect(page.locator(".react-flow__node")).toHaveCount(5);
  await page.getByRole("button", { name: "保存草稿", exact: true }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "设计草稿已保存" }),
  ).toBeVisible();
  const inserted = await request
    .get(base() + "/api/approval/definitions/drag-ui/governance", {
      headers: headers(),
    })
    .then((response) => response.json());
  const saved = JSON.parse(inserted.draftJson);
  const middle = saved.nodes.condition.branches[1].next;
  expect(
    saved.nodes.condition.branches.map((branch: any) => ({
      ...branch,
      expression: branch.expression ?? null,
    })),
  ).toEqual([
    { priority: 1, expression: "true", next: "first" },
    { priority: 99, expression: null, next: middle },
  ]);
  expect(middle).not.toBe("end");
  expect(saved.nodes[middle].next).toBe("end");
  await page.getByRole("button", { name: "撤销", exact: true }).click();
  await page.getByRole("button", { name: "撤销", exact: true }).click();
  await expect(page.locator(".react-flow__node")).toHaveCount(4);
  await page.getByRole("button", { name: "重做", exact: true }).click();
  await page.getByRole("button", { name: "重做", exact: true }).click();
  await expect(page.locator(".react-flow__node")).toHaveCount(5);
  await page.getByRole("button", { name: "保存草稿", exact: true }).click();
  await expect
    .poll(
      async () =>
        (
          await request
            .get(base() + "/api/approval/definitions/drag-ui/governance", {
              headers: headers(),
            })
            .then((response) => response.json())
        ).revision,
    )
    .toBe(inserted.revision + 1);
  const final = await request
    .get(base() + "/api/approval/definitions/drag-ui/governance", {
      headers: headers(),
    })
    .then((response) => response.json());
  expect(JSON.parse(final.draftJson)).toEqual(saved);
});
