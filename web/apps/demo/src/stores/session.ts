import { shallowRef } from 'vue'

const STORAGE_KEY = 'approval-demo-user'

/** 与后端 DemoCurrentUser 约定的请求头。 */
export const DEMO_USER_HEADER = 'X-User-Id'

/**
 * 当前演示用户（右上角切换，经请求头传给后端）。
 * 接入真实认证时删除此文件，在 ApprovalUI 插件的 headers 中返回 Authorization 头即可。
 */
export const currentUserId = shallowRef<string>(localStorage.getItem(STORAGE_KEY) ?? 'alice')

export function setCurrentUserId(userId: string) {
  currentUserId.value = userId
  localStorage.setItem(STORAGE_KEY, userId)
}
