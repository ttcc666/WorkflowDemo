import { computed, shallowRef } from 'vue'
import { useApprovalUser } from '@approval/ui'
import { demoApi } from '@/api/demo'
import type { DemoUser } from '@/api/types'
import { currentUserId, setCurrentUserId } from '@/stores/session'

// 演示用户列表只加载一次
const users = shallowRef<DemoUser[]>([])
let loadPromise: Promise<void> | null = null

function ensureLoaded() {
  loadPromise ??= demoApi
    .users()
    .then((list) => {
      users.value = list
    })
    .catch(() => {
      loadPromise = null
    })
  return loadPromise
}

/** 演示用户切换：当前用户 ID 存在 localStorage，经请求头 X-User-Id 传给后端。 */
export function useDemoUsers() {
  void ensureLoaded()
  const { roles } = useApprovalUser()

  return {
    userId: computed(() => currentUserId.value),
    users: computed(() => users.value),
    roles,
    switchUser: setCurrentUserId,
  }
}
