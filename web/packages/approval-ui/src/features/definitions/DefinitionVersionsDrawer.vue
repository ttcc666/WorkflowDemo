<script setup lang="ts">
import { useApprovalClient } from '../../client'
import { useAsyncData } from '../../composables/useAsyncData'
import { formatDateTime } from '../../utils/labels'
import DefinitionGovernancePanel from './DefinitionGovernancePanel.vue'

const { api: approvalApi } = useApprovalClient()

const props = defineProps<{ flowKey: string; canDesign?: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ editFrom: [definitionId: string]; changed: [] }>()

const { data: versions, loading } = useAsyncData((signal) => approvalApi.versions(props.flowKey, signal), {
  watch: [() => props.flowKey],
  initial: [],
})
</script>

<template>
  <el-drawer v-model="visible" :title="`版本与治理 · ${flowKey}`" size="720px">
    <DefinitionGovernancePanel :flow-key="flowKey" @changed="emit('changed')" />
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
          <el-button v-if="canDesign" link type="primary" @click="emit('editFrom', row.id)">基于此版本编辑</el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-drawer>
</template>
