<script setup lang="ts">
import { computed } from 'vue'
import type { ApprovalInstance } from '@/api/types'
import { businessTypeLabels, formatDateTime, instanceStatusLabels } from '@/utils/labels'

const props = defineProps<{ instance: ApprovalInstance }>()

const status = computed(() => instanceStatusLabels[props.instance.status])
const currentNodes = computed(() =>
  [...new Set(props.instance.tasks.filter((t) => t.status === 'Pending').map((t) => t.nodeName))].join('、') || '-',
)
</script>

<template>
  <el-descriptions :column="3" border>
    <el-descriptions-item label="标题" :span="2">{{ instance.title || instance.businessKey }}</el-descriptions-item>
    <el-descriptions-item label="状态">
      <el-tag :type="status.type">{{ status.text }}</el-tag>
    </el-descriptions-item>
    <el-descriptions-item label="业务类型">{{ businessTypeLabels[instance.businessType] ?? instance.businessType }}</el-descriptions-item>
    <el-descriptions-item label="申请人">{{ instance.initiatorId }}</el-descriptions-item>
    <el-descriptions-item label="当前节点">{{ instance.status === 'Running' ? currentNodes : '-' }}</el-descriptions-item>
    <el-descriptions-item label="流程">{{ instance.definitionKey }} v{{ instance.definitionVersion }}</el-descriptions-item>
    <el-descriptions-item label="发起时间">{{ formatDateTime(instance.createdAt) }}</el-descriptions-item>
    <el-descriptions-item label="完成时间">{{ formatDateTime(instance.completedAt) }}</el-descriptions-item>
  </el-descriptions>
</template>
