<script setup lang="ts">
import { shallowRef } from 'vue'
import { ElMessage } from 'element-plus'
import { useApprovalClient } from '../client'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import { errorMessage } from '../api/http'
import DiagnosticsPanel from '../features/maintenance/DiagnosticsPanel.vue'
import MaintenancePanel from '../features/maintenance/MaintenancePanel.vue'
const { api } = useApprovalClient(); const { can } = useApprovalUser()
const diagnostics = useAsyncData(signal => can('approval.diagnostics.read') ? api.diagnostics(signal) : Promise.resolve(null), { watch: [() => can('approval.diagnostics.read')] })
const page = shallowRef(1)
const archives = useAsyncData(signal => can('approval.archive.read') ? api.archives(page.value, signal) : Promise.resolve(null), { watch: [page, () => can('approval.archive.read')] })
async function exportArchive(id: string) {
  try { const data = await api.exportArchive(id); const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })); const link = document.createElement('a'); link.href = url; link.download = `approval-archive-${id}.json`; link.click(); URL.revokeObjectURL(url) }
  catch (error) { ElMessage.error(errorMessage(error)) }
}
</script>
<template>
  <DiagnosticsPanel v-if="can('approval.diagnostics.read')" :report="diagnostics.data.value ?? null" :loading="diagnostics.loading.value" @refresh="diagnostics.reload()" />
  <el-alert v-if="diagnostics.error.value" type="error" :closable="false" :title="errorMessage(diagnostics.error.value)" />
  <MaintenancePanel v-if="can('approval.archive.read')" :can-execute="can('approval.archive.execute')" :can-purge="can('approval.archive.purge')" @changed="archives.reload()" />
  <el-card v-if="can('approval.archive.read')" shadow="never">
    <template #header>历史归档 <el-button text @click="archives.reload()">刷新</el-button></template>
    <el-alert v-if="archives.error.value" type="error" :closable="false" :title="errorMessage(archives.error.value)" />
    <el-table :data="archives.data.value?.items ?? []"><el-table-column prop="target" label="类型" /><el-table-column prop="resourceId" label="原资源 ID" /><el-table-column prop="definitionKey" label="流程" /><el-table-column prop="businessType" label="业务" /><el-table-column label="详情"><template #default="{ row }">{{ row.payloadExpiredAt ? '详情保留期已结束' : '可检索' }} <el-button v-if="can('approval.archive.read-sensitive') && !row.payloadExpiredAt" link @click="exportArchive(row.id)">导出已授权详情</el-button></template></el-table-column></el-table>
    <el-pagination v-if="archives.data.value" v-model:current-page="page" :page-size="archives.data.value.pageSize" :total="archives.data.value.total" layout="prev, pager, next" />
  </el-card>
</template>
