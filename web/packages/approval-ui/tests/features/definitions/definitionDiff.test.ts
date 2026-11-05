import { describe, expect, it } from 'vitest'
import type { ApprovalDefinition } from '@ui/contracts/api'
import type { FlowDefinition } from '@ui/contracts/flow'
import { compareDefinitionVersions } from '@ui/features/definitions/definitionDiff'

const flow = (): FlowDefinition => ({
  schemaVersion: 1, startNodeId: 'start', settings: { category: 'Finance', startable: true },
  form: { fields: [{ key: 'amount', label: 'Amount', type: 'Number' }] },
  nodes: {
    start: { id: 'start', name: 'Start', type: 'Start', next: 'review' },
    review: { id: 'review', name: 'Review', type: 'Approval', mode: 'Sequential', next: 'route',
      approvers: [{ type: 'User', value: 'alice' }, { type: 'User', value: 'bob' }] },
    route: { id: 'route', name: 'Route', type: 'Condition', branches: [
      { next: 'split', priority: 10, expression: 'amount > 50' }, { next: 'end', priority: 20 },
    ] },
    split: { id: 'split', name: 'Split', type: 'Parallel', branches: [{ next: 'left', priority: 1 }, { next: 'right', priority: 2 }] },
    left: { id: 'left', name: 'Left', type: 'Cc', next: 'join' },
    right: { id: 'right', name: 'Right', type: 'Cc', next: 'join' },
    join: { id: 'join', name: 'Join', type: 'Join', next: 'end' },
    end: { id: 'end', name: 'End', type: 'End' },
  },
})

function version(version: number, definition: FlowDefinition): ApprovalDefinition {
  return { id: `v${version}`, version, key: 'expense', name: 'Expenses', definitionJson: JSON.stringify(definition),
    isPublished: true, createdAt: '2026-01-01T00:00:00Z' }
}

describe('compareDefinitionVersions', () => {
  it('ignores formatting and object property order', () => {
    const base = version(1, flow())
    const target = version(2, flow())
    target.definitionJson = JSON.stringify(JSON.parse(target.definitionJson), (_, value) =>
      value && typeof value === 'object' && !Array.isArray(value) ? Object.fromEntries(Object.entries(value).reverse()) : value, 2)
    expect(compareDefinitionVersions(base, target)).toMatchObject({ changes: [], nodes: { added: 0, removed: 0, modified: 0 } })
  })

  it('reports deterministic node, rule, route and form changes with before and after values', () => {
    const before = flow()
    const after = flow()
    delete after.nodes.right
    after.nodes.extra = { id: 'extra', name: 'Extra', type: 'Approval', next: 'join' }
    after.nodes.review!.mode = 'All'
    after.nodes.review!.approvers!.reverse()
    after.nodes.review!.fieldPermissions = { amount: 'Hidden' }
    after.nodes.review!.next = 'split'
    after.nodes.route!.branches = [{ next: 'split', priority: 10, expression: 'amount > 500' }, { next: 'end', priority: 30 }]
    after.nodes.split!.branches![1]!.next = 'extra'
    after.nodes.join!.next = 'route'
    after.form!.fields[0]!.required = true
    after.settings!.category = 'Operations'
    const base = version(1, before)
    const target = version(2, after)
    target.name = 'Operations Expenses'
    const result = compareDefinitionVersions(base, target)
    expect(result.nodes).toEqual({ added: 1, removed: 1, modified: 4 })
    expect(result.changes.map(({ path }) => path)).toEqual([
      '/name', '/settings/category', '/form/fields/amount/required', '/nodes/extra', '/nodes/join/next',
      '/nodes/review/approvers/1/value', '/nodes/review/approvers/2/value', '/nodes/review/fieldPermissions',
      '/nodes/review/mode', '/nodes/review/next', '/nodes/right', '/nodes/route/branches/1/expression',
      '/nodes/route/branches/2/priority', '/nodes/split/branches/2/next',
    ])
    expect(result.changes).toContainEqual({ kind: 'modified', area: 'rule', nodeId: 'review', path: '/nodes/review/mode', before: 'Sequential', after: 'All' })
    expect(compareDefinitionVersions(base, target).changes).toEqual(result.changes)
    expect(compareDefinitionVersions(target, base).nodes).toEqual({ added: 1, removed: 1, modified: 4 })
  })

  it('distinguishes branch additions, removals and reordering', () => {
    const before = flow()
    const after = flow()
    after.nodes.route!.branches!.reverse()
    after.nodes.route!.branches!.splice(1, 0, { next: 'review', priority: 5, expression: 'amount > 1000' })
    const base = version(1, before)
    const target = version(2, after)
    expect(compareDefinitionVersions(base, target).changes.map(({ kind }) => kind)).toEqual(['added', 'modified'])
    expect(compareDefinitionVersions(target, base).changes.map(({ kind }) => kind)).toEqual(['removed', 'modified'])
  })

  it('compares display-only fields even when they share a key', () => {
    const before = flow()
    before.form!.fields.unshift({ key: '', type: 'Description', label: '说明一' }, { key: '', type: 'Description', label: '说明二' })
    const after = structuredClone(before)
    after.form!.fields[0]!.label = '修改说明一'
    after.form!.fields[1]!.label = '修改说明二'
    expect(compareDefinitionVersions(version(1, before), version(2, after)).changes.map(({ before, after }) => [before, after]))
      .toEqual([['说明一', '修改说明一'], ['说明二', '修改说明二']])
  })

  it('rejects duplicate, unpublished and unrelated versions', () => {
    const base = version(1, flow())
    const target = version(2, flow())
    for (const invalid of [base, { ...target, isPublished: false }, { ...target, key: 'other' }]) {
      expect(() => compareDefinitionVersions(base, invalid)).toThrow('同一流程的两个不同发布版本')
    }
  })
})
