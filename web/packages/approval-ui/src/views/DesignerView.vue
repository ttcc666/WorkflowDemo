<script setup lang="ts">
import { computed, onBeforeUnmount, provide, shallowRef, useTemplateRef, watch } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useDefinitionGovernance } from '../features/designer/useDefinitionGovernance'
import { useApprovalClient } from '../client'
import { ApiError, errorMessage } from '../api/http'
import { useAsyncData } from '../composables/useAsyncData'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useDirectory } from '../composables/useDirectory'
import BranchInspector from '../features/designer/BranchInspector.vue'
import DesignerToolbar from '../features/designer/DesignerToolbar.vue'
import DesignerPreview from '../features/designer/DesignerPreview.vue'
import NodeInspector from '../features/designer/NodeInspector.vue'
import NodePalette from '../features/designer/NodePalette.vue'
import NotificationRulesEditor from '../features/designer/NotificationRulesEditor.vue'
import ValidationResult from '../features/designer/ValidationResult.vue'
import { designerFormKey } from '../features/designer/designerContext'
import FlowSettingsPanel from '../features/forms/FlowSettingsPanel.vue'
import FormDesigner from '../features/forms/FormDesigner.vue'
import { useFlowDesigner } from '../features/designer/useFlowDesigner'
import { useDesignerShortcuts } from '../features/designer/useDesignerShortcuts'
import FlowCanvas from '../features/flow/FlowCanvas.vue'
import { newFlowDefinition, notifyEventsFor, type FlowDefinition, type NodeType } from '../features/flow/flowModel'
import type { ApprovalMeta } from '../contracts/api'
import { approvalRouteNames } from '../routes'

const { api: approvalApi, userKey } = useApprovalClient()

const route = useRoute()
const router = useRouter()
const { me, can, canViewDefinitions } = useApprovalUser()
const directory = useDirectory()
void directory.loadRoles()
const designer = useFlowDesigner()
const canvas = useTemplateRef<InstanceType<typeof FlowCanvas>>('canvas')

/** 流程（画布）/ 表单 / 设置 三个页签；画布用 v-show 保持状态 */
const tab = shallowRef<'flow' | 'form' | 'settings'>('flow')
provide(designerFormKey, computed(() => designer.form.value))
const fieldCount = computed(() => designer.form.value.fields.length)

const flowKey = computed(() => route.params.key as string)
const governance = useDefinitionGovernance(() => flowKey.value)
const intent = governance.intent
const saving = shallowRef(false)
const previewVisible = shallowRef(false)
const previewDefinition = shallowRef<FlowDefinition>(newFlowDefinition())
function previewDraft() { previewDefinition.value = designer.buildDefinition(); previewVisible.value = true }
const changeNote = shallowRef('')
const canGovern = (permission: string) => governance.state.value?.capabilities[permission] ?? can(permission)
async function restartPublish() {
  try {
    await ElMessageBox.confirm('请先在版本记录中核对上次发布结果。确认后，将开始一次新的发布。', '重新发布', { type: 'warning' })
    intent.reset(flowKey.value)
  } catch { /* Keep the original intent until the result has been checked. */ }
}
const fromId = computed(() => route.query.from as string | undefined)
const name = shallowRef('')
const version = shallowRef<number | null>(null)

// 加载：?from=<定义ID> 基于历史版本编辑；否则取最新版本；不存在则新建。完整定义仅管理员（及 DefinitionReaders）可读。
// 每次身份变化都重新确认当前企业的权限与定义。
const canLoad = canViewDefinitions
watch(userKey, () => {
  designer.load(newFlowDefinition())
  name.value = ''
  version.value = null
  governance.state.value = null
  changeNote.value = ''
  tab.value = 'flow'
  if (route.query.from) void router.replace({ query: { ...route.query, from: undefined } })
}, { flush: 'sync' })
const loader = useAsyncData(
  async (signal) => {
    if (!canLoad.value) return undefined
    try {
      await governance.load(signal)
      if (!fromId.value && governance.state.value?.draftJson) return { name: governance.state.value.name, version: governance.state.value.lastVersion, definitionJson: governance.state.value.draftJson }
      return fromId.value ? await approvalApi.definitionById(fromId.value, signal) : await approvalApi.latestDefinition(flowKey.value)
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) return null
      throw e
    }
  },
  { watch: [flowKey, fromId, canLoad] },
)

watch(loader.data, (definition) => {
  if (definition === undefined) return
  designer.load(definition ? (JSON.parse(definition.definitionJson) as FlowDefinition) : newFlowDefinition())
  name.value = definition?.name ?? (route.query.name as string) ?? flowKey.value
  version.value = definition?.version ?? null
  canvas.value?.fitView()
})

