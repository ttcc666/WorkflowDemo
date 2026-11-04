<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useApprovalClient } from '../../client'
import type { FlowSummary } from '../../contracts/api'
import { useAsyncData } from '../../composables/useAsyncData'
import { localDayStart } from '../../composables/usePagedList'
import { assigneeText, durationText, formatDateTime } from '../../utils/labels'

const { api: approvalApi } = useApprovalClient()

/**
 * 统计：一行筛选（发起日期、流程）作用于下方全部内容；指标卡 + 各流程状态分布 + 节点处理时长 + 待办积压。
 * 表格即数据的完整视图，表内横条只是同一数值的长度提示（单一序列，统一用主色）。
 */
defineProps<{ flows: FlowSummary[] }>()

const toDate = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const daysAgo = (days: number) => {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return toDate(d)
}

const range = shallowRef<[string, string]>([daysAgo(29), toDate(new Date())])
const definitionKey = shallowRef<string>('')
const shortcuts = [
  { text: '最近 7 天', value: () => [new Date(daysAgo(6)), new Date()] },
  { text: '最近 30 天', value: () => [new Date(daysAgo(29)), new Date()] },
  { text: '最近 90 天', value: () => [new Date(daysAgo(89)), new Date()] },
  { text: '本月', value: () => [new Date(new Date().getFullYear(), new Date().getMonth(), 1), new Date()] },
]

const stats = useAsyncData(
  (signal) =>
    approvalApi.statistics(
      { from: localDayStart(range.value[0]), to: localDayStart(range.value[1], 1), definitionKey: definitionKey.value || undefined },
      signal,
    ),
  { watch: [range, definitionKey], resetOnReload: true },
)

const data = computed(() => stats.data.value)
const maxFlowTotal = computed(() => Math.max(1, ...(data.value?.flows ?? []).map((f) => f.total)))
const maxNodeMinutes = computed(() => Math.max(1, ...(data.value?.nodes ?? []).map((n) => n.averageMinutes)))
const maxBacklog = computed(() => Math.max(1, ...(data.value?.backlog ?? []).map((b) => b.count)))
const percent = (value: number, max: number) => `${Math.max(2, Math.round((value / max) * 100))}%`
const compact = (value: number) => value.toLocaleString('zh-CN')
</script>

<template>
  <div class="stats">
    <div class="stats__filters">
      <el-date-picker v-model="range" type="daterange" value-format="YYYY-MM-DD" :shortcuts="shortcuts" :clearable="false"
        start-placeholder="发起日期" end-placeholder="结束日期" />
      <el-select v-model="definitionKey" placeholder="全部流程" clearable filterable class="stats__flow">
        <el-option v-for="f in flows" :key="f.key" :label="f.name" :value="f.key" />
      </el-select>
      <span class="stats__hint">按发起日期统计；耗时只计已结束的流程</span>
    </div>

    <el-alert v-if="data?.truncated" type="warning" :closable="false" show-icon class="stats__alert"
      title="流程数超过统计上限，只统计了最早的 10 万条，请缩小时间范围" />

    <div v-if="data" class="stats__body" :class="{ 'stats__body--stale': stats.loading.value }">
      <div class="kpis">
        <div class="kpi">
          <div class="kpi__label">流程总数</div>
          <div class="kpi__value">{{ compact(data.summary.total) }}</div>
          <div class="kpi__note">审批中 {{ data.summary.running }} · 已通过 {{ data.summary.approved }} · 已驳回 {{ data.summary.rejected }}</div>
        </div>
        <div class="kpi">
          <div class="kpi__label">通过率</div>
          <div class="kpi__value">{{ data.approvalRate === null ? '-' : `${data.approvalRate}%` }}</div>
          <div class="kpi__note">通过 /（通过 + 驳回 + 终止），撤回不计入</div>
        </div>
        <div class="kpi">
          <div class="kpi__label">平均耗时</div>
          <div class="kpi__value">{{ durationText(data.summary.averageMinutes) }}</div>
          <div class="kpi__note">90% 的流程在 {{ durationText(data.summary.p90Minutes) }} 内结束</div>
        </div>
        <div class="kpi">
          <div class="kpi__label">超时任务</div>
          <div class="kpi__value">{{ compact(data.overdueTasks) }}</div>
          <div class="kpi__note">超过时限才处理，或当前已超时的待办</div>
        </div>
      </div>

      <section class="stats__section">
        <div class="stats__title">各流程</div>
        <el-table :data="data.flows" size="small" empty-text="该时间范围内没有流程">
          <el-table-column prop="definitionName" label="流程" min-width="140" show-overflow-tooltip />
          <el-table-column label="总数" min-width="160">
            <template #default="{ row }">
              <div class="bar-cell" :title="`${row.definitionName}：${row.total} 条`">
                <span class="bar-cell__value">{{ row.total }}</span>
                <span class="bar-cell__track"><span class="bar-cell__bar" :style="{ width: percent(row.total, maxFlowTotal) }" /></span>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="running" label="审批中" width="72" align="right" />
          <el-table-column prop="returned" label="待修改" width="72" align="right" />
          <el-table-column prop="approved" label="已通过" width="72" align="right" />
          <el-table-column prop="rejected" label="已驳回" width="72" align="right" />
          <el-table-column prop="withdrawn" label="已撤回" width="72" align="right" />
          <el-table-column prop="terminated" label="已终止" width="72" align="right" />
          <el-table-column label="平均耗时" width="100" align="right">
            <template #default="{ row }">{{ durationText(row.averageMinutes) }}</template>
          </el-table-column>
          <el-table-column label="P90 耗时" width="100" align="right">
            <template #default="{ row }">{{ durationText(row.p90Minutes) }}</template>
          </el-table-column>
        </el-table>
      </section>

      <div class="stats__grid">
        <section class="stats__section">
          <div class="stats__title">节点平均处理时长</div>
          <el-table :data="data.nodes" size="small" max-height="360" empty-text="暂无已处理的任务">
            <el-table-column label="节点" min-width="150" show-overflow-tooltip>
              <template #default="{ row }">{{ row.nodeName }}<span class="muted"> · {{ row.definitionName }}</span></template>
            </el-table-column>
            <el-table-column label="平均处理时长" min-width="170">
              <template #default="{ row }">
                <div class="bar-cell" :title="`${row.nodeName}：平均 ${durationText(row.averageMinutes)}，处理 ${row.handled} 次`">
                  <span class="bar-cell__value">{{ durationText(row.averageMinutes) }}</span>
                  <span class="bar-cell__track"><span class="bar-cell__bar" :style="{ width: percent(row.averageMinutes, maxNodeMinutes) }" /></span>
                </div>
              </template>
            </el-table-column>
            <el-table-column prop="handled" label="处理次数" width="80" align="right" />
            <el-table-column prop="overdue" label="超时" width="60" align="right" />
          </el-table>
        </section>

        <section class="stats__section">
          <div class="stats__title">待办积压（当前，前 10）</div>
          <el-table :data="data.backlog" size="small" empty-text="当前没有待办">
            <el-table-column label="处理人" min-width="120" show-overflow-tooltip>
              <template #default="{ row }">{{ assigneeText(row.assigneeKind, row.assigneeValue) }}</template>
            </el-table-column>
            <el-table-column label="待办数" min-width="150">
              <template #default="{ row }">
                <div class="bar-cell" :title="`${row.assigneeValue}：${row.count} 条待办`">
                  <span class="bar-cell__value">{{ row.count }}</span>
                  <span class="bar-cell__track"><span class="bar-cell__bar" :style="{ width: percent(row.count, maxBacklog) }" /></span>
                </div>
              </template>
            </el-table-column>
            <el-table-column prop="overdue" label="已超时" width="70" align="right" />
            <el-table-column label="最早到达" width="140">
              <template #default="{ row }">{{ formatDateTime(row.oldestCreatedAt) }}</template>
            </el-table-column>
          </el-table>
        </section>
      </div>
    </div>
    <el-skeleton v-else-if="stats.loading.value" :rows="6" animated />
  </div>
