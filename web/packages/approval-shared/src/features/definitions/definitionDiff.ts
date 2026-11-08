import type { ApprovalDefinition } from '../../contracts/api.js'
import type { ConditionBranch, FlowDefinition } from '../../contracts/flow.js'
import { assertSupportedFlowVersion } from '../flow/flowVersion.js'

export type ChangeKind = 'added' | 'removed' | 'modified'
export interface DefinitionChange {
  kind: ChangeKind
  area: 'flow' | 'node' | 'rule' | 'route' | 'form' | 'layout'
  nodeId?: string
  path: string
  before: unknown
  after: unknown
}

type Changes = (path: string, before: unknown, after: unknown) => void
const keys = (a: object, b: object) => [...new Set([...Object.keys(a), ...Object.keys(b)])].sort()
const pathFor = (path: string, key: string) => `${path}/${key.replaceAll('~', '~0').replaceAll('/', '~1')}`
const isObject = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value)

function compare(path: string, before: unknown, after: unknown, add: Changes) {
  if (Object.is(before, after)) return
  if (isObject(before) && isObject(after)) {
    for (const key of keys(before, after)) {
      compare(pathFor(path, key), Object.hasOwn(before, key) ? before[key] : undefined,
        Object.hasOwn(after, key) ? after[key] : undefined, add)
    }
  } else if (Array.isArray(before) && Array.isArray(after)) {
    for (let i = 0; i < Math.max(before.length, after.length); i++) compare(`${path}/${i + 1}`, before[i], after[i], add)
  } else {
    add(path, before, after)
  }
}

function compareBranches(path: string, before: ConditionBranch[], after: ConditionBranch[], add: Changes) {
  const unmatchedBefore = new Set(before.keys())
  const unmatchedAfter = new Set(after.keys())
  const pairs = new Map<number, number>()
  function pairBy(key: (branch: ConditionBranch) => string | number) {
    function unique(branches: ConditionBranch[], unmatched: Set<number>) {
      const indexes = new Map<string | number, number | null>()
      for (const index of unmatched) {
        const id = key(branches[index]!)
        indexes.set(id, indexes.has(id) ? null : index)
      }
      return indexes
    }
    const target = unique(after, unmatchedAfter)
    for (const [key, left] of unique(before, unmatchedBefore)) {
      const right = target.get(key)
      if (left == null || right == null) continue
      pairs.set(left, right)
      unmatchedBefore.delete(left)
      unmatchedAfter.delete(right)
    }
  }
  pairBy(branch => JSON.stringify([branch.next, branch.expression ?? null, branch.priority]))
  pairBy(branch => JSON.stringify([branch.next, branch.expression ?? null]))
  pairBy(branch => JSON.stringify([branch.next, branch.priority]))
  pairBy(branch => JSON.stringify([branch.expression ?? null, branch.priority]))
  for (const left of before.keys()) {
    const right = pairs.get(left)
    if (right === undefined) add(`${path}/原分支 ${left + 1}`, before[left], undefined)
    else compare(`${path}/${left + 1}`, before[left], after[right], add)
  }
  for (const right of unmatchedAfter) add(`${path}/目标分支 ${right + 1}`, undefined, after[right])
  const beforeOrder = [...pairs.keys()].sort((a, b) => a - b)
  const afterOrder = [...pairs.entries()].sort((a, b) => a[1] - b[1]).map(([left]) => left)
  if (beforeOrder.some((left, i) => left !== afterOrder[i])) {
    add(`${path}/顺序`, beforeOrder.map(i => before[i]), afterOrder.map(i => after[pairs.get(i)!]))
  }
}

