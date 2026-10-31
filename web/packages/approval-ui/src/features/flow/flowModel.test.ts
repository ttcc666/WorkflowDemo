import { describe, expect, it } from 'vitest'
import type { ApprovalInstance, ApprovalTask } from '../../api/types'
import {
  applyConnection,
  canInsertOnEdge,
  checkConnection,
  createNodeData,
  findEdgeAtPoint,
  insertNodeOnEdge,
  makeRoomBelow,
  FLOW_NODE_TYPE,
  NODE_HEIGHT,
  computeRunState,
  toDefinition,
  toFlowElements,
  NODE_WIDTH,
  type FlowDefinition,
} from './flowModel'

// 与后端示例 certificate.update.json 结构一致（无坐标）
const certificateFlow: FlowDefinition = {
  startNodeId: 'start',
  nodes: {
    start: { id: 'start', name: '提交申请', type: 'Start', next: 'dept' },
    dept: { id: 'dept', name: '部门审核', type: 'Approval', mode: 'Any', approvers: [{ type: 'Role', value: 'DeptReviewer' }], next: 'cond' },
    cond: {
      id: 'cond',
      name: '证书类型判断',
      type: 'Condition',
      branches: [
        { priority: 1, expression: 'certType == "Special"', next: 'safety' },
        { priority: 99, expression: null, next: 'leader' },
      ],
    },
    safety: { id: 'safety', name: '安全审核', type: 'Approval', approvers: [{ type: 'Role', value: 'SafetyOfficer' }], next: 'leader' },
    leader: { id: 'leader', name: '负责人', type: 'Approval', mode: 'All', approvers: [{ type: 'Role', value: 'Leader' }], next: 'end' },
    end: { id: 'end', name: '结束', type: 'End' },
  },
}

describe('toFlowElements / toDefinition', () => {
  it('round-trips structure and adds positions via auto layout', () => {
    const { nodes, edges } = toFlowElements(certificateFlow)

    expect(nodes).toHaveLength(6)
    expect(edges).toHaveLength(6)
    expect(new Set(nodes.map((n) => `${n.position.x},${n.position.y}`)).size).toBe(6) // 排版后不重叠
    expect(edges.find((e) => e.target === 'leader' && e.source === 'cond')?.label).toBe('默认')

    const back = toDefinition(nodes, edges)
    expect(back.startNodeId).toBe('start')
    expect(back.nodes.dept).toMatchObject({ type: 'Approval', mode: 'Any', next: 'cond', approvers: [{ type: 'Role', value: 'DeptReviewer' }] })
    expect(back.nodes.leader.mode).toBe('All')
    expect(back.nodes.cond.branches).toEqual([
      { priority: 1, expression: 'certType == "Special"', next: 'safety' },
      { priority: 99, expression: null, next: 'leader' },
    ])
    expect(back.nodes.end.next).toBeUndefined()
    expect(back.nodes.start.position).toBeTruthy()
  })

  it('keeps saved positions instead of re-layout', () => {
    const flow = structuredClone(certificateFlow)
    for (const [i, node] of Object.values(flow.nodes).entries()) node.position = { x: i * 10, y: i * 20 }

    const { nodes } = toFlowElements(flow)
    expect(nodes.find((n) => n.id === 'cond')?.position).toEqual({ x: 20, y: 40 })
  })

  it('auto layout keeps side branch node clear of the straight default-branch edge', () => {
    const { nodes } = toFlowElements(certificateFlow)
    const center = (id: string) => nodes.find((n) => n.id === id)!.position.x + NODE_WIDTH / 2
    const safety = nodes.find((n) => n.id === 'safety')!.position.x

    // cond → leader 的默认分支在同一竖线上，安全审核节点不能覆盖这条线
    expect(center('cond')).toBeCloseTo(center('leader'))
    const lineX = center('cond')
    expect(lineX < safety - 10 || lineX > safety + NODE_WIDTH + 10).toBe(true)
  })

  it('start node is not deletable', () => {
    const { nodes } = toFlowElements(certificateFlow)
    expect(nodes.find((n) => n.id === 'start')?.deletable).toBe(false)
    expect(nodes.find((n) => n.id === 'dept')?.deletable).toBe(true)
  })
})

