import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, effectScope, nextTick, ref, type EffectScope } from 'vue'
import { ElMessage } from 'element-plus'
import { createApprovalClient, type ApprovalClient } from '@ui/client'
import { ApiError } from '@ui/api/http'
import type { ApprovalInstance, ApprovalTask } from '@ui/contracts/api'
import { useApprovalDetail } from '@ui/features/approval/useApprovalDetail'

vi.mock('element-plus', () => ({ ElMessage: { error: vi.fn() } }))

const api = { instance: vi.fn(), instanceDefinition: vi.fn(), myTask: vi.fn() }
let client: ApprovalClient
let scope: EffectScope

beforeEach(() => {
  vi.resetAllMocks()
  scope = effectScope()
  client = createApprovalClient()
  Object.assign(client.api, api)
  api.instanceDefinition.mockResolvedValue({
    id: 'definition', key: 'doc', version: 1, name: '文档审批', definitionJson: '{"nodes":{}}', isPublished: true, createdAt: '2026-11-01T00:00:00Z',
  })
  api.myTask.mockResolvedValue({ task: null, editableFields: [], capabilities: {} })
})

afterEach(() => { scope.stop(); client.dispose() })

function instance(canWithdraw: boolean, tasks: ApprovalTask[] = []): ApprovalInstance {
  return {
    id: 'instance', definitionId: 'definition', definitionKey: 'doc', definitionVersion: 1,
    businessType: 'Doc', businessKey: 'D1', title: '文档', initiatorId: 'alice', status: 'Running',
    currentNodeId: 'second', round: 0, formDataJson: '{}', createdAt: '2026-11-01T00:00:00Z', completedAt: null,
    capabilities: {}, tasks, logs: [], access: 'Full', hiddenFields: [], canWithdraw,
  }
}

function detail(id = ref<string | undefined>('instance')) {
  return scope.run(() => createApp({}).use(client).runWithContext(() => useApprovalDetail(id)))!
}

describe('审批详情的路由离开', () => {
  it('返回列表移除实例参数后不请求 undefined，并能再次加载有效实例', async () => {
    api.instance.mockImplementation(async (id: string) => ({ ...instance(false), id }))
    const id = ref<string | undefined>('instance')
    const state = detail(id)
    await vi.waitFor(() => expect(state.definition.value?.id).toBe('definition'))
    api.instance.mockClear(); api.instanceDefinition.mockClear(); api.myTask.mockClear()

    id.value = undefined
    await nextTick()

    expect(api.instance).not.toHaveBeenCalled()
    expect(api.instanceDefinition).not.toHaveBeenCalled()
    expect(api.myTask).not.toHaveBeenCalled()
    expect(state.instance.value).toBeUndefined()
    expect(state.myTask.value).toBeUndefined()
    expect(ElMessage.error).not.toHaveBeenCalled()

    id.value = 'another-instance'
    await vi.waitFor(() => expect(state.instance.value?.id).toBe('another-instance'))
    expect(api.instance).toHaveBeenCalledExactlyOnceWith('another-instance', expect.any(AbortSignal))
    expect(api.myTask).toHaveBeenCalledExactlyOnceWith('another-instance', expect.any(AbortSignal))
    expect(api.instanceDefinition).toHaveBeenCalledExactlyOnceWith('another-instance', expect.any(AbortSignal))
  })

  it('离开时取消尚未完成的待办查询，迟到错误不在列表弹出', async () => {
    let reject!: (cause: Error) => void
    api.instance.mockResolvedValue(instance(false))
    api.myTask.mockImplementation(() => new Promise((_resolve, fail) => { reject = fail }))
    const id = ref<string | undefined>('instance')
    const state = detail(id)
    const signal = api.myTask.mock.calls[0]![1] as AbortSignal

    id.value = undefined
    reject(new ApiError('迟到的详情查询失败', 404))
    await nextTick()

    expect(signal.aborted).toBe(true)
    expect(api.myTask).toHaveBeenCalledTimes(1)
    expect(state.myTask.value).toBeUndefined()
    expect(ElMessage.error).not.toHaveBeenCalled()
  })

  it('有效实例查询真正失败时仍显示错误', async () => {
    api.instance.mockRejectedValue(new ApiError('服务暂时不可用', 503))
    const state = detail()

    await vi.waitFor(() => expect(state.error.value).toBe('服务暂时不可用'))

    expect(state.notFound.value).toBe(false)
    expect(ElMessage.error).toHaveBeenCalledExactlyOnceWith('服务暂时不可用')
  })
})

describe('审批详情的撤回能力', () => {
  it('已有审批通过记录时仍尊重服务端允许撤回的结果', async () => {
    const approved: ApprovalTask = {
      id: 'task', instanceId: 'instance', nodeId: 'first', nodeName: '一审', assigneeKind: 'User', assigneeValue: 'bob',
      kind: 'Approval', parentTaskId: null, round: 0, status: 'Approved', order: 0, handledBy: 'bob', comment: null,
      createdAt: '2026-11-01T00:00:00Z', completedAt: '2026-11-01T01:00:00Z', dueAt: null, remindCount: 0, nextRemindAt: null, lastRemindedAt: null, capabilities: {},
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
