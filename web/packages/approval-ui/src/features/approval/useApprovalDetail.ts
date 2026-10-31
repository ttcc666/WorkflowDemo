import { computed, type Ref } from 'vue'
import { approvalApi } from '../../api/approval'
import { useAsyncData } from '../../composables/useAsyncData'
import type { FlowDefinition } from '../flow/flowModel'
import { useApprovalUser } from '../../composables/useApprovalUser'
import { approvalUserKey } from '../../config'

/** 审批详情：实例（含任务、轨迹）、实例绑定版本的流程定义、当前用户可处理的任务。 */
export function useApprovalDetail(instanceId: Ref<string>) {
  const instance = useAsyncData((signal) => approvalApi.instance(instanceId.value, signal), { watch: [instanceId] })

  const definitionId = computed(() => instance.data.value?.definitionId)
  const definition = useAsyncData(
    async (signal) => (definitionId.value ? approvalApi.definitionById(definitionId.value, signal) : undefined),
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
    definition: definition.data,
    myTask: myTask.data,
    canWithdraw,
    nodeOptions,
    reload,
  }
}
