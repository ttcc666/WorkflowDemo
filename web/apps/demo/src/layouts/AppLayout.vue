<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { MessageBell, useApprovalUser } from '@approval/ui'
import UserSwitcher from '@/components/UserSwitcher.vue'

const route = useRoute()

// 审批页面来自 @approval/ui（createApprovalRoutes），证书管理为示例业务；流程定义、流程监控、投递记录仅管理员可见
const { formsEnabled, can, canViewDefinitions } = useApprovalUser()
const menus = computed(() => [
  { path: '/workbench', title: '审批工作台' },
  ...(formsEnabled.value && can('approval.instance.start') ? [{ path: '/start', title: '发起申请' }] : []),
  { path: '/certificates', title: '证书管理' },
  ...(canViewDefinitions.value ? [{ path: '/definitions', title: '流程定义' }] : []),
  ...(['approval.instance.read-all', 'approval.statistics.read', 'approval.task.reassign-bulk'].some(can) ? [{ path: '/monitor', title: '流程监控' }] : []),
  ...(can('approval.outbox.read') ? [{ path: '/outbox', title: '投递记录' }] : []),
  ...(can('approval.diagnostics.read') ? [{ path: '/diagnostics', title: '运行诊断' }] : []),
])

// 详情页、设计器归属到对应菜单高亮
const activeMenu = computed(() => {
  if (route.path.startsWith('/designer')) return '/definitions'
  if (route.path.startsWith('/approvals')) return '/workbench'
  if (route.path.startsWith('/start')) return '/start'
  return route.path
})
</script>

<template>
  <el-container class="layout">
    <el-header class="layout__header">
      <div class="layout__brand">审批中心</div>
      <el-menu :default-active="activeMenu" mode="horizontal" router :ellipsis="false" class="layout__menu">
        <el-menu-item v-for="m in menus" :key="m.path" :index="m.path">{{ m.title }}</el-menu-item>
      </el-menu>
      <MessageBell />
      <UserSwitcher />
    </el-header>
    <el-main class="layout__main">
      <!-- 依赖当前用户的页面各自监听用户变化并刷新；不按用户重建页面，避免设计器中切换到 admin 发布时丢失修改 -->
      <RouterView />
    </el-main>
  </el-container>
</template>

<style scoped>
.layout {
  min-height: 100vh;
}

.layout__header {
  display: flex;
  align-items: center;
  gap: 24px;
  border-bottom: 1px solid #ebeef5;
  background: #fff;
}

.layout__brand {
  color: #303133;
  font-size: 18px;
  font-weight: 600;
}

.layout__menu {
  flex: 1;
  border-bottom: none;
}

.layout__main {
  padding: 16px 20px;
}
</style>
