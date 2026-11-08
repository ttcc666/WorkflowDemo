import type { ReactNode } from "react";
import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import {
  newFlowDefinition,
  type ApprovalSimulationResult,
} from "@approval/shared";
import { FlowViewer } from "../../../src/features/flow/FlowViewer";

vi.mock("@xyflow/react", async (original) => ({
  ...(await original<typeof import("@xyflow/react")>()),
  ReactFlowProvider: ({ children }: { children: ReactNode }) => children,
  ReactFlow: ({
    nodes,
  }: {
    nodes: { id: string; className?: string; data: { statusLabel?: string } }[];
  }) => (
    <div>
      {nodes.map((node) => (
        <span key={node.id} data-preview-class={node.className}>
          {node.data.statusLabel}
        </span>
      ))}
    </div>
  ),
  Background: () => null,
  Controls: () => null,
  useNodesInitialized: () => true,
  useReactFlow: () => ({ setCenter: vi.fn() }),
}));

it("simulation viewer renders the latest observation when a node is visited again", () => {
  const definition = newFlowDefinition();
  definition.nodes.start.next = "join";
  definition.nodes.join = {
    id: "join",
    name: "汇聚节点",
    type: "Join",
    next: "end",
  };
  const step = {
    nodeId: "join",
    nodeName: "汇聚节点",
    nodeType: "Join" as const,
    blockReason: null,
    evaluations: [],
    excludedAssignees: [],
    resolvedAssignees: [],
    assignments: [],
    resolutionTrace: [],
  };
  const simulation: ApprovalSimulationResult = {
    isValid: true,
    isComplete: false,
    errors: [],
    warnings: [],
    steps: [
      { ...step, status: "Waiting" },
      { ...step, status: "Visited" },
    ],
  };
  const view = render(
    <FlowViewer definition={definition} simulation={simulation} />,
  );
  expect(screen.getByText("已预演")).toBeInTheDocument();
  expect(screen.queryByText("预演等待中")).not.toBeInTheDocument();
  view.rerender(
    <FlowViewer
      definition={definition}
      simulation={{
        ...simulation,
        steps: [
          { ...step, status: "Visited" },
          {
            ...step,
            status: "Blocked",
            blockReason: "仅显示动作计划，未产生正式输出",
          },
        ],
      }}
    />,
  );
  expect(screen.getByText("仅展示执行计划")).toHaveAttribute(
    "data-preview-class",
    "ar-preview-blocked",
  );
  expect(screen.queryByText("已预演")).not.toBeInTheDocument();
  expect(screen.queryByText("已拒绝")).not.toBeInTheDocument();
});
