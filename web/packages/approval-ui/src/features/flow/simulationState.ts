import type { ApprovalSimulationResult } from '@ui/contracts/api'
import type { FlowDefinition } from '@ui/contracts/flow'
import type { NodeRunStatus } from './flowElements'

export function computeSimulationState(definition: FlowDefinition, steps: ApprovalSimulationResult['steps']) {
  const status = new Map<string, NodeRunStatus>(Object.keys(definition.nodes).map(id => [id, 'skipped']))
  const visitedEdges = new Set<string>()
  for (const step of steps) {
    status.set(step.nodeId, ['Blocked', 'Failed'].includes(step.status) ? 'rejected' : ['RequiresDecision', 'Waiting'].includes(step.status) ? 'active' : 'done')
    const node = definition.nodes[step.nodeId]
    if (!node || ['RequiresDecision', 'Blocked', 'Failed', 'Waiting'].includes(step.status)) continue
    if (node.type === 'Condition') {
      for (const evaluation of step.evaluations) if (evaluation.matched) visitedEdges.add(`${step.nodeId}->${evaluation.nextNodeId}`)
    } else if (node.type === 'Parallel') {
      for (const branch of node.branches ?? []) if (steps.some(s => s.nodeId === branch.next)) visitedEdges.add(`${step.nodeId}->${branch.next}`)
    } else if (node.next && steps.some(s => s.nodeId === node.next)) visitedEdges.add(`${step.nodeId}->${node.next}`)
  }
  return { status, visitedEdges }
}