</template>

<style scoped>
.stats {
  --viz-bar: #2a78d6;
  --viz-text-primary: #0b0b0b;
  --viz-text-secondary: #52514e;
  --viz-text-muted: #898781;
  --viz-border: rgb(11 11 11 / 10%);
}

:global(html.dark) .stats {
  --viz-bar: #3987e5;
  --viz-text-primary: #ffffff;
  --viz-text-secondary: #c3c2b7;
  --viz-border: rgb(255 255 255 / 10%);
}

.stats__filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
}

.stats__flow {
  width: 180px;
}

.stats__hint,
.muted {
  color: var(--viz-text-muted);
  font-size: 12px;
}

.stats__alert {
  margin-bottom: 12px;
}

.stats__body {
  transition: opacity 0.15s;
}

/* 重新加载时保留上一次的结果并降低不透明度，避免布局跳动 */
.stats__body--stale {
  opacity: 0.55;
}

.kpis {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 12px;
  margin-bottom: 20px;
}

.kpi {
  padding: 14px 16px;
  border: 1px solid var(--viz-border);
  border-radius: 8px;
}

.kpi__label {
  color: var(--viz-text-secondary);
  font-size: 13px;
}

.kpi__value {
  margin-top: 6px;
  color: var(--viz-text-primary);
  font-size: 28px;
  font-weight: 600;
  line-height: 1.2;
}

.kpi__note {
  margin-top: 6px;
  color: var(--viz-text-muted);
  font-size: 12px;
}

.stats__section + .stats__section,
.stats__grid {
  margin-top: 20px;
}

.stats__grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(420px, 1fr));
  gap: 20px;
}

.stats__grid .stats__section + .stats__section {
  margin-top: 0;
}

.stats__title {
  margin-bottom: 8px;
  color: var(--viz-text-primary);
  font-weight: 600;
}

.bar-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.bar-cell__value {
  flex: none;
  min-width: 56px;
  color: var(--viz-text-primary);
  font-variant-numeric: tabular-nums;
  text-align: right;
}

.bar-cell__track {
  flex: 1;
  height: 8px;
}

/* 单一序列的长度提示：从左侧基线生长，数据端 4px 圆角 */
.bar-cell__bar {
  display: block;
  height: 100%;
  border-radius: 0 4px 4px 0;
  background: var(--viz-bar);
}
</style>
