import type { Edge, Node } from '@vue-flow/core'
import type { NodeType, ApproveMode, ApproverRule, EmptyApproverPolicy, ActionStep, NotificationRule, TimeoutPolicy, FieldAccess } from '../../contracts/flow'

/** 节点在流程实例中的状态（只读展示时使用） */
export type NodeRunStatus = 'done' | 'active' | 'rejected' | 'skipped'

export interface FlowNodeData {
  type: NodeType
  name: string
  mode: ApproveMode
  approvers: ApproverRule[]
  emptyApproverPolicy: EmptyApproverPolicy
  allowReturn: boolean
  allowAddSign: boolean
  allowTransfer: boolean
  requireQualifiedAssignee: boolean
  actions: ActionStep[]
  notifications: NotificationRule[]
  timeout: TimeoutPolicy | null
  fieldPermissions: Record<string, FieldAccess>
  status?: NodeRunStatus
}

/** 条件分支信息挂在从条件节点出发的连线上 */
export interface BranchData {
  priority: number
  /** null 表示默认分支 */
  expression: string | null
}

export type FlowNode = Node<FlowNodeData>
export type FlowEdge = Edge<BranchData | undefined>

export const FLOW_NODE_TYPE = 'flow-node'
export const NODE_WIDTH = 220
export const NODE_HEIGHT = 72
/** 从节点面板拖到画布时 dataTransfer 使用的类型 */
export const PALETTE_DRAG_TYPE = 'application/x-approval-node'

export function branchLabel(data: BranchData | undefined) {
  if (!data) return ''
  if (data.expression === null) return '默认'
  return data.expression.trim() ? `${data.priority}. ${data.expression}` : `${data.priority}. (未填写条件)`
}

export function createEdge(source: string, target: string, data?: BranchData): FlowEdge {
  return {
    id: `${source}->${target}`,
    source,
    target,
    data,
    label: branchLabel(data),
    markerEnd: 'arrowclosed',
  }
}