const emptyMeta: ApprovalMeta = {
  approverTypes: [],
  recipientTypes: [],
  channels: [],
  templateVariables: [],
  defaultTemplates: {},
}
const meta = useAsyncData(() => approvalApi.meta(), { initial: emptyMeta })
const metaValue = computed(() => meta.data.value ?? emptyMeta)
const flowEvents = notifyEventsFor('Flow')

// ───── 画布交互 ─────

function onConnect(source: string, target: string) {
  const error = designer.connect(source, target)
  if (error) ElMessage.warning(error)
}

function addNode(type: NodeType) {
  designer.addNode(type, canvas.value?.viewportCenter() ?? { x: 0, y: 0 })
}

function onDropNode(type: NodeType, center: { x: number; y: number }, edgeId: string | null) {
  designer.dropNode(type, center, edgeId)
}

function onNodeDroppedOnEdge(nodeId: string, edgeId: string) {
  if (!designer.insertOnEdge(nodeId, edgeId)) designer.markChanged()
}

// ───── 撤销 / 重做快捷键（输入框内保留浏览器自带的文本撤销） ─────

useDesignerShortcuts(designer)


function autoLayout() {
  designer.autoLayout()
  canvas.value?.fitView()
}

const selectedEdgeData = computed(() => designer.selectedEdge.value?.data)

// ───── 校验与发布 ─────

const validating = shallowRef(false)
const publishing = shallowRef(false)
async function saveDraft() {
  if (!name.value.trim()) return ElMessage.warning('请填写流程名称')
  saving.value = true
  try {
    await governance.save(name.value.trim(), JSON.stringify(designer.buildDefinition()), fromId.value ?? governance.state.value?.draftSourceDefinitionId ?? governance.state.value?.activeDefinitionId ?? null)
    designer.markSaved()
    ElMessage.success('设计草稿已保存；发起审批仍使用当前选用的发布版本')
  } catch (error) { ElMessage.error(errorMessage(error)) }
  finally { saving.value = false }
}

async function validate() {
  validating.value = true
  try {
    return await designer.validate(can('approval.definition.validate'))
  } catch (e) {
    ElMessage.error(errorMessage(e))
    return { ok: false, definition: null }
  } finally {
    validating.value = false
  }
}

async function publish() {
  const identity = userKey.value
  if (!name.value.trim()) return ElMessage.warning('请填写流程名称')
  const { ok, definition } = await validate()
  if (identity !== userKey.value) return
  if (!ok || !definition) return ElMessage.error('校验未通过，请先修正问题')

  try {
    await ElMessageBox.confirm('发布后新发起的审批将使用新版本，进行中的审批仍按原版本流转。', '发布新版本', { type: 'info' })
    if (identity !== userKey.value) return
  } catch {
    return
  }

  publishing.value = true
  try {
    if (intent.pending.value !== flowKey.value && (!governance.state.value?.draftJson || designer.dirty.value || name.value.trim() !== governance.state.value.name || fromId.value))
      await governance.save(name.value.trim(), JSON.stringify(definition), fromId.value ?? governance.state.value?.draftSourceDefinitionId ?? governance.state.value?.activeDefinitionId ?? null)
    const result = await governance.publish(changeNote.value.trim())
    version.value = result.version
    designer.markSaved()
    ElMessage.success(`已发布 ${result.key} v${result.version}`)
    if (fromId.value) await router.replace({ query: {} })
  } catch (e) {
    ElMessage.error(errorMessage(e))
  } finally {
    publishing.value = false
  }
}

// ───── 未保存提醒 ─────

onBeforeRouteLeave(async () => {
  if (!designer.dirty.value) return true
  try {
    await ElMessageBox.confirm('有未保存的修改，离开后将丢失。确定离开？', '提示', { type: 'warning' })
    return true
  } catch {
    return false
  }
})

const onBeforeUnload = (e: BeforeUnloadEvent) => {
  if (designer.dirty.value) e.preventDefault()
}
window.addEventListener('beforeunload', onBeforeUnload)
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))
</script>

