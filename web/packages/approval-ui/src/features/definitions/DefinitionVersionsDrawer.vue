<script setup lang="ts">
import { useApprovalClient } from '../../client'
import { useAsyncData } from '../../composables/useAsyncData'
import { formatDateTime } from '../../utils/labels'

const { api: approvalApi } = useApprovalClient()

const props = defineProps<{ flowKey: string }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ editFrom: [definitionId: string] }>()

const { data: versions, loading } = useAsyncData((signal) => approvalApi.versions(props.flowKey, signal), {
  watch: [() => props.flowKey],
  initial: [],
})
</script>

<template>
  <el-drawer v-model="visible" :title="`版本历史 · ${flowKey}`" size="460px">
    <el-table v-loading="loading" :data="versions" size="small">
      <el-table-column label="版本" width="70">
        <template #default="{ row }">v{{ row.version }}</template>
      </el-table-column>
      <el-table-column prop="name" label="名称" />
      <el-table-column label="发布时间" width="150">
        <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="" width="110">
        <template #default="{ row }">
          <el-button link type="primary" @click="emit('editFrom', row.id)">基于此版本编辑</el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-drawer>
</template>
