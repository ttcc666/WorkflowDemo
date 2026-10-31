<script setup lang="ts">
import type { InstanceListItem } from '../../api/types'
import { formatDateTime, instanceStatusLabels } from '../../utils/labels'

defineProps<{ items: InstanceListItem[]; loading: boolean; emptyText: string }>()
const emit = defineEmits<{ open: [instanceId: string] }>()
</script>

<template>
  <el-table v-loading="loading" :data="items" row-key="id" :empty-text="emptyText" @row-click="(row: InstanceListItem) => emit('open', row.id)">
    <el-table-column label="标题" min-width="240">
      <template #default="{ row }">
        <el-link type="primary" underline="never">{{ row.title || row.businessKey }}</el-link>
      </template>
    </el-table-column>
    <el-table-column prop="definitionName" label="流程" width="140" show-overflow-tooltip />
    <el-table-column label="状态" width="100">
      <template #default="{ row }">
        <el-tag :type="instanceStatusLabels[row.status as InstanceListItem['status']].type" size="small">
          {{ instanceStatusLabels[row.status as InstanceListItem['status']].text }}
        </el-tag>
      </template>
    </el-table-column>
    <el-table-column prop="initiatorId" label="申请人" width="110" />
    <el-table-column label="发起时间" width="170">
      <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
    </el-table-column>
    <el-table-column label="完成时间" width="170">
      <template #default="{ row }">{{ formatDateTime(row.completedAt) }}</template>
    </el-table-column>
  </el-table>
</template>
