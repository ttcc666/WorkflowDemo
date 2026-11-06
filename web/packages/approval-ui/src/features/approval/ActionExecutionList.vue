<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import type { ApprovalLog } from '@ui/contracts/api'
import { useApprovalUser } from '@ui/composables/useApprovalUser'
import { approvalRouteNames } from '@ui/routes'
import { formatDateTime } from '@ui/utils/labels'

const props = defineProps<{ logs: ApprovalLog[]; instanceId: string }>()
const { can } = useApprovalUser()
const node = shallowRef('')
const type = shallowRef('')
const status = shallowRef('')
type Execution = {
  id: string
  type: string
  mode: string
  status: 'Succeeded' | 'Failed' | 'Queued'
  executionId?: string
  outboxId?: string
  durationMs?: number
  failure?: string
}

const executions = computed(() => props.logs.flatMap(log => {
  if (log.action !== 'ActionExecuted' || !log.details) return []
  try {
    const detail = JSON.parse(log.details) as Execution
    return [{ ...detail, logId: log.id, nodeId: log.nodeId ?? '', nodeName: log.nodeName, createdAt: log.createdAt }]
  } catch { return [] }
}))
const rows = computed(() => executions.value.filter(row =>
  (!node.value || row.nodeId === node.value) && (!type.value || row.type === type.value) &&
  (!status.value || row.status === status.value)))
const statuses = { Succeeded: '执行成功', Failed: '执行失败', Queued: '已入队' }
</script>

<template>
  <el-card v-if="executions.length" shadow="never" header="动作执行记录">
    <div class="execution-filters">
      <el-select v-model="node" clearable placeholder="节点" aria-label="按节点筛选">
        <el-option v-for="value in new Set(executions.map(row => row.nodeId))" :key="value" :label="value" :value="value" />
      </el-select>
      <el-select v-model="type" clearable placeholder="动作类型" aria-label="按动作类型筛选">
        <el-option v-for="value in new Set(executions.map(row => row.type))" :key="value" :label="value" :value="value" />
      </el-select>
      <el-select v-model="status" clearable placeholder="状态" aria-label="按动作状态筛选">
        <el-option v-for="(label, value) in statuses" :key="value" :label="label" :value="value" />
      </el-select>
    </div>
    <ul class="execution-list">
      <li v-for="row in rows" :key="row.logId">
        <strong>{{ row.nodeName || row.nodeId }} · {{ row.id }} · {{ row.type }}</strong>
        <div>{{ statuses[row.status] }} · {{ formatDateTime(row.createdAt) }}<span v-if="row.durationMs != null"> · {{ row.durationMs.toFixed(1) }} ms</span></div>
        <div v-if="row.failure">{{ row.failure }}</div>
        <code v-if="row.outboxId || row.executionId">{{ row.outboxId || row.executionId }}</code>
      </li>
    </ul>
    <router-link v-if="can('approval.outbox.read')" :to="{ name: approvalRouteNames.outbox, query: { instanceId } }">查看此实例的 Outbox</router-link>
  </el-card>
</template>

<style scoped>
.execution-filters { display: flex; gap: 6px; }
.execution-list { display: grid; gap: 8px; padding-inline-start: 16px; overflow-wrap: anywhere; }
</style>
