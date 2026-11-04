import { describe, expect, it, vi } from 'vitest'
import { createSSRApp, h } from 'vue'
import { renderToString } from 'vue/server-renderer'
import ElementPlus, { ID_INJECTION_KEY, ZINDEX_INJECTION_KEY } from 'element-plus'
import { createApprovalClient } from '../../client'
import ApprovalActions from './ApprovalActions.vue'
import type { ApprovalTask } from '../../contracts/api'

const task: ApprovalTask = {
  id: 'task', instanceId: 'instance', nodeId: 'review', nodeName: '审核', assigneeKind: 'User', assigneeValue: 'bob',
  kind: 'Approval', parentTaskId: null, round: 0, status: 'Pending', order: 0, handledBy: null, comment: null,
  createdAt: '', completedAt: null, dueAt: null, remindCount: 0, nextRemindAt: null, lastRemindedAt: null, capabilities: {},
}
async function actions(capabilities: Record<string, boolean>, allowReturn = true) {
  const client = createApprovalClient({ fetcher: async () => Response.json({}) })
  const app = createSSRApp({ render: () => h(ApprovalActions, {
    myTask: task, capabilities, canWithdraw: false, canTerminate: false, currentUserId: 'bob', initiatorId: 'alice',
    allowReturn, allowAddSign: true, allowTransfer: true, busy: false,
  }) }).use(client).use(ElementPlus)
  app.provide(ID_INJECTION_KEY, { prefix: 1024, current: 0 })
  app.provide(ZINDEX_INJECTION_KEY, { current: 0 })
  const warnings = vi.spyOn(console, 'warn')
  try {
    const html = await renderToString(app)
    expect(warnings).not.toHaveBeenCalled()
    return html
  } finally { warnings.mockRestore(); client.dispose() }
}
describe('审批操作的权限能力', () => {
  it('只显示单独允许的动作，未提供能力时拒绝显示', async () => {
    const html = await actions({ 'approval.task.approve': true, 'approval.task.transfer': true })
    expect(html).toContain('同意'); expect(html).toContain('转交')
    expect(html).not.toContain('驳回'); expect(html).not.toContain('加签')
    const empty = await actions({})
    expect(empty).not.toContain('同意'); expect(empty).not.toContain('转交')
  })
  it('允许退回不代表允许驳回，节点禁止退回时仍不显示', async () => {
    const html = await actions({ 'approval.task.return': true })
    expect(html).toContain('退回'); expect(html).not.toContain('驳回')
    expect(await actions({ 'approval.task.return': true }, false)).not.toContain('退回')
  })
})
