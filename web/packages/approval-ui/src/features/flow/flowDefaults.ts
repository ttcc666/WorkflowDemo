import type { NodeType, FlowDefinition } from '../../contracts/flow'
import type { FlowNodeData } from './flowElements'
import { defaultNotification } from './flowPresentation'
import { currentFlowSchemaVersion } from './flowVersion'

let idSeed = Date.now() % 100000

export function newNodeId(type: NodeType, existing: Iterable<string>) {
  const ids = new Set(existing)
  let id: string
  do id = `${type.toLowerCase()}_${(idSeed++).toString(36)}`
  while (ids.has(id))
  return id
}

export function createNodeData(type: NodeType): FlowNodeData {
  const defaults: Record<NodeType, Pick<FlowNodeData, 'name' | 'approvers' | 'actions' | 'notifications'>> = {
    Start: { name: '提交申请', approvers: [], actions: [], notifications: [] },
    // 新审批节点默认发站内信给审批人，可在属性面板修改
    Approval: {
      name: '审批',
      approvers: [{ type: 'Role', value: '' }],
      actions: [],
      notifications: [defaultNotification('Enter')],
    },
    Cc: { name: '抄送', approvers: [{ type: 'Initiator', value: '' }], actions: [], notifications: [] },
    Condition: { name: '条件分支', approvers: [], actions: [], notifications: [] },
    Action: { name: '自动动作', approvers: [], actions: [], notifications: [] },
    Parallel: { name: '并行分支', approvers: [], actions: [], notifications: [] },
    Join: { name: '合并', approvers: [], actions: [], notifications: [] },
    End: { name: '结束', approvers: [], actions: [], notifications: [] },
  }
  return {
    type,
    mode: 'Any',
    emptyApproverPolicy: 'Error',
    allowReturn: true,
    allowAddSign: true,
    allowTransfer: true,
    requireQualifiedAssignee: false,
    timeout: null,
    fieldPermissions: {},
    ...defaults[type],
  }
}

export function newFlowDefinition(): FlowDefinition {
  return {
    schemaVersion: currentFlowSchemaVersion,
    startNodeId: 'start',
    nodes: {
      start: { id: 'start', name: '提交申请', type: 'Start', next: 'end', position: { x: 0, y: 0 } },
      end: { id: 'end', name: '结束', type: 'End', position: { x: 0, y: 160 } },
    },
  }
}
