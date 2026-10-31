<script setup lang="ts">
import type { ApprovalDefinition } from '../../api/types'
import { formatDateTime } from '../../utils/labels'

defineProps<{ definitions: ApprovalDefinition[]; loading: boolean }>()
const emit = defineEmits<{ design: [key: string]; versions: [definition: ApprovalDefinition] }>()

const nodeCount = (d: ApprovalDefinition) => {
  try {
    return Object.keys(JSON.parse(d.definitionJson).nodes ?? {}).length
  } catch {
    return '-'
  }
}

// el-table 插槽中的 row 类型为通用对象，这里显式转换
const def = (row: unknown) => row as ApprovalDefinition
</script>

<template>
  <el-table v-loading="loading" :data="definitions" row-key="id" empty-text="暂无流程">
    <el-table-column prop="key" label="流程标识" width="200">
      <template #default="{ row }"><code>{{ row.key }}</code></template>
    </el-table-column>
    <el-table-column prop="name" label="流程名称" min-width="180" />
    <el-table-column label="最新版本" width="100">
      <template #default="{ row }"><el-tag size="small" effect="plain">v{{ row.version }}</el-tag></template>
    </el-table-column>
    <el-table-column label="节点数" width="90">
      <template #default="{ row }">{{ nodeCount(def(row)) }}</template>
    </el-table-column>
    <el-table-column label="发布时间" width="170">
      <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
    </el-table-column>
    <el-table-column label="操作" width="160" fixed="right">
      <template #default="{ row }">
        <el-button link type="primary" @click="emit('design', row.key)">设计</el-button>
        <el-button link @click="emit('versions', def(row))">版本历史</el-button>
      </template>
    </el-table-column>
  </el-table>
</template>