describe('connections', () => {
  const { nodes, edges } = toFlowElements(certificateFlow)

  it('rejects invalid connections', () => {
    expect(checkConnection(nodes, edges, 'dept', 'dept')).toBeTruthy()
    expect(checkConnection(nodes, edges, 'end', 'dept')).toBeTruthy()
    expect(checkConnection(nodes, edges, 'dept', 'start')).toBeTruthy()
    expect(checkConnection(nodes, edges, 'cond', 'safety')).toBeTruthy() // 重复分支
    expect(checkConnection(nodes, edges, 'dept', 'leader')).toBeNull()
  })

  it('replaces the single outgoing edge of a normal node', () => {
    const next = applyConnection(nodes, edges, 'dept', 'leader')
    expect(next.filter((e) => e.source === 'dept').map((e) => e.target)).toEqual(['leader'])
  })

  it('adds a conditional branch with next priority when a default exists', () => {
    const next = applyConnection(nodes, edges, 'cond', 'end')
    expect(next.find((e) => e.source === 'cond' && e.target === 'end')?.data).toEqual({ priority: 2, expression: '' })
  })

  it('first branch of a condition without default becomes the default', () => {
    const without = edges.filter((e) => e.source !== 'cond')
    const next = applyConnection(nodes, without, 'cond', 'end')
    expect(next.find((e) => e.source === 'cond')?.data?.expression).toBeNull()
  })
})

describe('computeRunState', () => {
  const task = (nodeId: string, status: ApprovalTask['status'], round = 0): ApprovalTask => ({
    id: nodeId + status,
    instanceId: 'i',
    nodeId,
    nodeName: nodeId,
    assigneeKind: 'Role',
    assigneeValue: 'x',
    kind: 'Approval',
    parentTaskId: null,
    round,
    status,
    order: 0,
    handledBy: null,
    comment: null,
    createdAt: '',
    completedAt: null,
    dueAt: null,
    remindCount: 0,
  })
  const instance = (status: ApprovalInstance['status'], tasks: ApprovalTask[]): ApprovalInstance => ({
    id: 'i',
    definitionId: 'd',
    definitionKey: 'k',
    definitionVersion: 1,
    businessType: 'Certificate',
    businessKey: 'b',
    title: null,
    initiatorId: 'alice',
    status,
    currentNodeId: null,
    round: 0,
    formDataJson: '{}',
    createdAt: '',
    completedAt: null,
    tasks,
    logs: [],
  })

  it('marks done path, active node and skipped branch', () => {
    const { status, visitedEdges } = computeRunState(
      certificateFlow,
      instance('Running', [task('dept', 'Approved'), task('leader', 'Pending')]),
    )
    expect(status.get('start')).toBe('done')
    expect(status.get('dept')).toBe('done')
    expect(status.get('cond')).toBe('done')
    expect(status.get('leader')).toBe('active')
    expect(status.has('safety')).toBe(false) // 普通证书未走安全审核
    expect(visitedEdges.has('cond->leader')).toBe(true)
    expect(visitedEdges.has('cond->safety')).toBe(false)
  })

  it('uses the latest round after a return', () => {
    // 负责人退回到部门审核：部门审核旧任务已同意、新任务待处理；负责人旧任务被驳回
    const { status } = computeRunState(
      certificateFlow,
      instance('Running', [task('dept', 'Approved'), task('leader', 'Rejected'), task('dept', 'Pending', 1)]),
    )
    expect(status.get('dept')).toBe('active')
    expect(status.get('leader')).toBe('rejected')

    const again = computeRunState(
      certificateFlow,
      instance('Running', [task('dept', 'Approved'), task('leader', 'Rejected'), task('dept', 'Approved', 1), task('leader', 'Pending', 1)]),
    )
    expect(again.status.get('leader')).toBe('active')
  })

  it('marks rejected node and finished flow', () => {
    expect(computeRunState(certificateFlow, instance('Rejected', [task('dept', 'Rejected')])).status.get('dept')).toBe('rejected')

    const approved = computeRunState(certificateFlow, instance('Approved', [task('dept', 'Approved'), task('leader', 'Approved')]))
    expect(approved.status.get('end')).toBe('done')
    expect(approved.visitedEdges.has('leader->end')).toBe(true)
  })
})

