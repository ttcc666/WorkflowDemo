import type { NodeType, FlowDefinition } from '@ui/contracts/flow'
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
  const defaults: Record<NodeType, Pick<FlowNodeData, 'name' | 'approvers' | 'notifications'>> = {
    Start: { name: '提交申请', approvers: [], notifications: [] },
    // 新审批节点默认发站内信给审批人，可在属性面板修改
    Approval: {
      name: '审批',
      approvers: [{ type: 'Role', value: '' }],
      notifications: [defaultNotification('Enter')],
    },
    Cc: { name: '抄送', approvers: [{ type: 'Initiator', value: '' }], notifications: [] },
    Condition: { name: '条件分支', approvers: [], notifications: [] },
    Parallel: { name: '并行分支', approvers: [], notifications: [] },
    Join: { name: '合并', approvers: [], notifications: [] },
    Action: { name: '业务动作', approvers: [], notifications: [] },
    End: { name: '结束', approvers: [], notifications: [] },
  }
  return {
    type,
    ...(type === 'Action' ? { actions: [] } : {}),
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
