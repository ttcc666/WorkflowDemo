import { computed, type Ref } from 'vue'
import { useApprovalClient } from '../../client'
import { ApiError } from '../../api/http'
import { useAsyncData } from '../../composables/useAsyncData'
import type { FlowDefinition } from '../../contracts/flow'

/**
 * 审批详情：实例（含任务、轨迹，已按当前用户去掉隐藏字段）、实例绑定版本的流程定义（公开视图）、
 * 当前用户可处理的任务及其所在节点可在同意时修改的字段。
 */
export function useApprovalDetail(instanceId: Ref<string>) {
  const { api: approvalApi, userKey: approvalUserKey } = useApprovalClient()
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
  const handling = useAsyncData((signal) => approvalApi.myTask(instanceId.value, signal), {
    watch: [instanceId, approvalUserKey],
  })
  const myTask = computed(() => handling.data.value?.task)
  const editableFields = computed(() => handling.data.value?.editableFields ?? [])

  // 使用服务端的操作能力，包含宿主撤回配置；切换用户或刷新详情时随响应更新。
  const canWithdraw = computed(() => instance.data.value?.canWithdraw ?? false)

  /** 当前待办所在节点的退回 / 加签选项 */
  const nodeOptions = computed(() => {
    const nodeId = myTask.value?.nodeId
    let node
    try {
      node = nodeId && definition.data.value ? (JSON.parse(definition.data.value.definitionJson) as FlowDefinition).nodes[nodeId] : undefined
    } catch {
      node = undefined
    }
    const isApproval = node?.type === 'Approval'
    return { allowReturn: isApproval && node?.allowReturn !== false, allowAddSign: isApproval && node?.allowAddSign !== false,
      allowTransfer: isApproval && node?.allowTransfer !== false }
  })

  async function reload() {
    await Promise.all([instance.reload(), handling.reload()])
  }

  return {
    instance: instance.data,
    loading: instance.loading,
    error: instance.error,
    notFound,
    definition: definition.data,
    myTask,
    editableFields,
    canWithdraw,
    nodeOptions,
    reload,
  }
}