describe('insert node on edge', () => {
  const { nodes, edges } = toFlowElements(certificateFlow)
  const withNew = (type: 'Approval' | 'Condition' | 'End', position = { x: 0, y: 0 }) => [
    ...nodes,
    { id: 'n', type: FLOW_NODE_TYPE, position, data: createNodeData(type) },
  ]

  it('splits A->B into A->N->B keeping the branch data on A->N', () => {
    const all = withNew('Approval')
    const next = insertNodeOnEdge(all, edges, 'cond->safety', 'n')!
    expect(next.some((e) => e.id === 'cond->safety')).toBe(false)
    expect(next.find((e) => e.id === 'cond->n')?.data).toEqual({ priority: 1, expression: 'certType == "Special"' })
    expect(next.find((e) => e.id === 'n->safety')?.data).toBeUndefined()
    // 分支顺序不变
    const def = toDefinition(all, next)
    expect(def.nodes.cond.branches?.map((b) => b.next)).toEqual(['n', 'leader'])
    expect(def.nodes.n.next).toBe('safety')
  })

  it('inserted condition node gets the outgoing edge as default branch', () => {
    const next = insertNodeOnEdge(withNew('Condition'), edges, 'dept->cond', 'n')!
    expect(next.find((e) => e.id === 'n->cond')?.data).toEqual({ priority: 99, expression: null })
  })

  it('refuses End nodes and nodes that already have edges', () => {
    expect(insertNodeOnEdge(withNew('End'), edges, 'dept->cond', 'n')).toBeNull()
    expect(canInsertOnEdge(nodes, edges, 'safety')).toBe(false)
  })

  it('finds the edge under a point', () => {
    const source = nodes.find((n) => n.id === 'dept')!
    const target = nodes.find((n) => n.id === 'cond')!
    const x = source.position.x + NODE_WIDTH / 2
    const midY = (source.position.y + NODE_HEIGHT + target.position.y) / 2
    expect(findEdgeAtPoint(nodes, edges, { x, y: midY })).toBe('dept->cond')
    expect(findEdgeAtPoint(nodes, edges, { x: x + 500, y: midY })).toBeNull()
  })

  it('pushes nodes below the inserted node down', () => {
    const all = withNew('Approval', { x: 0, y: 100 })
    const moved = makeRoomBelow([...all, { id: 'low', type: FLOW_NODE_TYPE, position: { x: 0, y: 120 }, data: createNodeData('End') }], 'n')
    expect(moved.find((n) => n.id === 'low')!.position.y).toBe(100 + NODE_HEIGHT + 50)
  })
})

