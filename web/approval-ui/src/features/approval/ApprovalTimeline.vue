<script setup lang="ts">
import { computed } from 'vue'
import type { ApprovalLog, ApprovalTask } from '@/api/types'
import { actionLabels, assigneeText, dueText, formatDateTime, taskStatusLabels } from '@/utils/labels'

const props = defineProps<{ logs: ApprovalLog[]; tasks: ApprovalTask[] }>()

type TimelineType = 'primary' | 'success' | 'warning' | 'danger' | 'info'
const actionTypes: Partial<Record<ApprovalLog['action'], TimelineType>> = {
  Submit: 'primary',
  Approve: 'success',
  AutoApprove: 'success',
  Complete: 'success',
  Reject: 'danger',
  Withdraw: 'info',
  Transfer: 'warning',
  Action: 'primary',
  Remind: 'warning',
  TimeoutApprove: 'success',
  TimeoutReject: 'danger',
}

const openTasks = computed(() => props.tasks.filter((t) => t.status === 'Pending' || t.status === 'Waiting'))
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
      <div v-if="log.comment" class="timeline-comment">{{ log.comment }}</div>
    </el-timeline-item>

    <el-timeline-item v-for="task in openTasks" :key="task.id" type="warning" hollow placement="top" timestamp="待处理">
      <div class="timeline-title">
        <strong>{{ task.nodeName }}</strong>
        <el-tag size="small" :type="taskStatusLabels[task.status].type">{{ taskStatusLabels[task.status].text }}</el-tag>
        <span class="timeline-operator">{{ assigneeText(task.assigneeKind, task.assigneeValue) }}</span>
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
</style>
