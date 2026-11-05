<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import type { ApprovalDiagnosticReport } from '@ui/contracts/api'
import { formatDateTime } from '@ui/utils/labels'
const props = defineProps<{ report: ApprovalDiagnosticReport | null; loading: boolean; error?: string }>()
defineEmits<{ refresh: [] }>()
const labels: Record<string, string> = { unavailable_assignee: '没有合格处理人', invalid_definition: '定义无法读取', missing_node: '历史节点缺失' }
const kind = shallowRef('')
const flow = shallowRef('')
const page = shallowRef(1)
const filtered = computed(() => (props.report?.findings ?? []).filter(item => (!kind.value || item.code === kind.value) && (!flow.value || item.definitionKey === flow.value)))
const findings = computed(() => filtered.value.slice((page.value - 1) * 20, page.value * 20))
const flows = computed(() => [...new Set(props.report?.findings.map(item => item.definitionKey).filter(Boolean))])
watch(filtered, () => { page.value = 1 })
const workers: Record<string, string> = { outbox: '事件与通知投递', timeouts: '审批超时扫描' }
</script>
<template>
  <el-card v-loading="loading" shadow="never">
    <template #header><div class="diagnostics__header"><div><strong>审批异常中心</strong><p>查看运行健康，定位异常并进入授权处理入口。</p></div><el-button :loading="loading" @click="$emit('refresh')">刷新诊断</el-button></div></template>
    <el-alert v-if="error" type="error" :closable="false" :title="error" />
    <template v-if="report">
      <el-alert :type="report.ready ? 'success' : 'error'" :closable="false" :title="report.ready ? '数据库与迁移已就绪' : '数据库或迁移尚未就绪'" />
      <h3>运行健康</h3>
      <el-descriptions :column="1" border class="diagnostics__schema">
        <el-descriptions-item label="数据库迁移">{{ report.schemaManaged ? '由宿主管理迁移' : '未配置迁移快照' }}</el-descriptions-item>
        <el-descriptions-item label="待应用迁移">{{ report.dataAvailable ? report.pendingMigrations : '无法确认' }}</el-descriptions-item>
        <el-descriptions-item label="模型一致性">{{ !report.dataAvailable ? '无法确认' : !report.schemaManaged ? '无模型快照，未检查' : report.modelDrift ? '模型不一致，请检查部署' : '未检测到偏移' }}</el-descriptions-item>
      </el-descriptions>
      <div v-if="report.dataAvailable" class="diagnostics__metrics">
        <article><span>运行中流程</span><strong>{{ report.runningInstances }}</strong></article>
        <article><span>超期任务</span><strong>{{ report.overdueTasks }}</strong></article>
        <article><span>等待投递</span><strong>{{ report.pendingOutbox }}</strong></article>
        <article><span>投递失败</span><strong>{{ report.failedOutbox }}</strong></article>
      </div>
      <el-alert v-else type="warning" :closable="false" title="统计数据暂不可用；不将未知状态当成零积压" />
      <p class="diagnostics__hint">最早投递积压：{{ report.dataAvailable ? report.oldestPendingAt ? formatDateTime(report.oldestPendingAt) : '无积压' : '无法确认' }} · 业务占用缺失 / 不一致：{{ report.dataAvailable ? `${report.missingBusinessSlots} / ${report.staleBusinessSlots}` : '无法确认' }}</p>
      <div class="diagnostics__operations"><slot name="operations" /></div>
      <h3>待处理问题</h3>
      <el-alert v-if="report.assignmentSampleTruncated" type="warning" :closable="false" title="样本已截断：以下不是完整异常清单。筛选和分页仅作用于本次有限样本。" />
      <p class="diagnostics__hint">仅展示本次处理人检查样本。没有发现问题不代表所有运行路径均健康；数据库与业务占用问题需要人工核查。</p>
      <div class="diagnostics__filters">
      <el-select v-model="kind" clearable placeholder="全部异常类型" aria-label="异常类型"><el-option v-for="(label, code) in labels" :key="code" :label="label" :value="code" /></el-select>
      <el-select v-model="flow" clearable placeholder="全部样本流程" aria-label="异常流程"><el-option v-for="key in flows" :key="key!" :label="key!" :value="key!" /></el-select>
      </div>
      <el-table :data="findings" size="small" empty-text="本次筛选范围内未发现处理人异常">
        <el-table-column label="问题"><template #default="{ row }">{{ labels[row.code] ?? row.code }}</template></el-table-column>
        <el-table-column prop="definitionKey" label="流程" min-width="160" show-overflow-tooltip /><el-table-column prop="resourceId" label="资源编号" min-width="170" show-overflow-tooltip />
        <el-table-column prop="taskId" label="任务编号" min-width="170" show-overflow-tooltip />
        <el-table-column label="处理建议" min-width="190"><template #default="{ row }">{{ row.code === 'unavailable_assignee' ? '检查处理人规则，或经授权改派' : '检查绑定的历史定义，人工定位' }}</template></el-table-column>
        <el-table-column label="操作" min-width="180"><template #default="{ row }"><slot name="finding-actions" :finding="row" /></template></el-table-column>
      </el-table>
      <el-pagination v-if="filtered.length" v-model:current-page="page" :total="filtered.length" :page-size="20" layout="total, prev, pager, next" />
      <h3>后台运行</h3>
      <el-table :data="report.heartbeats" size="small" empty-text="尚未记录后台任务运行情况"><el-table-column label="后台任务" min-width="160"><template #default="{ row }">{{ workers[row.worker] ?? row.worker }}</template></el-table-column><el-table-column label="最近完成" min-width="180"><template #default="{ row }">{{ row.completedAt ? formatDateTime(row.completedAt) : '尚未完成' }}</template></el-table-column><el-table-column label="结果" min-width="90"><template #default="{ row }"><el-tag :type="row.succeeded === null ? 'info' : row.succeeded ? 'success' : 'danger'" size="small">{{ row.succeeded === null ? '执行中' : row.succeeded ? '成功' : '失败' }}</el-tag></template></el-table-column></el-table>
    </template>
  </el-card>
</template>

<style scoped>
.diagnostics__header { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.diagnostics__header p, .diagnostics__hint { color: #73767a; font-size: 13px; line-height: 1.7; margin: 8px 0; }
h3 { margin: 24px 0 12px; font-size: 15px; }
.diagnostics__schema { max-width: 720px; }
.diagnostics__metrics { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-top: 16px; }
.diagnostics__metrics article { padding: 16px; border: 1px solid #ebeef5; border-radius: 8px; background: #fafbfc; }
.diagnostics__metrics span { display: block; color: #606266; font-size: 13px; }
.diagnostics__metrics strong { display: block; font-size: 26px; margin-top: 6px; }
.diagnostics__filters, .diagnostics__operations { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
.diagnostics__filters .el-select { width: 240px; max-width: 100%; }
:deep(.el-table__empty-text) { line-height: 1.7; padding: 18px 0; }
@media (max-width: 650px) { .diagnostics__metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); } .diagnostics__filters .el-select { width: 100%; } }
</style>
