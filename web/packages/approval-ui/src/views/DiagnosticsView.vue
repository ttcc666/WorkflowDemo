<script setup lang="ts">
import { useApprovalClient } from '../client'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import DiagnosticsPanel from '../features/diagnostics/DiagnosticsPanel.vue'
const { api } = useApprovalClient()
const { can } = useApprovalUser()
const diagnostics = useAsyncData(signal => can('approval.diagnostics.read') ? api.diagnostics(signal) : Promise.resolve(null), {
  watch: [() => can('approval.diagnostics.read')],
})
</script>
<template>
  <DiagnosticsPanel :report="diagnostics.data.value ?? null" :loading="diagnostics.loading.value" @refresh="diagnostics.reload" />
</template>
