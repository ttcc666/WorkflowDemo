<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { approvalApi } from '../api/approval'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import HandoverPanel from '../features/monitor/HandoverPanel.vue'
import MonitorInstances from '../features/monitor/MonitorInstances.vue'
import MonitorStatistics from '../features/monitor/MonitorStatistics.vue'

/** 流程监控（仅管理员）：全部流程（搜索、终止）、统计、离职交接。 */
type Tab = 'instances' | 'statistics' | 'handover'
const tabs: Tab[] = ['instances', 'statistics', 'handover']

const route = useRoute()
const router = useRouter()
const { me, isAdmin } = useApprovalUser()

const tab = computed<Tab>({
  get: () => (tabs.includes(route.query.tab as Tab) ? (route.query.tab as Tab) : 'instances'),
  set: (value) => router.replace({ query: { ...route.query, tab: value } }),
})

const flows = useAsyncData((signal) => approvalApi.flows(signal), { initial: [], notifyError: false })
</script>

<template>
  <el-alert v-if="me && !isAdmin" type="warning" :closable="false" show-icon title="流程监控仅管理员可用" />
  <el-card v-else-if="isAdmin" shadow="never">
    <template #header>流程监控</template>
    <el-tabs v-model="tab">
      <el-tab-pane label="全部流程" name="instances">
        <MonitorInstances :flows="flows.data.value ?? []" />
      </el-tab-pane>
      <el-tab-pane label="统计" name="statistics" lazy>
        <MonitorStatistics :flows="flows.data.value ?? []" />
      </el-tab-pane>
      <el-tab-pane label="离职交接" name="handover" lazy>
        <HandoverPanel />
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>
