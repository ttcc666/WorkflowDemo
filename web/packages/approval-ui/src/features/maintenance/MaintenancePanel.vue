<script setup lang="ts">
import { shallowRef, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useApprovalClient } from '../../client'
import { useAsyncData } from '../../composables/useAsyncData'
import { useCommandIntent } from '../../composables/useCommandIntent'
import { errorMessage } from '../../api/http'
import type { MaintenancePreview, MaintenanceTarget, MaintenanceMode, MaintenanceBatchInfo, PagedResult } from '../../contracts/api'
import { formatDateTime } from '../../utils/labels'
const props = defineProps<{ canExecute: boolean; canPurge: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const { api, userKey } = useApprovalClient()
const intent = useCommandIntent('maintenance')
const target = shallowRef<MaintenanceTarget>('Instances')
const mode = shallowRef<MaintenanceMode>('Archive')
const preview = shallowRef<MaintenancePreview | null>(null)
const busy = shallowRef(false)
const batchId = shallowRef<string | null>(null); const batchPage = shallowRef(1); const batches = shallowRef<PagedResult<MaintenanceBatchInfo> | null>(null)
async function showBatches(id: string, page = 1) { try { batches.value = await api.maintenanceBatches(id, page); batchId.value = id; batchPage.value = page } catch (error) { ElMessage.error(errorMessage(error)) } }
const resourceId = shallowRef(''); const reason = shallowRef('')
const jobs = useAsyncData(signal => api.maintenanceJobs(signal), { initial: [] })
const targets: Array<{ value: MaintenanceTarget; label: string }> = [
  { value: 'Instances', label: '已结束实例' }, { value: 'ReadMessages', label: '已读消息' }, { value: 'SucceededOutbox', label: '成功投递' },
  { value: 'FailedOutbox', label: '失败投递（只归档）' }, { value: 'BusinessDrafts', label: '业务草稿' }, { value: 'DesignDrafts', label: '设计草稿' },
  { value: 'Operations', label: '命令结果（永久保留防重键）' }, { value: 'UnboundFiles', label: '未绑定附件' }, { value: 'ArchivedPayloads', label: '到期归档详情与附件' },
]
watch([target, mode, userKey], () => { preview.value = null; resourceId.value = ''; reason.value = ''; batchId.value = null; batches.value = null })
async function inspect() { busy.value = true; try { preview.value = await api.maintenancePreview(target.value, mode.value) } catch (error) { ElMessage.error(errorMessage(error)) } finally { busy.value = false } }
async function create() {
  const value = preview.value; if (!value?.enabled || !value.configured || !props.canExecute || value.mode === 'Purge' && !props.canPurge) return
  busy.value = true
  try {
    await ElMessageBox.confirm(`按当前保留策略处理候选记录（预览 ${value.eligible} 条）。运行中与保留数据会在执行前再次检查。确认创建任务并执行一批？`, '执行维护', { type: 'warning' })
    const body = { target: value.target, mode: value.mode, policyHash: value.policyHash }
    const job = await intent.run(`${value.target}:${value.mode}`, body, (payload, key) => api.createMaintenanceJob(payload, key))
    await api.runMaintenanceJob(job.id); await jobs.reload(); emit('changed'); ElMessage.success('已执行一批；可查看游标并继续任务')
  } catch (error) { if (error !== 'cancel' && error !== 'close') ElMessage.error(errorMessage(error)) }
  finally { busy.value = false }
}
async function resume(id: string) { busy.value = true; try { await api.runMaintenanceJob(id); await jobs.reload(); emit('changed') } catch (error) { ElMessage.error(errorMessage(error)) } finally { busy.value = false } }
async function hold(held: boolean) {
  if (!resourceId.value || held && !reason.value.trim()) return ElMessage.warning('请填写资源 ID 和保留原因')
  try { await intent.run(`hold:${target.value}:${resourceId.value}`, { target: target.value, resourceId: resourceId.value, held, reason: reason.value.trim() }, (body, key) => api.setRetentionHold(body, key)); preview.value = null; ElMessage.success(held ? '保留标记已设置' : '保留标记已释放') }
  catch (error) { ElMessage.error(errorMessage(error)) }
}
async function reset() { try { await ElMessageBox.confirm('先检查任务记录，确认上次创建结果后再开始新任务。', '确认旧结果'); await jobs.reload(); if (intent.pending.value) intent.reset(intent.pending.value) } catch { /* Keep old key. */ } }
</script>
<template>
  <el-card shadow="never">
    <template #header><span>归档与受控清理</span></template>
    <el-select v-model="target"><el-option v-for="item in targets" :key="item.value" :value="item.value" :label="item.label" /></el-select>
    <el-select v-model="mode"><el-option value="Archive" label="生成归档快照" /><el-option v-if="canPurge" value="Purge" label="按保留期清理" /></el-select>
    <el-button :loading="busy" @click="inspect">只读预览</el-button>
    <template v-if="preview">
      <p>候选 {{ preview.eligible }} 条 · 截止 {{ preview.cutoff ? formatDateTime(preview.cutoff) : '未配置保留期' }}</p>
      <el-alert v-if="!preview.enabled || !preview.configured" type="info" :closable="false" title="策略未启用或未配置；默认不执行维护和删除" />
      <el-button v-if="canExecute" :disabled="busy || !preview.enabled || !preview.configured || preview.eligible === 0" @click="create">确认并执行一批</el-button>
    </template>
    <el-button v-if="intent.pending.value" @click="reset">核对旧结果后重开</el-button>
    <div v-if="canExecute"><el-input v-model="resourceId" placeholder="当前所选类型的资源 ID" /><el-input v-model="reason" placeholder="保留原因" maxlength="1000" /><el-button @click="hold(true)">设置保留</el-button><el-button @click="hold(false)">释放保留</el-button></div>
    <el-button text @click="jobs.reload()">刷新任务</el-button>
    <el-alert v-if="jobs.error.value" type="error" :closable="false" :title="errorMessage(jobs.error.value)" />
    <el-table :data="jobs.data.value ?? []" size="small">
      <el-table-column prop="id" label="任务" /><el-table-column prop="target" label="类型" /><el-table-column prop="status" label="状态" />
      <el-table-column prop="completedBatches" label="已提交批次" /><el-table-column prop="processed" label="已处理" /><el-table-column prop="lastErrorCode" label="错误码" />
      <el-table-column label="操作"><template #default="{ row }"><el-button link @click="showBatches(row.id)">批次记录</el-button><el-button v-if="canExecute && (row.mode !== 'Purge' || canPurge) && row.status !== 'Completed'" :disabled="busy" link @click="resume(row.id)">继续一批</el-button></template></el-table-column>
    </el-table>
  </el-card>
  <el-dialog :model-value="!!batchId" title="已提交批次" @close="batchId = null">
    <el-table :data="batches?.items ?? []"><el-table-column prop="batch" label="批次" /><el-table-column prop="count" label="处理数量" /><el-table-column prop="actorId" label="提交主体" /><el-table-column prop="committedAt" label="提交时间" /></el-table>
    <el-pagination v-if="batches && batchId" :current-page="batchPage" :total="batches.total" :page-size="batches.pageSize" layout="prev, pager, next" @current-change="page => showBatches(batchId!, page)" />
  </el-dialog>
</template>
