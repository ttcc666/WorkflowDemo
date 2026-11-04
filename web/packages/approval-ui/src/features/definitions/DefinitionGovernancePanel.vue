<script setup lang="ts">
import { shallowRef } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useApprovalClient } from '../../client'
import { useAsyncData } from '../../composables/useAsyncData'
import { useCommandIntent } from '../../composables/useCommandIntent'
import { errorMessage } from '../../api/http'
import { formatDateTime } from '../../utils/labels'

const props = defineProps<{ flowKey: string }>()
const emit = defineEmits<{ changed: [] }>()
const { api } = useApprovalClient()
const intent = useCommandIntent('definition-governance')
const state = useAsyncData(signal => api.definitionGovernance(props.flowKey, signal), { watch: [() => props.flowKey] })
const versions = useAsyncData(signal => api.versions(props.flowKey, signal), { watch: [() => props.flowKey], initial: [] })
const page = shallowRef(1)
const audit = useAsyncData(signal => api.definitionAudit(props.flowKey, page.value, signal), { watch: [() => props.flowKey, page] })
const note = shallowRef('')
const selectedVersion = shallowRef<string | null>(null)
const busy = shallowRef(false)
const can = (permission: string) => state.data.value?.capabilities[permission] === true
async function govern(kind: 'enabled' | 'select') {
  if (!state.data.value) return
  const current = state.data.value
  const body: { enabled: boolean; expectedRevision: number; changeNote: string } | { definitionId: string; expectedRevision: number; changeNote: string } = kind === 'enabled' ? { enabled: !current.isEnabled, expectedRevision: current.revision, changeNote: note.value.trim() }
    : { definitionId: selectedVersion.value!, expectedRevision: current.revision, changeNote: note.value.trim() }
  busy.value = true
  try {
    await ElMessageBox.confirm(kind === 'enabled' ? (current.isEnabled ? '停用只阻止新发起，在途审批继续使用原版本。' : '启用后，新申请将使用选用版本。')
      : '新申请将使用此历史版本，在途审批不变。', '确认流程治理操作', { type: 'warning' })
    await intent.run(`${kind}:${props.flowKey}`, body, (payload, id) => 'enabled' in payload
      ? api.setDefinitionEnabled(props.flowKey, payload, id) : api.selectDefinitionVersion(props.flowKey, payload, id))
    await Promise.all([state.reload(), audit.reload()]); emit('changed'); ElMessage.success('流程状态已更新')
  } catch (error) { if (error !== 'cancel' && error !== 'close') ElMessage.error(errorMessage(error)) }
  finally { busy.value = false }
}
async function resetIntent() {
  try {
    await ElMessageBox.confirm('请先核对当前状态与审计记录，确认旧操作结果后再开始新操作。', '确认旧结果', { type: 'warning' })
    await Promise.all([state.reload(), audit.reload()]);
    if (intent.pending.value) intent.reset(intent.pending.value)
  } catch { /* Preserve the original key when verification was cancelled or failed. */ }
}
const actionLabels: Record<string, string> = { DraftSaved: '保存草稿', Published: '发布', Enabled: '启用', Disabled: '停用', VersionSelected: '选用历史版本' }
const actionLabel = (action: string) => actionLabels[action] ?? action
</script>

<template>
  <section v-loading="state.loading.value">
    <el-alert v-if="state.error.value" type="error" :closable="false" :title="errorMessage(state.error.value)" />
    <template v-if="state.data.value">
      <p>当前{{ state.data.value.isEnabled ? '已启用' : '已停用' }} · 最新发布 v{{ state.data.value.lastVersion }}</p>
      <el-input v-model="note" placeholder="治理变更说明" maxlength="1000" />
      <el-button v-if="can('approval.definition.disable') && state.data.value.lastVersion" :disabled="busy" @click="govern('enabled')">
        {{ state.data.value.isEnabled ? '停用流程' : '启用流程' }}
      </el-button>
      <div v-if="can('approval.definition.publish') && versions.data.value?.length">
        <el-select v-model="selectedVersion" placeholder="选用历史发布版本">
          <el-option v-for="version in versions.data.value" :key="version.id" :value="version.id"
            :label="`v${version.version} · ${version.name}${version.id === state.data.value.activeDefinitionId ? '（当前选用）' : ''}`" />
        </el-select>
        <el-button :disabled="busy || !selectedVersion" @click="govern('select')">选用此版本</el-button>
      </div>
      <el-button v-if="intent.pending.value" :disabled="busy" @click="resetIntent">核对旧结果后开始新操作</el-button>
    </template>
    <p>治理审计</p>
    <el-alert v-if="audit.error.value" type="error" :closable="false" :title="errorMessage(audit.error.value)" />
    <el-table :data="audit.data.value?.items ?? []" size="small">
      <el-table-column label="操作"><template #default="{ row }">{{ actionLabel(row.action) }}</template></el-table-column>
      <el-table-column prop="actorId" label="操作人" />
      <el-table-column prop="changeNote" label="变更说明" />
      <el-table-column label="时间"><template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template></el-table-column>
    </el-table>
    <el-pagination v-if="audit.data.value" v-model:current-page="page" :page-size="audit.data.value.pageSize" :total="audit.data.value.total" layout="prev, pager, next" />
  </section>
</template>
