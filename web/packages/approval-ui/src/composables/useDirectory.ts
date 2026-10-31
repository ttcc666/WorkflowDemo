import { shallowRef } from 'vue'
import { approvalApi } from '../api/approval'
import type { DirectoryUser } from '../api/types'

// 用户名称与角色列表的应用级缓存：用户选择器回显、设计器角色下拉
const names = new Map<string, DirectoryUser>()
const roles = shallowRef<string[]>([])
/** 宿主未注册用户目录（接口返回 501）时为 false，选择器退化为手动输入 */
const available = shallowRef(true)
let rolesPromise: Promise<void> | null = null

function remember(users: DirectoryUser[]) {
  for (const u of users) names.set(u.id, u)
  return users
}

function handleError(error: unknown) {
  if ((error as { status?: number }).status === 501) available.value = false
  return [] as DirectoryUser[]
}

/** 用户目录：搜索用户、按 ID 取用户、角色列表（后端 IApprovalUserDirectory）。 */
export function useDirectory() {
  async function search(keyword: string, take = 50) {
    return approvalApi.users(keyword, take).then(remember, handleError)
  }

  /** 按 ID 取用户（已缓存的不再请求），用于回显已选用户的名称 */
  async function resolve(ids: string[]) {
    const missing = ids.filter((id) => id && !names.has(id))
    if (missing.length > 0) await approvalApi.usersByIds(missing).then(remember, handleError)
    return ids.map((id) => names.get(id) ?? { id, name: id, roles: [] })
  }

  function loadRoles() {
    rolesPromise ??= approvalApi
      .roles()
      .then((list) => {
        roles.value = list
      })
      .catch((e) => {
        handleError(e)
        rolesPromise = null
      })
    return rolesPromise
  }

  const displayName = (id: string) => names.get(id)?.name ?? id

  return { search, resolve, loadRoles, roles, available, displayName }
}
