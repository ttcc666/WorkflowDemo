import {
  memo,
  useImperativeHandle,
  useRef,
  useState,
  useSyncExternalStore,
  type Ref,
} from "react";
import {
  Background,
  Controls,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  ReactFlowProvider,
  applyEdgeChanges,
  applyNodeChanges,
  type Edge,
  type Node,
  type NodeProps,
  type ReactFlowInstance,
} from "@xyflow/react";
import {
  canInsertOnEdge,
  findEdgeAtPoint,
  nodeTypeLabels,
  PALETTE_DRAG_TYPE,
  type FlowEdge,
  type FlowNode,
  type FlowNodeData,
} from "@approval/shared";
import type { DesignerModel } from "../designer/designerModel";
import { ErrorState } from "../../components/ui/primitives";
import "@xyflow/react/dist/style.css";

type CanvasNode = Node<FlowNodeData & Record<string, unknown>>;
type CanvasEdge = Edge;
const FlowNodeCard = memo(({ data }: NodeProps<CanvasNode>) => (
  <div
    className={`ar-flow-node ar-node-${data.type} ar-node-status-${data.status ?? ""}`}
  >
    {data.type !== "Start" && <Handle type="target" position={Position.Top} />}
    <small>
      {nodeTypeLabels[data.type]}
      {data.statusLabel ? ` · ${data.statusLabel}` : ""}
    </small>
    <strong>{data.name}</strong>
    {data.type === "Approval" && (
      <small>
        {data.mode === "All"
          ? "会签"
          : data.mode === "Sequential"
            ? "依次审批"
            : "或签"}
      </small>
    )}
    {data.type === "Action" && (
      <small>{data.actions?.length ?? 0} 个步骤</small>
    )}
    {data.type !== "End" && <Handle type="source" position={Position.Bottom} />}
  </div>
));
export const nodeTypes = { "flow-node": FlowNodeCard };
export const canvasNodes = (
  nodes: FlowNode[],
  selectedId?: string | null,
): CanvasNode[] =>
  nodes.map((node) => ({
    ...node,
    data: { ...node.data! },
    type: "flow-node",
    selected: selectedId === undefined ? false : selectedId === node.id,
    deletable: node.data?.type !== "Start",
  }));
export const canvasEdges = (edges: FlowEdge[]): CanvasEdge[] =>
  edges.map((edge) => ({
    ...edge,
    type: "smoothstep",
    markerEnd: { type: MarkerType.ArrowClosed },
    data: edge.data ? { ...edge.data } : undefined,
  }));
