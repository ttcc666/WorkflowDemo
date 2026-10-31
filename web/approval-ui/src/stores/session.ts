import { shallowRef } from 'vue'

const STORAGE_KEY = 'approval-demo-user'

/**
 * 当前登录用户（演示：从演示用户中切换，并通过请求头 X-User-Id 传给后端）。
 * 接入真实认证时，把这里替换为从令牌/会话中获取的用户即可。
 */
export const currentUserId = shallowRef<string>(localStorage.getItem(STORAGE_KEY) ?? 'alice')

export function setCurrentUserId(userId: string) {
  currentUserId.value = userId
  localStorage.setItem(STORAGE_KEY, userId)
}
