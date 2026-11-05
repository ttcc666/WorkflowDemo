<script setup lang="ts">
import type { DefinitionGovernanceSummary } from '@ui/contracts/api'
import { formatDateTime } from '@ui/utils/labels'

defineProps<{ definitions: DefinitionGovernanceSummary[]; loading: boolean; canDesign?: boolean }>()
const emit = defineEmits<{ design: [key: string]; versions: [definition: DefinitionGovernanceSummary] }>()


// el-table 插槽中的 row 类型为通用对象，这里显式转换
const def = (row: unknown) => row as DefinitionGovernanceSummary
</script>

<template>
  <el-table v-loading="loading" :data="definitions" row-key="key" empty-text="暂无流程">
    <el-table-column prop="key" label="流程标识" width="200">
      <template #default="{ row }"><code>{{ row.key }}</code></template>
    </el-table-column>
    <el-table-column prop="name" label="流程名称" min-width="180" />
    <el-table-column label="分类" width="150"><template #default="{ row }">{{ row.category || '未分类' }}</template></el-table-column>
    <el-table-column label="最新版本" width="100">
      <template #default="{ row }"><el-tag size="small" effect="plain">{{ row.lastVersion ? `v${row.lastVersion}` : '未发布' }}</el-tag></template>
    </el-table-column>
    <el-table-column label="状态" width="150">
      <template #default="{ row }">{{ row.isEnabled ? '已启用' : '已停用' }}{{ row.hasDraftChanges ? ' · 草稿待发布' : '' }}</template>
    </el-table-column>
    <el-table-column label="治理更新时间" width="170">
      <template #default="{ row }">{{ row.updatedAt ? formatDateTime(row.updatedAt) : '历史版本' }}</template>
    </el-table-column>
    <el-table-column label="操作" width="160" fixed="right">
      <template #default="{ row }">
        <el-button v-if="canDesign && row.capabilities['approval.definition.design']" link type="primary" @click="emit('design', row.key)">设计</el-button>
        <el-button link @click="emit('versions', def(row))">版本与治理</el-button>
      </template>
    </el-table-column>
  </el-table>
</template>