function neutralNodes(nodes: CanvasNode[]): FlowNode[] {
  return nodes.map((node) => ({
    ...node,
    dimensions:
      node.measured?.width && node.measured?.height
        ? { width: node.measured.width, height: node.measured.height }
        : undefined,
  }));
}
function neutralEdges(edges: CanvasEdge[]): FlowEdge[] {
  return edges.map((edge) => ({
    ...edge,
    label: typeof edge.label === "string" ? edge.label : "",
    markerEnd: "arrowclosed",
    data: edge.data as FlowEdge["data"],
  }));
}
export interface FlowCanvasHandle {
  viewportCenter: () => { x: number; y: number };
  fit: () => void;
}
export function FlowCanvas({
  model,
  ref,
  onInspect,
}: {
  model: DesignerModel;
  ref?: Ref<FlowCanvasHandle>;
  onInspect?: () => void;
}) {
  return (
    <ReactFlowProvider>
      <EditorCanvas model={model} canvasRef={ref} onInspect={onInspect} />
    </ReactFlowProvider>
  );
}
function EditorCanvas({
  model,
  canvasRef,
  onInspect,
}: {
  model: DesignerModel;
  canvasRef?: Ref<FlowCanvasHandle>;
  onInspect?: () => void;
}) {
  const state = useSyncExternalStore(
    model.state.subscribe,
    model.state.getSnapshot,
  );
  const [flow, setFlow] = useState<ReactFlowInstance<CanvasNode, CanvasEdge>>();
  const [error, setError] = useState<unknown>(null);
  const [dropEdge, setDropEdge] = useState<string | null>(null);
  const container = useRef<HTMLDivElement>(null);
  useImperativeHandle(
    canvasRef,
    () => ({
      viewportCenter() {
        const rect = container.current?.getBoundingClientRect();
        if (!rect || !flow) return { x: 300, y: 160 };
        return flow.screenToFlowPosition({
          x: rect.left + rect.width / 2,
          y:
            (Math.max(0, rect.top) +
              Math.min(rect.bottom, window.innerHeight - 20)) /
            2,
        });
      },
      fit() {
        requestAnimationFrame(() => {
          void flow?.fitView({ padding: 0.15, maxZoom: 1 });
        });
      },
    }),
    [flow],
  );
  const nodes = canvasNodes(state.nodes, state.selectedNodeId);
  const edges = canvasEdges(state.edges).map((edge) => ({
    ...edge,
    selected: state.selectedEdgeId === edge.id,
    style:
      dropEdge === edge.id
        ? { stroke: "var(--primary)", strokeWidth: 4 }
        : undefined,
  }));
  return (
    <div
      ref={container}
      className="ar-flow-canvas"
      aria-label="流程画布"
      onDragOver={(event) => {
        if (!event.dataTransfer.types.includes(PALETTE_DRAG_TYPE)) return;
        event.preventDefault();
        if (flow)
          setDropEdge(
            findEdgeAtPoint(
              state.nodes,
              state.edges,
              flow.screenToFlowPosition({ x: event.clientX, y: event.clientY }),
            ),
          );
      }}
      onDragLeave={() => setDropEdge(null)}
      onDrop={(event) => {
        event.preventDefault();
        if (!flow) return;
        const type = event.dataTransfer.getData(
          PALETTE_DRAG_TYPE,
        ) as FlowNodeData["type"];
        if (!Object.hasOwn(nodeTypeLabels, type)) return;
        const center = flow.screenToFlowPosition({
          x: event.clientX,
          y: event.clientY,
        });
        model.addNode(
          type,
          center,
          findEdgeAtPoint(state.nodes, state.edges, center),
          false,
        );
        setDropEdge(null);
      }}
    >
      <ReactFlow<CanvasNode, CanvasEdge>
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        fitView
        fitViewOptions={{ padding: 0.2, maxZoom: 1 }}
        onInit={setFlow}
        onNodesChange={(changes) => {
          const current = model.state.value;
          const next = neutralNodes(
            applyNodeChanges(
              changes,
              canvasNodes(current.nodes, current.selectedNodeId),
            ),
          );
          const removed = new Set(
            changes
              .filter((change) => change.type === "remove")
              .map((change) => change.id),
          );
          model.setCanvas(
            next,
            current.edges.filter(
              (edge) => !removed.has(edge.source) && !removed.has(edge.target),
            ),
            removed.size > 0,
          );
        }}
        onEdgesChange={(changes) => {
          const current = model.state.value;
          model.setCanvas(
            current.nodes,
            neutralEdges(applyEdgeChanges(changes, canvasEdges(current.edges))),
            changes.some((change) => change.type === "remove"),
          );
        }}
        onNodeClick={(_event, node) => {
          model.select(node.id);
          onInspect?.();
        }}
        onEdgeClick={(_event, edge) => {
          model.select(null, edge.id);
          onInspect?.();
        }}
        onPaneClick={() => model.select(null)}
        onConnect={(connection) => {
          const message = model.connect(connection.source, connection.target);
          setError(message);
        }}
        onNodeDragStop={(_event, node) => {
          const current = model.state.value;
          const edgeId = canInsertOnEdge(current.nodes, current.edges, node.id)
            ? findEdgeAtPoint(
                current.nodes,
                current.edges,
                { x: node.position.x + 110, y: node.position.y + 36 },
                { excludeNodeId: node.id },
              )
            : null;
          if (edgeId) model.insertOnEdge(node.id, edgeId);
          else model.record();
        }}
        deleteKeyCode={["Backspace", "Delete"]}
        minZoom={0.15}
        maxZoom={2}
      >
        <Background gap={20} />
        <Controls />
      </ReactFlow>
      <ErrorState error={error} />
    </div>
  );
}
