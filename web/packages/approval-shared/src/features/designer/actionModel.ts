import type { ApprovalActionMeta } from '../../contracts/api.js'
import type { ActionStep } from '../../contracts/flow.js'
import type { FlowEdge, FlowNode } from '../flow/flowElements.js'

export function configureActionStep(step: ActionStep, descriptor: ApprovalActionMeta): ActionStep {
  const type = descriptor.type
  const mode = type !== 'Http' && descriptor.supportedModes.includes('Sync') ? 'Sync' : 'Async'
  const schema = descriptor.configSchema as { properties?: Record<string, { default?: unknown }> } | null
  const defaults = Object.fromEntries(Object.entries(schema?.properties ?? {}).filter(([, field]) => field.default !== undefined).map(([key, field]) => [key, field.default]))
  const config = type === 'JavaScript' ? { script: 'function execute(context) {\n  return { amount: context.form.amount };\n}' } : type === 'Mapping' ? { mappings: [] } : type === 'Notification' ? { recipients: [{ type: 'Initiator', value: '' }], channels: ['InApp'], title: '', content: '' } : defaults
  return { ...step, type, mode, config, name: descriptor.displayName ?? type, outputKey: null, failurePolicy: mode === 'Async' ? 'Retry' : 'Fail', configVersion: descriptor.configVersion }
}
export function upstreamActions(nodes: FlowNode[], edges: FlowEdge[], nodeId: string): ActionStep[] {
  const incoming = new Map<string, string[]>()
  for (const edge of edges) incoming.set(edge.target, [...incoming.get(edge.target) ?? [], edge.source])
  const visited = new Set([nodeId]); const pending = [nodeId]
  while (pending.length) for (const source of incoming.get(pending.pop()!) ?? []) if (!visited.has(source)) { visited.add(source); pending.push(source) }
  visited.delete(nodeId)
  return nodes.filter(node => visited.has(node.id)).flatMap(node => node.data?.actions ?? [])
}
export function actionOutputPaths(steps: ActionStep[]): string[] {
  return steps.flatMap(step => {
    if (!step.outputKey || step.mode !== 'Sync' || step.type === 'Notification') return []
    if (step.type === 'Http') return [step.outputKey, `${step.outputKey}.statusCode`, `${step.outputKey}.body`]
    return step.type === 'Sql' ? [step.outputKey, `${step.outputKey}.rows`, `${step.outputKey}.rows[0]`, `${step.outputKey}.count`] : [step.outputKey]
  })
}
