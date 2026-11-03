<script setup lang="ts">
import type { ApprovalDiagnosticReport } from '../../contracts/api'
import { formatDateTime } from '../../utils/labels'
defineProps<{ report: ApprovalDiagnosticReport | null; loading: boolean }>()
defineEmits<{ refresh: [] }>()
const labels: Record<string, string> = { unavailable_assignee: '没有合格处理人', invalid_definition: '定义无法读取', missing_node: '历史节点缺失' }
</script>
<template>
  <el-card v-loading="loading" shadow="never">
    <template #header><span>通用诊断</span><el-button text @click="$emit('refresh')">刷新</el-button></template>
    <template v-if="report">
      <el-alert :type="report.ready ? 'success' : 'error'" :closable="false" :title="report.ready ? '数据库与迁移已就绪' : '数据库或迁移尚未就绪'" />
      <el-descriptions v-if="report.dataAvailable" :column="3" border>
        <el-descriptions-item label="运行中实例">{{ report.runningInstances }}</el-descriptions-item>
        <el-descriptions-item label="待投递 / 失败">{{ report.pendingOutbox }} / {{ report.failedOutbox }}</el-descriptions-item>
        <el-descriptions-item label="最早积压">{{ report.oldestPendingAt ? formatDateTime(report.oldestPendingAt) : '-' }}</el-descriptions-item>
        <el-descriptions-item label="过期租约">{{ report.expiredLeases }}</el-descriptions-item>
        <el-descriptions-item label="超期任务">{{ report.overdueTasks }}</el-descriptions-item>
        <el-descriptions-item label="占用缺失 / 不一致">{{ report.missingBusinessSlots }} / {{ report.staleBusinessSlots }}</el-descriptions-item>
        <el-descriptions-item label="待删除附件">{{ report.pendingFileDeletions }}</el-descriptions-item>
      </el-descriptions>
      <el-alert v-else type="warning" :closable="false" title="统计数据暂不可用；不将未知状态当成零积压" />
      <p v-if="report.assignmentSampleTruncated">处理人检查只覆盖本次样本，请结合业务列表继续核查。</p>
      <el-table :data="report.findings" size="small"><el-table-column label="问题"><template #default="{ row }">{{ labels[row.code] ?? row.code }}</template></el-table-column><el-table-column prop="resourceId" label="资源 ID" /></el-table>
      <el-table :data="report.heartbeats" size="small"><el-table-column prop="worker" label="后台任务" /><el-table-column label="最近完成"><template #default="{ row }">{{ row.completedAt ? formatDateTime(row.completedAt) : '尚未完成' }}</template></el-table-column><el-table-column label="结果"><template #default="{ row }">{{ row.succeeded === null ? '执行中' : row.succeeded ? '成功' : '失败' }}</template></el-table-column></el-table>
    </template>
  </el-card>
</template>
