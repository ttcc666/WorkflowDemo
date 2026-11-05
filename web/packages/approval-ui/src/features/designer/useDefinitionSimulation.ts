import { onScopeDispose, shallowRef, watch } from 'vue'
import { useApprovalClient } from '@ui/client'
import { errorMessage } from '@ui/api/http'
import type { ApprovalSimulationResult } from '@ui/contracts/api'

export interface SimulationInput { definitionJson: string; formDataJson: string; nodeId?: string; ruleIndex?: number }

/** 只接受输入快照；数据、身份或窗口状态变化后丢弃过期响应。 */
export function useDefinitionSimulation(input: () => SimulationInput, visible: () => boolean) {
  const { api, userKey } = useApprovalClient()
  const result = shallowRef<ApprovalSimulationResult | null>(null)
  const error = shallowRef('')
  const loading = shallowRef(false)
  let revision = 0
  function invalidate() { revision++; result.value = null; error.value = ''; loading.value = false }
  watch([input, userKey, visible], invalidate, { deep: true, flush: 'sync' })
  onScopeDispose(invalidate)
  async function simulate() {
    if (loading.value || !visible()) return
    const current = ++revision
    const request = input()
    loading.value = true; error.value = ''; result.value = null
    try {
      let data: unknown
      try { data = JSON.parse(request.formDataJson) }
      catch { throw new Error('模拟数据不是有效 JSON，请检查逗号、引号和括号') }
      if (!data || Array.isArray(data) || typeof data !== 'object') throw new Error('模拟数据必须是 JSON 对象')
      const response = await api.simulate(request.definitionJson, data as Record<string, unknown>, request.nodeId, request.ruleIndex)
      if (revision === current) result.value = response
    } catch (cause) { if (revision === current) error.value = errorMessage(cause) }
    finally { if (revision === current) loading.value = false }
  }
  return { result, error, loading, simulate }
}
