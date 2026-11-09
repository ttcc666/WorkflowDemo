import { expect, it } from "vitest";
import { createDesignerModel } from "../../../src/features/designer/designerModel";
import {
  configureActionStep,
  nodeTypeLabels,
  toDefinition,
  toFlowElements,
  type ApprovalActionMeta,
} from "@approval/shared";

it('acknowledges only the persisted designer snapshot and preserves edits made while saving', () => {
  const model = createDesignerModel();
  model.updateNode('start', { name: '提交时的节点' });
  const persisted = model.definition();
  model.updateForm({ fields: [{ key: 'later', label: '保存期间的新字段', type: 'Text' }] });
  model.markSaved(persisted);
  expect(model.definition().form?.fields[0]?.key).toBe('later');
  expect(model.definition().nodes.start.name).toBe('提交时的节点');
  expect(model.state.value.dirty).toBe(true);
  model.undo();
  expect(model.definition()).toEqual(persisted);
  expect(model.state.value.dirty).toBe(false);
  model.redo();
  expect(model.state.value.dirty).toBe(true);
});

it("staggers click-added nodes like Vue and places palette drops at the exact requested center", () => {
  const model = createDesignerModel();
  const click = model.addNode("Approval", { x: 300, y: 160 });
  expect(
    model.state.value.nodes.find((node) => node.id === click)?.position,
  ).toEqual({ x: 238, y: 172 });
  const drop = model.addNode("Action", { x: 300, y: 160 }, null, false);
  expect(
    model.state.value.nodes.find((node) => node.id === drop)?.position,
  ).toEqual({ x: 190, y: 124 });
});

it("click-added nodes avoid existing endpoints while palette drops keep their exact position", () => {
  const model = createDesignerModel();
  const click = model.addNode("Approval", { x: 110, y: 120 });
  expect(
    model.state.value.nodes.find((node) => node.id === click)?.position,
  ).toEqual({ x: 48, y: 256 });
  expect(
    model.state.value.nodes.find((node) => node.id === "start")?.position,
  ).toEqual({ x: 0, y: 0 });
  expect(
    model.state.value.nodes.find((node) => node.id === "end")?.position,
  ).toEqual({ x: 0, y: 160 });
  const drop = model.addNode("Action", { x: 110, y: 196 }, null, false);
  expect(
    model.state.value.nodes.find((node) => node.id === drop)?.position,
  ).toEqual({ x: 0, y: 160 });
  model.undo();
  expect(model.state.value.nodes.some((node) => node.id === drop)).toBe(false);
  expect(
    model.state.value.nodes.find((node) => node.id === click)?.position,
  ).toEqual({ x: 48, y: 256 });
  model.redo();
  expect(
    model.state.value.nodes.find((node) => node.id === drop)?.position,
  ).toEqual({ x: 0, y: 160 });
});

it("inserts a node on a conditional edge and preserves priority expression order through undo redo", () => {
  const model = createDesignerModel();
  const condition = model.addNode("Condition");
  model.connect("start", condition);
  model.connect(condition, "end");
  const review = model.addNode("Approval");
  model.connect(condition, review);
  const branch = model.state.value.edges.find(
    (edge) => edge.target === review,
  )!;
  model.updateBranch(branch.id, { expression: "amount > 100", priority: 2 });
  const before = model.definition();
  const inserted = model.addNode("Action");
  model.insertOnEdge(inserted, branch.id);
  expect(model.definition().nodes[condition]?.branches).toEqual([
    { priority: 99, expression: null, next: "end" },
    { priority: 2, expression: "amount > 100", next: inserted },
  ]);
  expect(model.definition().nodes[inserted]?.next).toBe(review);
  model.undo();
  expect(model.definition().nodes[condition]?.branches).toEqual(
    before.nodes[condition]?.branches,
  );
  model.redo();
  expect(model.definition().nodes[condition]?.branches?.[1]?.next).toBe(
    inserted,
  );
});
it("retains all eight node kinds action JSON form settings permissions and positions on a round trip", () => {
  const model = createDesignerModel();
  for (const type of Object.keys(
    nodeTypeLabels,
  ) as (keyof typeof nodeTypeLabels)[])
    if (type !== "Start" && type !== "End") model.addNode(type);
  const action = model.state.value.nodes.find(
    (node) => node.data?.type === "Action",
  )!;
  model.updateNode(action.id, {
    actions: [
      {
        id: "host",
        type: "ArchiveInvoice",
        mode: "Sync",
        failurePolicy: "Fail",
        configVersion: 3,
        outputKey: "archive",
        config: { nested: { enabled: true, ids: [2, 1] } },
      },
    ],
  });
  const approval = model.state.value.nodes.find(
    (node) => node.data?.type === "Approval",
  )!;
  model.updateNode(approval.id, {
    fieldPermissions: { secret: "Hidden" },
    allowTransfer: false,
  });
  model.updateForm({
    fields: [{ key: "secret", label: "隐藏", type: "Text", span: 12 }],
  });
  model.updateSettings({
    startable: true,
    category: "parity",
    titleTemplate: "{secret}",
  });
  const expected = model.definition();
  const elements = toFlowElements(expected);
  const roundtrip = {
    ...toDefinition(elements.nodes, elements.edges, expected.notifications),
    form: expected.form,
    settings: expected.settings,
  };
  expect(roundtrip).toEqual(expected);
  expect(
    new Set(Object.values(roundtrip.nodes).map((node) => node.type)).size,
  ).toBe(8);
});
it("records form action order settings and layout without recording selection", () => {
  const model = createDesignerModel();
  model.select("start");
  expect(model.state.value.canUndo).toBe(false);
  model.updateForm({
    fields: [{ key: "amount", label: "金额", type: "Money" }],
  });
  model.updateSettings({ category: "finance" });
  model.markSaved();
  expect(model.state.value.dirty).toBe(false);
  model.autoLayout();
  expect(model.state.value.canUndo).toBe(true);
  model.undo();
  expect(model.definition().settings).toEqual({ category: "finance" });
  model.undo();
  expect(model.definition().settings).toBeUndefined();
  model.undo();
  expect(model.definition().form).toBeUndefined();
});
it("rejects forbidden connections protects Start and replaces ordinary successors", () => {
  const model = createDesignerModel();
  expect(model.connect("start", "start")).toBe("不能连接到自身");
  expect(model.connect("end", "start")).toBe("结束节点不能有后续节点");
  const review = model.addNode("Approval");
  expect(model.connect("start", review)).toBeNull();
  expect(
    model.state.value.edges
      .filter((edge) => edge.source === "start")
      .map((edge) => edge.target),
  ).toEqual([review]);
  model.removeNode("start");
  expect(model.definition().startNodeId).toBe("start");
});
it("uses host action descriptors for custom versions defaults modes and no new protocol fields", () => {
  const descriptor: ApprovalActionMeta = {
    type: "ArchiveInvoice",
    displayName: "归档",
    description: "",
    configVersion: 7,
    supportedModes: ["Async"],
    allowsContinue: false,
    configSchema: { properties: { enabled: { default: true } } },
  };
  const result = configureActionStep(
    {
      id: "step1",
      type: "",
      config: {},
      configVersion: 1,
      mode: "Sync",
      outputKey: "old",
      failurePolicy: "Fail",
    },
    descriptor,
  );
  expect(result).toEqual({
    id: "step1",
    name: "归档",
    type: "ArchiveInvoice",
    config: { enabled: true },
    configVersion: 7,
    mode: "Async",
    outputKey: null,
    failurePolicy: "Retry",
  });
});
