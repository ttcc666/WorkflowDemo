import { expect, it } from 'vitest'
import { toDefinition, toFlowElements, type FlowDefinition } from './flowModel'

it('流程导入导出保留转交限制与岗位资格要求', () => {
  const definition: FlowDefinition = { startNodeId: 'start', nodes: {
    start: { id: 'start', name: '提交', type: 'Start', next: 'review' },
    review: { id: 'review', name: '复审', type: 'Approval', next: 'end', approvers: [{ type: 'Role', value: 'QualityManager' }],
      allowReturn: false, allowAddSign: false, allowTransfer: false, requireQualifiedAssignee: true },
    end: { id: 'end', name: '结束', type: 'End' },
  } }
  const { nodes, edges } = toFlowElements(definition)
  expect(toDefinition(nodes, edges).nodes.review).toMatchObject({ allowReturn: false, allowAddSign: false, allowTransfer: false, requireQualifiedAssignee: true })
})

it('旧流程保持默认允许转交且不强制原岗位资格', () => {
  const { nodes } = toFlowElements({ startNodeId: 's', nodes: {
    s: { id: 's', name: '提交', type: 'Start', next: 'r' }, r: { id: 'r', name: '审核', type: 'Approval', next: 'e' }, e: { id: 'e', name: '结束', type: 'End' },
  } })
  expect(nodes.find(n => n.id === 'r')?.data).toMatchObject({ allowTransfer: true, requireQualifiedAssignee: false })
})