<template>
  <el-alert v-if="me && (!canViewDefinitions || !can('approval.definition.design'))" type="warning" :closable="false" show-icon title="没有流程设计或定义读取权限" />
  <div v-else v-loading="loader.loading.value" class="designer">
    <el-card shadow="never" body-class="designer__toolbar-body">
      <DesignerToolbar
        v-model:name="name"
        :flow-key="flowKey"
        :version="version"
        :dirty="designer.dirty.value"
        :validating="validating"
        :publishing="publishing"
        :saving="saving"
        :can-save="canGovern('approval.definition.design')"
        :can-publish="canGovern('approval.definition.publish')"
        :can-validate="can('approval.definition.validate')"
        :can-undo="designer.canUndo.value"
        :can-redo="designer.canRedo.value"
        @back="router.push({ name: approvalRouteNames.definitions })"
        @undo="designer.undo"
        @redo="designer.redo"
        @layout="autoLayout"
        @validate="validate"
        @publish="publish"
        @save="saveDraft"
        @preview="previewDraft"
      />
    </el-card>

    <ValidationResult :errors="designer.errors.value" :warnings="designer.warnings.value" />
    <DesignerPreview v-model="previewVisible" :definition="previewDefinition" />
    <el-input v-model="changeNote" placeholder="发布变更说明（最多 1000 字符）" maxlength="1000" />
    <el-alert v-if="governance.state.value && !governance.state.value.isEnabled" type="warning" :closable="false" title="此流程已停用；保存或发布不会自动启用" />
    <el-alert v-if="governance.state.value?.hasDraftChanges" type="info" :closable="false" title="设计草稿已保存，尚未发布；新申请继续使用选用的发布版本" />
    <el-button v-if="intent.pending.value" :disabled="publishing" @click="restartPublish">确认旧版本后重新发布</el-button>

    <el-tabs v-model="tab" class="designer__tabs">
      <el-tab-pane label="流程" name="flow" />
      <el-tab-pane :label="fieldCount ? `表单（${fieldCount}）` : '表单'" name="form" />
      <el-tab-pane label="发起设置" name="settings" />
    </el-tabs>

    <div v-if="tab === 'form'" class="designer__form">
      <FormDesigner :model-value="designer.form.value" @update:model-value="designer.updateForm" />
    </div>

    <el-card v-if="tab === 'settings'" shadow="never" class="designer__settings">
      <FlowSettingsPanel :model-value="designer.settings.value" :form="designer.form.value" :roles="directory.roles.value"
        @update:model-value="designer.updateSettings" />
    </el-card>

    <div v-show="tab === 'flow'" class="designer__body">
      <aside class="designer__side">
        <NodePalette @add="addNode" />
      </aside>

      <FlowCanvas
        ref="canvas"
        v-model:nodes="designer.nodes.value"
        v-model:edges="designer.edges.value"
        class="designer__canvas"
        @connect="onConnect"
        @node-click="designer.selectNode"
        @edge-click="designer.selectEdge"
        @pane-click="designer.clearSelection"
        @changed="designer.markChanged()"
        @drop-node="onDropNode"
        @node-dropped-on-edge="onNodeDroppedOnEdge"
      />

      <aside class="designer__side designer__side--right">
        <NodeInspector
          v-if="designer.selectedNode.value?.data"
          :key="designer.selectedNode.value.id"
          :node-id="designer.selectedNode.value.id"
          :data="designer.selectedNode.value.data"
          :meta="metaValue"
          :roles="directory.roles.value"
          @update="(patch) => designer.updateNode(designer.selectedNode.value!.id, patch)"
          @remove="designer.removeNode(designer.selectedNode.value!.id)"
        />
        <BranchInspector
          v-else-if="designer.selectedEdge.value && selectedEdgeData"
          :key="designer.selectedEdge.value.id"
          :data="selectedEdgeData"
          :source-name="designer.nodeName(designer.selectedEdge.value.source)"
          :target-name="designer.nodeName(designer.selectedEdge.value.target)"
          @update="(patch) => designer.updateBranch(designer.selectedEdge.value!.id, patch)"
          @remove="designer.removeEdge(designer.selectedEdge.value!.id)"
        />
        <div v-else-if="designer.selectedEdge.value" class="designer__empty">
          普通连线没有可设置的属性，可按 Delete 删除或
          <el-button link type="danger" @click="designer.removeEdge(designer.selectedEdge.value!.id)">删除连线</el-button>
        </div>
        <div v-else>
          <div class="designer__panel-title">流程级通知</div>
          <div class="designer__empty">对所有审批节点生效（如统一的“到达节点”提醒），以及流程结束。节点上可再单独配置。</div>
          <NotificationRulesEditor
            :model-value="designer.flowNotifications.value"
            :events="flowEvents"
            :channels="metaValue.channels"
            :recipient-types="metaValue.recipientTypes"
            :variables="metaValue.templateVariables"
            :roles="directory.roles.value"
            @update:model-value="designer.updateFlowNotifications"
          />
        </div>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.designer {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: calc(100vh - 100px);
}

:deep(.designer__toolbar-body) {
  padding: 10px 16px;
}

.designer__tabs :deep(.el-tabs__header) {
  margin: 0;
}

.designer__form {
  flex: 1;
  min-height: 0;
}

.designer__settings {
  flex: 1;
  overflow-y: auto;
}

.designer__body {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}

.designer__side {
  width: 220px;
  flex: none;
  overflow-y: auto;
  padding: 12px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fff;
}

.designer__side--right {
  width: 340px;
}

.designer__canvas {
  flex: 1;
  min-width: 0;
}

.designer__panel-title {
  margin-bottom: 6px;
  font-weight: 600;
}

.designer__empty {
  color: #909399;
  font-size: 13px;
  line-height: 1.8;
}
</style>
