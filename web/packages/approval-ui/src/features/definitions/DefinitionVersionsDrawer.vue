<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import CommandRecovery from '@ui/components/CommandRecovery.vue'
import { useApprovalClient } from '@ui/client'
import { useAsyncData } from '@ui/composables/useAsyncData'
import { formatDateTime } from '@ui/utils/labels'
import DefinitionGovernancePanel from './DefinitionGovernancePanel.vue'
import DefinitionVersionComparison from './DefinitionVersionComparison.vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useCommandIntent } from '@ui/composables/useCommandIntent'
import { errorMessage } from '@ui/api/http'
import { definitionFileName } from './definitionTransfer'
import type { ApprovalDefinition } from '@ui/contracts/api'

const { api: approvalApi, userKey } = useApprovalClient()

const props = defineProps<{ flowKey: string; canDesign?: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ editFrom: [definitionId: string]; changed: [] }>()
const copyIntent = useCommandIntent('copy-definition')
async function copy() {
  try {
    const target = await ElMessageBox.prompt('输入新流程标识', '复制为新草稿', { inputPattern: /^[A-Za-z0-9._-]{1,100}$/, inputErrorMessage: '请输入有效的流程标识' })
    const body = { targetKey: target.value, name: `${props.flowKey}（副本）` }
    await copyIntent.run(props.flowKey, body, (payload, key) => approvalApi.copyDefinition(props.flowKey, payload, key))
    ElMessage.success('已复制为未发布草稿'); emit('changed')
  } catch (error) { if (error !== 'cancel' && error !== 'close') ElMessage.error(errorMessage(error)) }
}

const { data: versions, loading } = useAsyncData((signal) => approvalApi.versions(props.flowKey, signal), {
  watch: [() => props.flowKey],
  resetOnReload: true,
  initial: [],
})
const comparing = shallowRef(false)
const published = computed(() => versions.value?.filter(version => version.isPublished && version.key === props.flowKey) ?? [])
watch([() => props.flowKey, userKey, visible], () => { comparing.value = false }, { flush: 'sync' })

const exporting = shallowRef<string>()
async function download(version: ApprovalDefinition) {
  if (exporting.value) return
  const owner = userKey.value
  exporting.value = version.id
  try {
    const file = await approvalApi.exportDefinition(version.id)
    if (owner !== userKey.value || !visible.value || version.key !== props.flowKey) return
    const url = URL.createObjectURL(file)
    const link = document.createElement('a')
    link.href = url
    link.download = definitionFileName(version.key, version.version)
    link.click()
    setTimeout(() => URL.revokeObjectURL(url), 0)
  } catch (error) { if (owner === userKey.value) ElMessage.error(errorMessage(error)) }
  finally { exporting.value = undefined }
}
</script>

<template>
  <el-drawer v-model="visible" :title="`版本与治理 · ${flowKey}`" size="720px">
  <CommandRecovery :intent="copyIntent" @recovered="emit('changed')" />
    <DefinitionGovernancePanel :flow-key="flowKey" @changed="emit('changed')" />
    <el-button v-if="canDesign" @click="copy">复制为新流程草稿</el-button>
    <el-button :disabled="published.length < 2" :aria-expanded="comparing" @click="comparing = !comparing">版本对比</el-button>
    <DefinitionVersionComparison v-if="comparing" :versions="published" />
    <el-table v-loading="loading" :data="versions" size="small">
      <el-table-column label="版本" width="70">
        <template #default="{ row }">v{{ row.version }}</template>
      </el-table-column>
      <el-table-column prop="name" label="名称" />
      <el-table-column label="发布时间" width="150">
        <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="" width="175">
        <template #default="{ row }">
          <el-button v-if="canDesign" link type="primary" @click="emit('editFrom', row.id)">基于此版本编辑</el-button>
          <el-button v-if="row.isPublished" link type="primary" :loading="exporting === row.id"
            @click="download(row as ApprovalDefinition)">导出</el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-drawer>
</template>
