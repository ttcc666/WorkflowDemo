<script setup lang="ts">
import { useApprovalUser, useAsyncData } from '@approval/ui'
import { approval } from '@/approval'

const { userKey: approvalUserKey, http } = approval

interface Diagnostics {
  pendingApplications: number
  failedDeliveries: number
  dueDeliveries: number
  overdueTasks: number
  unstaffedTasks: { taskId: string; instanceId: string; nodeName: string; problem: string }[]
  truncated: boolean
  database: { ready: boolean; problems: string[] }
}

const { isAdmin } = useApprovalUser()
const { data, loading, error, reload } = useAsyncData((signal) => isAdmin.value
  ? http.get<Diagnostics>('/api/approval/admin/certificates/diagnostics', signal) : Promise.resolve(undefined), {
  watch: [isAdmin, approvalUserKey], notifyError: false, resetOnReload: true,
})
</script>

<template>
  <el-card v-if="isAdmin" shadow="never" class="diagnostics">
    <template #header><div class="header"><span>试点运行检查</span><el-button text @click="reload">刷新</el-button></div></template>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div v-loading="loading">
      <template v-if="data">
        <el-descriptions :column="4" border>
          <el-descriptions-item label="审核中">{{ data.pendingApplications }}</el-descriptions-item>
          <el-descriptions-item label="投递失败">{{ data.failedDeliveries }}</el-descriptions-item>
          <el-descriptions-item label="等待投递">{{ data.dueDeliveries }}</el-descriptions-item>
          <el-descriptions-item label="已超时待办">{{ data.overdueTasks }}</el-descriptions-item>
        </el-descriptions>
        <el-alert v-if="!data.database.ready" type="error" :title="data.database.problems.join('；')" :closable="false" />
        <el-alert v-if="data.truncated" type="info" title="岗位检查展示最早的 100 条待办，请结合流程监控处理积压。" :closable="false" />
        <el-table v-if="data.unstaffedTasks.length" :data="data.unstaffedTasks" size="small">
          <el-table-column label="需要处理的节点" width="220"><template #default="{ row }">
            <router-link :to="{ name: 'approval-detail', params: { id: row.instanceId } }">{{ row.nodeName }}</router-link>
          </template></el-table-column>
          <el-table-column prop="problem" label="问题" />
        </el-table>
      </template>
    </div>
  </el-card>
</template>

<style scoped>
.diagnostics { margin-top: 16px; }
.header { display: flex; align-items: center; justify-content: space-between; }
</style>
