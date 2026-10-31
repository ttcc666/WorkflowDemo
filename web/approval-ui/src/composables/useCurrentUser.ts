import { computed, shallowRef } from 'vue'
import { demoApi } from '@/api/approval'
import type { DemoUser } from '@/api/types'
import { currentUserId, setCurrentUserId } from '@/stores/session'

// 全应用共享一份用户列表，只加载一次
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

/** 当前用户与演示用户列表。 */
export function useCurrentUser() {
  void ensureLoaded()

  const roles = computed(() => users.value.find((u) => u.id === currentUserId.value)?.roles ?? [])
  const isAdmin = computed(() => roles.value.includes('Admin'))
  const allRoles = computed(() => [...new Set(users.value.flatMap((u) => u.roles))].sort())

  return {
    userId: computed(() => currentUserId.value),
    users: computed(() => users.value),
    roles,
    allRoles,
    isAdmin,
    switchUser: setCurrentUserId,
  }
}