function compareFields(path: string, before: FlowDefinition['form'], after: FlowDefinition['form'], add: Changes) {
  function indexFields(fields: NonNullable<FlowDefinition['form']>['fields']) {
    const counts = new Map<string, number>()
    return new Map(fields.map(field => {
      // Display-only Description fields can share a key, including the empty key.
      const occurrence = (counts.get(field.key) ?? 0) + 1
      counts.set(field.key, occurrence)
      return [JSON.stringify([field.key, occurrence]), { field, occurrence }] as const
    }))
  }
  const left = indexFields(before?.fields ?? [])
  const right = indexFields(after?.fields ?? [])
  for (const id of [...new Set([...left.keys(), ...right.keys()])].sort()) {
    const previous = left.get(id)
    const next = right.get(id)
    const a = previous?.field
    const b = next?.field
    const { field, occurrence } = (previous ?? next)!
    const keyPath = pathFor(path, field.key)
    const fieldPath = occurrence > 1 ? `${keyPath}/${occurrence}` : keyPath
    if (!a || !b) add(fieldPath, a, b)
    else {
      const { columns: ac, ...af } = a
      const { columns: bc, ...bf } = b
      compare(fieldPath, af, bf, add)
      if (ac || bc) compareFields(`${fieldPath}/columns`, { fields: ac ?? [] }, { fields: bc ?? [] }, add)
    }
  }
  const oldOrder = [...left.keys()].filter(id => right.has(id))
  const newOrder = [...right.keys()].filter(id => left.has(id))
  if (oldOrder.some((id, index) => id !== newOrder[index])) {
    add(`${path}/顺序`, oldOrder.map(id => left.get(id)!.field.key || left.get(id)!.field.label),
      newOrder.map(id => right.get(id)!.field.key || right.get(id)!.field.label))
  }
}

const ruleFields = new Set(['mode', 'approvers', 'emptyApproverPolicy', 'allowAddSign', 'allowReturn', 'allowTransfer',
  'requireComment', 'requireQualifiedAssignee', 'notifications', 'timeout', 'actions'])

export function compareDefinitionVersions(base: ApprovalDefinition, target: ApprovalDefinition) {
  if (!base.isPublished || !target.isPublished || base.key !== target.key || base.id === target.id) {
    throw new Error('请选择同一流程的两个不同发布版本')
  }
  const before = JSON.parse(base.definitionJson) as FlowDefinition
  const after = JSON.parse(target.definitionJson) as FlowDefinition
  assertSupportedFlowVersion(before)
  assertSupportedFlowVersion(after)

  const changes: DefinitionChange[] = []
  const nodes = { added: 0, removed: 0, modified: 0, moved: 0 }
  const record = (area: DefinitionChange['area'], nodeId?: string): Changes => (path, before, after) => {
    changes.push({ area, nodeId, path, before, after, kind: before === undefined ? 'added' : after === undefined ? 'removed' : 'modified' })
  }
  compare('/name', base.name, target.name, record('flow'))
  compare('/startNodeId', before.startNodeId, after.startNodeId, record('route'))
  compare('/settings', before.settings ?? {}, after.settings ?? {}, record('flow'))
  compare('/notifications', before.notifications ?? [], after.notifications ?? [], record('rule'))
  if (!!before.form !== !!after.form) compare('/form', !!before.form, !!after.form, record('form'))
  compareFields('/form/fields', before.form, after.form, record('form'))

  for (const id of keys(before.nodes, after.nodes)) {
    const left = Object.hasOwn(before.nodes, id) ? before.nodes[id] : undefined
    const right = Object.hasOwn(after.nodes, id) ? after.nodes[id] : undefined
    const path = pathFor('/nodes', id)
    if (!left || !right) {
      nodes[left ? 'removed' : 'added']++
      record('node', id)(path, left, right)
      continue
    }
    const start = changes.length
    compareBranches(`${path}/branches`, left.branches ?? [], right.branches ?? [], record('route', id))
    for (const key of keys(left, right) as (keyof typeof left)[]) {
      if (key === 'branches' || key === 'id' || key === 'position') continue
      const area = key === 'next' ? 'route' : key === 'fieldPermissions' ? 'form' : ruleFields.has(key) ? 'rule' : 'node'
      compare(pathFor(path, key), left[key], right[key], record(area, id))
    }
    if (changes.length !== start) nodes.modified++
    const beforeLayout = changes.length
    compare(`${path}/position`, left.position ?? null, right.position ?? null, record('layout', id))
    if (changes.length !== beforeLayout) nodes.moved++
  }
  return { before, after, changes, nodes }
}
