import { describe, expect, it } from 'vitest'
import { effectScope, nextTick, shallowRef } from 'vue'
import { useCarbonCopyRecipients } from './useCarbonCopyRecipients'
import type { CarbonCopyRecipients } from '../../contracts/api'

const result = (id: string): CarbonCopyRecipients => ({ carbonCopyId: id, assigneeKind: 'Role', assigneeValue: 'HR', isCurrentRoleMembership: true, queriedAt: '', items: [], total: 42, page: 1, pageSize: 20 })
function setup() {
  const scope = effectScope(), actor = shallowRef('alice'), id = shallowRef('one'), visible = shallowRef(true)
  const calls: Array<{ id: string; page: number; size: number; signal?: AbortSignal; resolve: (value: CarbonCopyRecipients) => void; reject: (error: Error) => void }> = []
  const state = scope.run(() => useCarbonCopyRecipients({ carbonCopyRecipients: (id, page = 1, size = 20, signal) => new Promise((resolve, reject) => calls.push({ id, page, size, signal, resolve, reject })) }, actor, () => id.value, visible))!
  return { scope, actor, id, visible, calls, state }
}
describe('抄送人员请求隔离', () => {
  it('按服务器分页，切换记录清空数据并拒绝旧响应', async () => {
    const x = setup()
    try {
      x.calls[0]!.resolve(result('one')); await nextTick(); expect(x.state.data.value?.carbonCopyId).toBe('one')
      x.state.page.value = 2; await nextTick()
      expect(x.calls[1]).toMatchObject({ id: 'one', page: 2, size: 20 }); expect(x.state.data.value).toBeNull()
      x.id.value = 'two'; await nextTick(); expect(x.calls[1]!.signal?.aborted).toBe(true)
      x.calls[1]!.resolve(result('old')); await nextTick(); expect(x.state.data.value).toBeNull()
      expect(x.calls[2]).toMatchObject({ id: 'two', page: 1 })
      x.calls[2]!.resolve(result('two')); await nextTick(); expect(x.state.data.value?.carbonCopyId).toBe('two')
    } finally { x.scope.stop() }
  })
  it('切换身份立即关闭弹窗，忽略旧成功与错误，关闭后中止请求', async () => {
    const x = setup()
    try {
      x.actor.value = 'bob'; expect(x.visible.value).toBe(false); expect(x.state.data.value).toBeNull()
      x.calls[0]!.resolve(result('secret')); await nextTick(); expect(x.state.data.value).toBeNull(); expect(x.calls[0]!.signal?.aborted).toBe(true)
      x.visible.value = true; await nextTick(); x.visible.value = false
      x.calls[1]!.reject(new Error('private error')); await nextTick(); expect(x.state.error.value).toBe(''); expect(x.state.loading.value).toBe(false)
    } finally { x.scope.stop() }
  })
  it('当前请求失败可见，无记录不请求，停用作用域中止请求', async () => {
    const x = setup()
    x.calls[0]!.reject(new Error('directory offline')); await nextTick(); expect(x.state.error.value).toBe('directory offline')
    x.id.value = ''; await nextTick(); expect(x.calls).toHaveLength(1); expect(x.state.error.value).toBe('')
    x.id.value = 'two'; await nextTick(); x.scope.stop(); expect(x.calls[1]!.signal?.aborted).toBe(true)
  })
})
