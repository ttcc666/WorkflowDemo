import { computed, type Ref } from 'vue'
import { approvalApi } from '../../api/approval'
import { ApiError } from '../../api/http'
import { useAsyncData } from '../../composables/useAsyncData'
import type { FlowDefinition } from '../flow/flowModel'
import { useApprovalUser } from '../../composables/useApprovalUser'
import { approvalUserKey } from '../../config'

/** 审批详情：实例（含任务、轨迹）、实例绑定版本的流程定义（公开视图）、当前用户可处理的任务。 */
export function useApprovalDetail(instanceId: Ref<string>) {
  // 切换用户后重新加载（权限可能变化）；无权查看与不存在都返回 404，数据置为 null，页面显示提示
  const instance = useAsyncData(
    async (signal) => {
      try {
        return await approvalApi.instance(instanceId.value, signal)
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) return null
        throw e
      }
    },
    { watch: [instanceId, approvalUserKey] },
  )
  const notFound = computed(() => instance.data.value === null)

  // 实例加载成功后再取定义：公开视图不含动作参数、通知配置，所有能查看实例的人都可读取
  const definitionId = computed(() => instance.data.value?.definitionId)
  const definition = useAsyncData(
    async (signal) => (definitionId.value ? approvalApi.instanceDefinition(instanceId.value, signal) : undefined),
    { watch: [definitionId] },
  )

  // 切换用户后重新判断可处理的任务
  const myTask = useAsyncData(async (signal) => (await approvalApi.myTask(instanceId.value, signal)).task, {
    watch: [instanceId, approvalUserKey],
  })

  const { userId } = useApprovalUser()
  const isInitiator = computed(() => instance.data.value?.initiatorId === userId.value)
  // 未有人同意时可撤回；被退回到发起人时总是可以撤回（放弃本次申请）
  const canWithdraw = computed(() => {
    const value = instance.data.value
    if (!isInitiator.value || value?.status !== 'Running') return false
    const returned = value.tasks.some((t) => t.kind === 'Resubmit' && t.status === 'Pending')
    return returned || !value.tasks.some((t) => t.status === 'Approved')
  })

  /** 当前待办所在节点的退回 / 加签选项 */
  const nodeOptions = computed(() => {
    const nodeId = myTask.data.value?.nodeId
    let node
    try {
      node = nodeId && definition.data.value ? (JSON.parse(definition.data.value.definitionJson) as FlowDefinition).nodes[nodeId] : undefined
    } catch {
      node = undefined
    }
    const isApproval = node?.type === 'Approval'
    return { allowReturn: isApproval && node?.allowReturn !== false, allowAddSign: isApproval && node?.allowAddSign !== false }
  })

  async function reload() {
    await Promise.all([instance.reload(), myTask.reload()])
  }

  return {
    instance: instance.data,
    loading: instance.loading,
    error: instance.error,
    notFound,
    definition: definition.data,
    myTask: myTask.data,
    canWithdraw,
    nodeOptions,
    reload,
  }
}
