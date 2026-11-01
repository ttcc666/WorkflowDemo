import type { FlowDefinition, FlowNodeDef, NotificationRule } from '../../contracts/flow'
import { createEdge, FLOW_NODE_TYPE, type FlowNode, type FlowEdge } from './flowElements'
import { layoutNodes } from './flowLayout'
import { notifyEventsFor } from './flowPresentation'
import { assertSupportedFlowVersion, currentFlowSchemaVersion } from './flowVersion'

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T

export function toFlowElements(definition: FlowDefinition): { nodes: FlowNode[]; edges: FlowEdge[] } {
  assertSupportedFlowVersion(definition)
  const defs = Object.entries(definition.nodes).map(([key, node]) => ({ ...node, id: node.id || key }))

  const nodes: FlowNode[] = defs.map((def) => ({
    id: def.id,
    type: FLOW_NODE_TYPE,
    position: def.position ? { x: def.position.x, y: def.position.y } : { x: 0, y: 0 },
    deletable: def.type !== 'Start',
    data: {
      type: def.type,
      name: def.name,
      mode: def.mode ?? 'Any',
      approvers: (def.approvers ?? []).map((r) => ({ ...r })),
      emptyApproverPolicy: def.emptyApproverPolicy ?? 'Error',
      allowReturn: def.allowReturn ?? true,
      allowAddSign: def.allowAddSign ?? true,
      allowTransfer: def.allowTransfer ?? true,
      requireQualifiedAssignee: def.requireQualifiedAssignee ?? false,
      actions: clone(def.actions ?? []),
      notifications: clone(def.notifications ?? []),
      timeout: def.timeout ? { ...def.timeout } : null,
      fieldPermissions: { ...def.fieldPermissions },
    },
  }))

  const edges: FlowEdge[] = defs.flatMap((def) => {
    if (def.type === 'Condition')
      return (def.branches ?? []).map((b) =>
        createEdge(def.id, b.next, { priority: b.priority, expression: b.expression?.trim() ? b.expression : null }),
      )
    // 并行分支没有条件，连线不带分支数据
    if (def.type === 'Parallel') return (def.branches ?? []).map((b) => createEdge(def.id, b.next))
    return def.next ? [createEdge(def.id, def.next)] : []
  })

  // 旧定义（没有保存坐标）自动排版
  const needsLayout = defs.some((d) => !d.position)
  return { nodes: needsLayout ? layoutNodes(nodes, edges) : nodes, edges }
}

// ───── 画布 → 定义 ─────

export function toDefinition(nodes: FlowNode[], edges: FlowEdge[], flowNotifications: NotificationRule[] = []): FlowDefinition {
  const start = nodes.find((n) => n.data?.type === 'Start')
  const result: FlowDefinition = { schemaVersion: currentFlowSchemaVersion, startNodeId: start?.id ?? '', nodes: {} }
  if (flowNotifications.length) result.notifications = clone(flowNotifications)

  for (const node of nodes) {
    const data = node.data!
    const outgoing = edges.filter((e) => e.source === node.id)
    const def: FlowNodeDef = {
      id: node.id,
      name: data.name.trim(),
      type: data.type,
      position: { x: Math.round(node.position.x), y: Math.round(node.position.y) },
    }

    if (data.type === 'Approval' || data.type === 'Cc') {
      def.approvers = data.approvers.map((r) => ({ type: r.type, value: r.value.trim() }))
      if (Object.keys(data.fieldPermissions).length) def.fieldPermissions = { ...data.fieldPermissions }
    }
    if (data.type === 'Approval') {
      def.mode = data.mode
      def.emptyApproverPolicy = data.emptyApproverPolicy
      // 只保存非默认值，保持定义简洁
      if (!data.allowReturn) def.allowReturn = false
      if (!data.allowAddSign) def.allowAddSign = false
      if (!data.allowTransfer) def.allowTransfer = false
      if (data.requireQualifiedAssignee) def.requireQualifiedAssignee = true
      if (data.timeout) def.timeout = { ...data.timeout }
    }
    if (data.type === 'Action') def.actions = clone(data.actions)
    if (notifyEventsFor(data.type).length && data.notifications.length) def.notifications = clone(data.notifications)
    if (data.type === 'Condition') {
      def.branches = outgoing.map((e) => ({
        priority: e.data?.priority ?? 1,
        expression: e.data?.expression?.trim() ? e.data.expression.trim() : null,
        next: e.target,
      }))
    } else if (data.type === 'Parallel') {
      def.branches = outgoing.map((e, i) => ({ priority: i + 1, expression: null, next: e.target }))
    } else if (data.type !== 'End') {
      def.next = outgoing[0]?.target ?? null
    }

    result.nodes[node.id] = def
  }
  return result
}
