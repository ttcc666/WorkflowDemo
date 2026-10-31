<script setup lang="ts">
import type { TodoItem } from '../../api/types'
import { assigneeText, dueText, formatDateTime, taskKindLabels } from '../../utils/labels'

defineProps<{ items: TodoItem[]; loading: boolean }>()
const emit = defineEmits<{ open: [instanceId: string] }>()
</script>

<template>
  <el-table v-loading="loading" :data="items" row-key="taskId" empty-text="暂无待办" @row-click="(row: TodoItem) => emit('open', row.instanceId)">
    <el-table-column label="标题" min-width="240">
      <template #default="{ row }">
        <el-link type="primary" underline="never">{{ row.title || row.businessKey }}</el-link>
        <el-tag v-if="taskKindLabels[row.kind as TodoItem['kind']]" size="small" type="warning" effect="plain" class="kind-tag">
          {{ taskKindLabels[row.kind as TodoItem['kind']] }}
        </el-tag>
      </template>
    </el-table-column>
    <el-table-column prop="definitionName" label="流程" width="140" show-overflow-tooltip />
    <el-table-column prop="nodeName" label="当前节点" width="140" />
    <el-table-column label="分配给" width="180">
      <template #default="{ row }">{{ assigneeText(row.assigneeKind, row.assigneeValue) }}</template>
    </el-table-column>
    <el-table-column prop="initiatorId" label="申请人" width="110" />
    <el-table-column label="到达时间" width="170">
      <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
    </el-table-column>
    <el-table-column label="处理时限" width="150">
      <template #default="{ row }">
        <template v-if="row.dueAt">
          <el-tag :type="dueText(row.dueAt).overdue ? 'danger' : 'info'" effect="plain" :title="`截止 ${formatDateTime(row.dueAt)}`">
            {{ dueText(row.dueAt).text }}
          </el-tag>
        </template>
        <span v-else class="muted">不限</span>
      </template>
    </el-table-column>
  </el-table>
</template>

<style scoped>
.kind-tag {
  margin-left: 6px;
}

.muted {
  color: #c0c4cc;
}
</style>
