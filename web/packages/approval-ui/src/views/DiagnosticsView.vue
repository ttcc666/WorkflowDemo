<script setup lang="ts">
import { useApprovalClient } from '@ui/client'
import { useApprovalUser } from '@ui/composables/useApprovalUser'
import { useAsyncData } from '@ui/composables/useAsyncData'
import DiagnosticsPanel from '@ui/features/diagnostics/DiagnosticsPanel.vue'
import { approvalRouteNames } from '@ui/routes'
import { useRouter } from 'vue-router'
import { errorMessage } from '@ui/api/http'
const router = useRouter()
const { api } = useApprovalClient()
const { can } = useApprovalUser()
const diagnostics = useAsyncData(signal => can('approval.diagnostics.read') ? api.diagnostics(signal) : Promise.resolve(null), {
  watch: [() => can('approval.diagnostics.read')],
})
</script>
<template>
  <el-result v-if="!can('approval.diagnostics.read')" icon="warning" title="没有运行诊断权限" sub-title="请切换到有权限的账号，或联系管理员授权。" />
  <DiagnosticsPanel v-else :report="diagnostics.data.value ?? null" :loading="diagnostics.loading.value" :error="diagnostics.error.value ? errorMessage(diagnostics.error.value) : ''" @refresh="diagnostics.reload">
    <template #operations>
      <el-button v-if="can('approval.outbox.read')" @click="router.push({ name: approvalRouteNames.outbox, query: { status: 'Failed' } })">处理失败投递</el-button>
      <el-button v-if="can('approval.instance.read-all')" @click="router.push({ name: approvalRouteNames.monitor, query: { tab: 'instances', m_overdueOnly: '1' } })">查看超期流程</el-button>
    </template>
    <template #finding-actions="{ finding }">
      <router-link v-if="finding.instanceId && can('approval.instance.read-all')" :to="{ name: approvalRouteNames.detail, params: { id: finding.instanceId } }">查看实例与可用安全操作</router-link>
      <span v-else>需有资源读取权限的人员定位</span>
    </template>
  </DiagnosticsPanel>
</template>
