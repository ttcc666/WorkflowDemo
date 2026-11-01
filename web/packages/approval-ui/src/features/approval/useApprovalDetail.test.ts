import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, ref, type EffectScope } from 'vue'
import { approvalApi } from '../../api/approval'
import { ApiError } from '../../api/http'
import type { ApprovalInstance, ApprovalTask } from '../../contracts/api'
import { useApprovalDetail } from './useApprovalDetail'

vi.mock('../../api/approval', () => ({
  approvalApi: { instance: vi.fn(), instanceDefinition: vi.fn(), myTask: vi.fn() },
}))
vi.mock('element-plus', () => ({ ElMessage: { error: vi.fn() } }))

const api = vi.mocked(approvalApi)
let scope: EffectScope

beforeEach(() => {
  vi.resetAllMocks()
  scope = effectScope()
  api.instanceDefinition.mockResolvedValue({
    id: 'definition', key: 'doc', version: 1, name: '文档审批', definitionJson: '{"nodes":{}}', isPublished: true, createdAt: '2026-11-01T00:00:00Z',
  })
  api.myTask.mockResolvedValue({ task: null, editableFields: [] })
})

afterEach(() => scope.stop())

function instance(canWithdraw: boolean, tasks: ApprovalTask[] = []): ApprovalInstance {
  return {
    id: 'instance', definitionId: 'definition', definitionKey: 'doc', definitionVersion: 1,
    businessType: 'Doc', businessKey: 'D1', title: '文档', initiatorId: 'alice', status: 'Running',
    currentNodeId: 'second', round: 0, formDataJson: '{}', createdAt: '2026-11-01T00:00:00Z', completedAt: null,
    tasks, logs: [], access: 'Full', hiddenFields: [], canWithdraw,
  }
}

function detail() {
  return scope.run(() => useApprovalDetail(ref('instance')))!
}

describe('审批详情的撤回能力', () => {
  it('已有审批通过记录时仍尊重服务端允许撤回的结果', async () => {
    const approved: ApprovalTask = {
      id: 'task', instanceId: 'instance', nodeId: 'first', nodeName: '一审', assigneeKind: 'User', assigneeValue: 'bob',
      kind: 'Approval', parentTaskId: null, round: 0, status: 'Approved', order: 0, handledBy: 'bob', comment: null,
      createdAt: '2026-11-01T00:00:00Z', completedAt: '2026-11-01T01:00:00Z', dueAt: null, remindCount: 0,
    }
    const response = instance(true, [approved])
    api.instance.mockResolvedValue(response)
    const state = detail()

    await vi.waitFor(() => expect(state.instance.value).toEqual(response))

    expect(state.canWithdraw.value).toBe(true)
  })

  it('发起人且尚无人审批时也尊重服务端禁止撤回的结果', async () => {
    const response = instance(false)
    api.instance.mockResolvedValue(response)
    const state = detail()

    await vi.waitFor(() => expect(state.instance.value).toEqual(response))

    expect(state.canWithdraw.value).toBe(false)
  })

  it('重新加载后使用最新的撤回能力', async () => {
    api.instance.mockResolvedValue(instance(true))
    const state = detail()
    await vi.waitFor(() => expect(state.canWithdraw.value).toBe(true))

    api.instance.mockResolvedValue(instance(false))
    await state.reload()

    expect(state.canWithdraw.value).toBe(false)
  })

  it('实例不可见时不允许撤回', async () => {
    api.instance.mockRejectedValue(new ApiError('不存在或无权查看', 404))
    const state = detail()

    await vi.waitFor(() => expect(state.notFound.value).toBe(true))

    expect(state.canWithdraw.value).toBe(false)
  })
})
