import { computed, type Ref } from 'vue'
import { approvalApi } from '@/api/approval'
import { useAsyncData } from '@/composables/useAsyncData'
import { currentUserId } from '@/stores/session'

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
    watch: [instanceId, currentUserId],
  })

  const isInitiator = computed(() => instance.data.value?.initiatorId === currentUserId.value)
  const canWithdraw = computed(
    () =>
      isInitiator.value &&
      instance.data.value?.status === 'Running' &&
      !instance.data.value.tasks.some((t) => t.status === 'Approved'),
  )

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
    reload,
  }
}
