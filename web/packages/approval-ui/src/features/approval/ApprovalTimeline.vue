<script setup lang="ts">
import { computed, shallowReactive } from 'vue'
import type { ApprovalLog, ApprovalTask, FormFieldChange } from '../../contracts/api'
import { sumKey, type FormField, type FormSchema } from '../forms/formModel'
import { actionLabels, assigneeText, dueText, formatDateTime, taskKindLabels, taskStatusLabels } from '../../utils/labels'
import FieldChangeValue from './FieldChangeValue.vue'

const props = defineProps<{
  logs: ApprovalLog[]
  tasks: ApprovalTask[]
  /** 表单结构：修改表单的轨迹按字段类型显示修改前后的值 */
  form?: FormSchema
  /** 管理员：未完成的任务旁显示“改派” */
  canReassign?: boolean
}>()
const emit = defineEmits<{ reassign: [task: ApprovalTask] }>()

type TimelineType = 'primary' | 'success' | 'warning' | 'danger' | 'info'
const actionTypes: Partial<Record<ApprovalLog['action'], TimelineType>> = {
  Submit: 'primary',
  Approve: 'success',
  AutoApprove: 'success',
  Complete: 'success',
  Reject: 'danger',
  Withdraw: 'info',
  Transfer: 'warning',
  Remind: 'warning',
  TimeoutApprove: 'success',
  TimeoutReject: 'danger',
  Return: 'warning',
  AddSign: 'primary',
  Resubmit: 'primary',
  Terminate: 'danger',
  Reassign: 'warning',
  FormEdit: 'primary',
}

const openTasks = computed(() => props.tasks.filter((t) => t.status === 'Pending' || t.status === 'Waiting' || t.status === 'Suspended'))

/** 修改表单的字段差异（已按当前用户去掉隐藏字段） */
function changesOf(log: ApprovalLog): FormFieldChange[] {
  if (log.action !== 'FormEdit' || !log.details) return []
  try {
    const parsed: unknown = JSON.parse(log.details)
    return Array.isArray(parsed) ? (parsed as FormFieldChange[]) : []
  } catch {
    return []
  }
}

/** 字段定义：明细合计字段按合计列的类型显示（如金额保留两位小数），其他不在表单中的字段按值的类型显示 */
function fieldOf(change: FormFieldChange): FormField {
  const key = change.field.toLowerCase()
  const fields = props.form?.fields ?? []
  const field = fields.find((f) => f.key.toLowerCase() === key)
  if (field) return field
  for (const table of fields.filter((f) => f.type === 'Table')) {
    const column = table.columns?.find((c) => sumKey(table.key, c.key).toLowerCase() === key)
    if (column) return { ...column, key: change.field, label: change.label }
  }
  const numeric = typeof change.before === 'number' || typeof change.after === 'number'
  return { key: change.field, label: change.label, type: numeric ? 'Number' : 'Text' }
}

const expanded = shallowReactive(new Set<string>())
const toggle = (id: string) => (expanded.has(id) ? expanded.delete(id) : expanded.add(id))
</script>

<template>
  <el-timeline class="approval-timeline">
    <el-timeline-item
      v-for="log in logs"
      :key="log.id"
      :timestamp="formatDateTime(log.createdAt)"
      :type="actionTypes[log.action] ?? 'info'"
      placement="top"
    >
      <div class="timeline-title">
        <strong>{{ actionLabels[log.action] }}</strong>
        <span v-if="log.nodeName && log.action !== 'Submit'" class="timeline-node">· {{ log.nodeName }}</span>
        <span class="timeline-operator">{{ log.operatorId === 'system' ? '系统' : log.operatorId }}</span>
      </div>
      <div v-if="log.comment" class="timeline-comment">
        {{ log.comment }}
        <el-button v-if="changesOf(log).length" link type="primary" size="small" class="timeline-toggle" @click="toggle(log.id)">
          {{ expanded.has(log.id) ? '收起' : '查看修改' }}
        </el-button>
        <table v-if="expanded.has(log.id)" class="timeline-changes">
          <thead>
            <tr><th>字段</th><th>修改前</th><th>修改后</th></tr>
          </thead>
          <tbody>
            <tr v-for="change in changesOf(log)" :key="change.field">
              <td>{{ change.label }}</td>
              <td><FieldChangeValue :field="fieldOf(change)" :value="change.before" /></td>
              <td><FieldChangeValue :field="fieldOf(change)" :value="change.after" /></td>
            </tr>
          </tbody>
        </table>
      </div>
    </el-timeline-item>

    <el-timeline-item v-for="task in openTasks" :key="task.id" type="warning" hollow placement="top" timestamp="待处理">
      <div class="timeline-title">
        <strong>{{ task.nodeName }}</strong>
        <el-tag size="small" :type="taskStatusLabels[task.status].type">{{ taskStatusLabels[task.status].text }}</el-tag>
        <el-tag v-if="taskKindLabels[task.kind]" size="small" type="warning" effect="plain">{{ taskKindLabels[task.kind] }}</el-tag>
        <span class="timeline-operator">{{ assigneeText(task.assigneeKind, task.assigneeValue) }}</span>
        <el-button v-if="canReassign && task.capabilities?.['approval.task.reassign'] === true && task.kind !== 'Resubmit'" link type="primary" size="small" @click="emit('reassign', task)">改派</el-button>
      </div>
      <div v-if="task.dueAt && task.status === 'Pending'" class="timeline-comment">
        截止 {{ formatDateTime(task.dueAt) }}（{{ dueText(task.dueAt).text }}<template v-if="task.remindCount">，已提醒 {{ task.remindCount }} 次</template>）
      </div>
    </el-timeline-item>
  </el-timeline>
</template>

<style scoped>
.approval-timeline {
  padding-left: 4px;
}

.timeline-title {
  display: flex;
  align-items: center;
  gap: 8px;
}

.timeline-node {
  color: #606266;
}

.timeline-operator {
  margin-left: auto;
  color: #909399;
}

.timeline-comment {
  margin-top: 6px;
  padding: 6px 10px;
  border-radius: 4px;
  background: #f5f7fa;
  color: #606266;
}

.timeline-toggle {
  margin-left: 6px;
  vertical-align: baseline;
}

.timeline-changes {
  width: 100%;
  margin-top: 6px;
  border-collapse: collapse;
  font-size: 12px;
}

.timeline-changes th,
.timeline-changes td {
  padding: 4px 6px;
  border: 1px solid #ebeef5;
  text-align: left;
  vertical-align: top;
}

.timeline-changes th {
  background: #fafafa;
  color: #909399;
  font-weight: normal;
}
</style>
