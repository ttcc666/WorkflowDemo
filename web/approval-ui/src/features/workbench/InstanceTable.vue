<script setup lang="ts">
import type { ApprovalInstance } from '@/api/types'
import { businessTypeLabels, formatDateTime, instanceStatusLabels } from '@/utils/labels'

defineProps<{ items: ApprovalInstance[]; loading: boolean; emptyText: string }>()
const emit = defineEmits<{ open: [instanceId: string] }>()
</script>

<template>
  <el-table v-loading="loading" :data="items" row-key="id" :empty-text="emptyText" @row-click="(row: ApprovalInstance) => emit('open', row.id)">
    <el-table-column label="标题" min-width="240">
      <template #default="{ row }">
        <el-link type="primary" underline="never">{{ row.title || row.businessKey }}</el-link>
      </template>
    </el-table-column>
    <el-table-column label="业务类型" width="120">
      <template #default="{ row }">{{ businessTypeLabels[row.businessType] ?? row.businessType }}</template>
    </el-table-column>
    <el-table-column label="状态" width="100">
      <template #default="{ row }">
        <el-tag :type="instanceStatusLabels[row.status as ApprovalInstance['status']].type" size="small">
          {{ instanceStatusLabels[row.status as ApprovalInstance['status']].text }}
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