describe('notifications, timeout and actions round-trip', () => {
  it('keeps node config and flow-level rules', () => {
    const flow = structuredClone(certificateFlow)
    flow.nodes.dept.timeout = { minutes: 60, action: 'Remind', repeatMinutes: 30, maxReminders: 2 }
    flow.nodes.dept.notifications = [{ events: ['Enter'], recipients: [{ type: 'Assignees', value: '' }], channels: ['InApp'], content: '{标题}' }]
    flow.nodes.act = { id: 'act', name: '动作', type: 'Action', next: 'end', actions: [{ type: 'Script', mode: 'Sync', parameters: { script: 'x' } }] }
    flow.notifications = [{ events: ['Completed'], recipients: [{ type: 'Initiator', value: '' }], channels: ['InApp'] }]

    const { nodes, edges } = toFlowElements(flow)
    const back = toDefinition(nodes, edges, flow.notifications)
    expect(back.nodes.dept.timeout).toEqual(flow.nodes.dept.timeout)
    expect(back.nodes.dept.notifications).toEqual(flow.nodes.dept.notifications)
    expect(back.nodes.act.actions).toEqual(flow.nodes.act.actions)
    expect(back.notifications).toEqual(flow.notifications)
    expect(back.nodes.cond.notifications).toBeUndefined()
  })
})

describe('parallel nodes', () => {
  const parallelFlow: FlowDefinition = {
    startNodeId: 'start',
    nodes: {
      start: { id: 'start', name: '提交', type: 'Start', next: 'p' },
      p: { id: 'p', name: '并行', type: 'Parallel', branches: [{ priority: 1, expression: null, next: 'a' }, { priority: 2, expression: null, next: 'b' }] },
      a: { id: 'a', name: 'A', type: 'Approval', approvers: [{ type: 'User', value: 'u' }], next: 'j' },
      b: { id: 'b', name: 'B', type: 'Approval', approvers: [{ type: 'User', value: 'u' }], next: 'j' },
      j: { id: 'j', name: '合并', type: 'Join', next: 'end' },
      end: { id: 'end', name: '结束', type: 'End' },
    },
  }

  it('round-trips parallel branches as plain edges', () => {
    const { nodes, edges } = toFlowElements(parallelFlow)
    expect(edges.filter((e) => e.source === 'p').map((e) => [e.target, e.data, e.label])).toEqual([
      ['a', undefined, ''],
      ['b', undefined, ''],
    ])
    const back = toDefinition(nodes, edges)
    expect(back.nodes.p.branches).toEqual(parallelFlow.nodes.p.branches)
    expect(back.nodes.p.next).toBeUndefined()
    expect(back.nodes.j.next).toBe('end')
  })

  it('parallel node keeps multiple outgoing edges', () => {
    const { nodes, edges } = toFlowElements(parallelFlow)
    const withoutB = edges.filter((e) => e.id !== 'p->b')
    expect(checkConnection(nodes, edges, 'p', 'a')).toBeTruthy() // 重复分支
    const next = applyConnection(nodes, withoutB, 'p', 'b')
    expect(next.filter((e) => e.source === 'p')).toHaveLength(2)
  })

  it('marks gateways passed when a branch is reached', () => {
    const run = computeRunState(parallelFlow, {
      id: 'i', definitionId: 'd', definitionKey: 'k', definitionVersion: 1, businessType: 'X', businessKey: 'b', title: null,
      initiatorId: 'alice', status: 'Running', currentNodeId: 'a,b', round: 0, formDataJson: '{}', createdAt: '', completedAt: null,
      tasks: [
        { id: '1', instanceId: 'i', nodeId: 'a', nodeName: 'A', assigneeKind: 'User', assigneeValue: 'u', kind: 'Approval', parentTaskId: null,
          round: 0, status: 'Approved', order: 0, handledBy: 'u', comment: null, createdAt: '', completedAt: null, dueAt: null, remindCount: 0 },
        { id: '2', instanceId: 'i', nodeId: 'b', nodeName: 'B', assigneeKind: 'User', assigneeValue: 'u', kind: 'Approval', parentTaskId: null,
          round: 0, status: 'Pending', order: 0, handledBy: null, comment: null, createdAt: '', completedAt: null, dueAt: null, remindCount: 0 },
      ],
      logs: [],
    })
    expect(run.status.get('p')).toBe('done')
    expect(run.status.get('b')).toBe('active')
    expect(run.status.has('j')).toBe(false)
    expect(run.visitedEdges.has('p->a')).toBe(true)
  })
})
