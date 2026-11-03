<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useApprovalClient } from '../client'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import HandoverPanel from '../features/monitor/HandoverPanel.vue'
import MonitorInstances from '../features/monitor/MonitorInstances.vue'
import MonitorStatistics from '../features/monitor/MonitorStatistics.vue'

const { api: approvalApi } = useApprovalClient()

/** 流程监控（仅管理员）：全部流程（搜索、终止）、统计、离职交接。 */
type Tab = 'instances' | 'statistics' | 'handover'
const tabPermissions = { instances: 'approval.instance.read-all', statistics: 'approval.statistics.read', handover: 'approval.task.reassign-bulk' }
const tabs = computed(() => (Object.keys(tabPermissions) as Tab[]).filter(key => can(tabPermissions[key])))

const route = useRoute()
const router = useRouter()
const { me, can } = useApprovalUser()

const tab = computed<Tab>({
  get: () => (tabs.value.includes(route.query.tab as Tab) ? (route.query.tab as Tab) : tabs.value[0] ?? 'instances'),
  set: (value) => router.replace({ query: { ...route.query, tab: value } }),
})

const flows = useAsyncData((signal) => approvalApi.flows(signal), { initial: [], notifyError: false })
</script>

<template>
  <el-alert v-if="me && !tabs.length" type="warning" :closable="false" show-icon title="没有流程监控权限" />
  <el-card v-else-if="tabs.length" shadow="never">
    <template #header>流程监控</template>
    <el-tabs v-model="tab">
      <el-tab-pane v-if="can(tabPermissions.instances)" label="全部流程" name="instances">
        <MonitorInstances :flows="flows.data.value ?? []" />
      </el-tab-pane>
      <el-tab-pane v-if="can(tabPermissions.statistics)" label="统计" name="statistics" lazy>
        <MonitorStatistics :flows="flows.data.value ?? []" />
      </el-tab-pane>
      <el-tab-pane v-if="can(tabPermissions.handover)" label="离职交接" name="handover" lazy>
        <HandoverPanel />
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>
