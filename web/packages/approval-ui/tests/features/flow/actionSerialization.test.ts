import { expect, test } from 'vitest'
import type { ActionStep, FlowDefinition } from '@ui/contracts/flow'
import { toDefinition, toFlowElements } from '@ui/features/flow/flowSerialization'
import { createHistory } from '@ui/features/designer/history'

test('action step configs survive canvas serialization and reordering can be undone and redone', () => {
  const steps: ActionStep[] = [
    { id: 'risk', type: 'JavaScript', mode: 'Sync', configVersion: 1, failurePolicy: 'Fail', outputKey: 'risk',
      config: { script: 'function execute(c) { return { amount: c.form.amount }; }' } },
    { id: 'erp', type: 'Http', mode: 'Async', configVersion: 1, failurePolicy: 'Retry',
      config: { body: { amount: '{{outputs.risk.amount}}' } } },
  ]
  const flow: FlowDefinition = { schemaVersion: 1, startNodeId: 's', nodes: {
    s: { id: 's', name: 'Start', type: 'Start', next: 'a' },
    a: { id: 'a', name: 'Action', type: 'Action', actions: steps, next: 'e' },
    e: { id: 'e', name: 'End', type: 'End' },
  } }
  const { nodes, edges } = toFlowElements(flow)
  const node = nodes.find(item => item.id === 'a')!
  const original = toDefinition(nodes, edges)
  expect(original.nodes.a?.actions).toEqual(steps)
  expect(original.nodes.a?.next).toBe('e')
  expect(original.nodes.s).not.toHaveProperty('actions')
  const history = createHistory()
  history.reset(JSON.stringify(original))
  node.data!.actions = [...node.data!.actions!].reverse()
  const reordered = toDefinition(nodes, edges)
  history.record(JSON.stringify(reordered))
  expect(JSON.parse(history.undo()!).nodes.a.actions).toEqual(steps)
  expect(JSON.parse(history.redo()!).nodes.a.actions.map((step: ActionStep) => step.id)).toEqual(['erp', 'risk'])
  expect(flow.nodes.a?.actions).toEqual(steps)
})
