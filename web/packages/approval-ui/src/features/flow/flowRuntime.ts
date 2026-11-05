import type { FlowDefinition, FlowNodeDef } from '@ui/contracts/flow'
import type { ApprovalInstance } from '@ui/contracts/api'
import type { NodeRunStatus } from './flowElements'

/** 根据实例的任务与轨迹，推算每个节点的状态以及走过的连线。 */
export function computeRunState(definition: FlowDefinition, instance: ApprovalInstance) {
  const status = new Map<string, NodeRunStatus>()
  const running = (instance.status === 'Running' || instance.status === 'Returned')

  for (const node of Object.values(definition.nodes)) {
    const tasks = latestRound(instance.tasks.filter((t) => t.nodeId === node.id))
    const logged = instance.logs.some((l) => l.nodeId === node.id)

    if (node.type === 'Start') status.set(node.id, running && tasks.some(t => t.status === 'Pending' && t.kind === 'Resubmit') ? 'active' : 'done')
    else if (tasks.some((t) => t.status === 'Rejected')) status.set(node.id, 'rejected')
    else if (running && tasks.some((t) => t.status === 'Pending' || t.status === 'Waiting' || t.status === 'Suspended'))
      status.set(node.id, 'active')
    else if (tasks.some((t) => t.status === 'Approved') || logged) status.set(node.id, 'done')
    else if (node.type === 'End' && instance.status === 'Approved') status.set(node.id, 'done')
  }

  // 条件、并行、合并节点本身不产生任务：只要某个后续节点被走到，就视为已经过。
  // 按拓扑逆序多次传播，处理连续的网关（如并行节点后紧跟条件节点）
  const reached = (id: string) => status.has(id)
  const gateways = Object.values(definition.nodes).filter((n) => n.type === 'Condition' || n.type === 'Parallel' || n.type === 'Join')
  for (let changed = true; changed; ) {
    changed = false
    for (const node of gateways) {
      if (!status.has(node.id) && successors(node).some(reached)) {
        status.set(node.id, 'done')
        changed = true
      }
    }
  }

  const visitedEdges = new Set<string>()
  for (const node of Object.values(definition.nodes)) {
    if (status.get(node.id) !== 'done') continue
    for (const target of successors(node)) if (reached(target)) visitedEdges.add(`${node.id}->${target}`)
  }

  return { status, visitedEdges }
}

const successors = (node: FlowNodeDef) =>
  node.type === 'Condition' || node.type === 'Parallel' ? (node.branches ?? []).map((b) => b.next) : node.next ? [node.next] : []

/** 节点被退回后会重新生成任务，只看最近一轮 */
function latestRound(tasks: ApprovalInstance['tasks']) {
  if (tasks.length === 0) return tasks
  const round = Math.max(...tasks.map((t) => t.round))
  return tasks.filter((t) => t.round === round)
}
