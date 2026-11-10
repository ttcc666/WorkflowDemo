import { useEffect, useMemo } from 'react'
import {
  Background,
  Controls,
  ReactFlow,
  ReactFlowProvider,
  useNodesInitialized,
  useReactFlow,
} from '@xyflow/react'
import {
  computeRunState,
  computeSimulationState,
  toFlowElements,
  type ApprovalInstance,
  type ApprovalSimulationResult,
  type FlowDefinition,
} from '@approval/shared'
import { canvasEdges, canvasNodes, nodeTypes } from './FlowCanvas'
import { EmptyState } from '../../components/ui/primitives'
import { simulationPresentation } from './simulationPresentation'

export function FlowViewer({
  definition,
  instance,
  simulation,
  highlightedNodeId,
}: {
  definition: FlowDefinition
  instance?: ApprovalInstance
  simulation?: ApprovalSimulationResult
  highlightedNodeId?: string
}) {
  const elements = useMemo(() => toFlowElements(definition), [definition])
  const run = instance
    ? computeRunState(definition, instance)
    : simulation
      ? computeSimulationState(definition, simulation.steps)
      : null
  if (!elements.nodes.length) return <EmptyState text="没有流程节点" />
  const previewSteps = new Map(
    !instance
      ? simulation?.steps.map((step) => [step.nodeId, step] as const)
      : [],
  )
  const nodes = canvasNodes(elements.nodes).map((node) => {
    const step = previewSteps.get(node.id)
    const preview = step ? simulationPresentation(step) : undefined
    return {
      ...node,
      selected: node.id === highlightedNodeId,
      className:
        step?.status === 'Blocked'
          ? 'ar-preview-blocked'
          : step?.status === 'Failed'
            ? 'ar-preview-failed'
            : undefined,
      data: {
        ...node.data,
        status: preview?.status ?? run?.status.get(node.id),
        statusLabel:
          preview?.label ??
          (run
            ? {
                done: '已完成',
                active: '当前节点',
                rejected: '已拒绝',
                skipped: '未执行',
              }[run.status.get(node.id) ?? 'skipped']
            : undefined),
      },
    }
  })
  const edges = canvasEdges(elements.edges).map((edge) => ({
    ...edge,
    animated: run?.visitedEdges.has(edge.id),
    style: run?.visitedEdges.has(edge.id)
      ? { stroke: 'var(--success-foreground)', strokeWidth: 2 }
      : undefined,
  }))
  return (
    <ReactFlowProvider>
      <FocusNode id={highlightedNodeId} nodes={nodes} />
      <div
        className="ar-flow-canvas"
        style={{ height: 440 }}
        aria-label="流程查看器"
      >
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable={false}
          deleteKeyCode={null}
          fitView
          fitViewOptions={{ padding: 0.2 }}
        >
          <Background />
          <Controls showInteractive={false} />
        </ReactFlow>
      </div>
    </ReactFlowProvider>
  )
}

function FocusNode({
  id,
  nodes,
}: {
  id?: string
  nodes: ReturnType<typeof canvasNodes>
}) {
  const initialized = useNodesInitialized()
  const flow = useReactFlow()
  const position = nodes.find(node => node.id === id)?.position
  const x = position?.x
  const y = position?.y
  useEffect(() => {
    if (!initialized || !id) return
    if (x !== undefined && y !== undefined)
      void flow.setCenter(x + 110, y + 36, {
        zoom: 1,
        duration: 200,
      })
  }, [initialized, id, x, y, flow])
  return null
}
