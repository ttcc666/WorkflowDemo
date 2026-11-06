<script setup lang="ts">
import { inject, shallowRef } from 'vue'
import type { ApprovalMeta } from '@ui/contracts/api'
import type { ActionMode, ActionStep } from '@ui/contracts/flow'
import ActionConfigEditor from './ActionConfigEditor.vue'
import { simulationPreviewKey } from './designerContext'

const props = defineProps<{ nodeId: string; steps: ActionStep[]; upstreamActions: ActionStep[]; meta: ApprovalMeta; roles: string[] }>()
const emit = defineEmits<{ update: [steps: ActionStep[]] }>()
const preview = inject(simulationPreviewKey, undefined)
const dragged = shallowRef<string>()
const selected = shallowRef<string>()
const descriptor = (step: ActionStep) => props.meta.actions?.find(item => item.type === step.type)

function nextId() {
  let index = 1
  while (props.steps.some(step => step.id === `step${index}`)) index++
  return `step${index}`
}

function changeType(step: ActionStep, type: string) {
  const meta = props.meta.actions?.find(item => item.type === type)
  if (!meta) return
  const mode = type !== 'Http' && meta.supportedModes.includes('Sync') ? 'Sync' : 'Async'
  const schema = meta.configSchema as { properties?: Record<string, { default?: unknown }> } | null
  const defaults = Object.fromEntries(Object.entries(schema?.properties ?? {})
    .filter(([, field]) => field.default !== undefined).map(([name, field]) => [name, field.default]))
  const config: Record<string, unknown> = type === 'JavaScript' ? {
    script: 'function execute(context) {\n  return { amount: context.form.amount };\n}',
  } : type === 'Mapping' ? { mappings: [] } : type === 'Notification' ? {
    recipients: [{ type: 'Initiator', value: '' }], channels: ['InApp'], title: '', content: '',
  } : defaults
  return { ...step, type, mode, config, name: meta.displayName ?? type, outputKey: null,
    failurePolicy: mode === 'Async' ? 'Retry' : 'Fail', configVersion: meta.configVersion } satisfies ActionStep
}

function outputPaths(steps: ActionStep[]) {
  return steps.flatMap(step => {
    const key = step.outputKey
    if (!key) return []
    if (step.type === 'Sql') return [key, `${key}.rows`, `${key}.rows[0]`, `${key}.count`]
    if (step.type === 'Http') return [key, `${key}.statusCode`, `${key}.body`]
    return [key]
  })
}

function update(id: string, patch: Partial<ActionStep>) {
  emit('update', props.steps.map(step => step.id === id ? { ...step, ...patch } : step))
}

function add() {
  const first = props.meta.actions?.[0]
  if (!first) return
  const id = nextId()
  const step = changeType({ id, type: '', mode: 'Sync', failurePolicy: 'Fail', configVersion: 1, config: {} }, first.type!)
  if (!step) return
  emit('update', [...props.steps, step])
  selected.value = id
}

function copy(step: ActionStep) {
  const id = nextId()
  emit('update', [...props.steps, { ...step, id, outputKey: null }])
  selected.value = id
}

function move(id: string, target: number) {
  const from = props.steps.findIndex(step => step.id === id)
  if (from < 0 || target < 0 || target >= props.steps.length || from === target) return
  const steps = [...props.steps]
  const [step] = steps.splice(from, 1)
  steps.splice(target, 0, step!)
  emit('update', steps)
}

function drop(target: number) {
  if (dragged.value) move(dragged.value, target)
  dragged.value = undefined
}

function startDrag(event: DragEvent, id: string) {
  dragged.value = id
  event.dataTransfer?.setData('application/x-approval-action-step', id)
}

function setMode(step: ActionStep, mode: ActionMode) {
  update(step.id, { mode, failurePolicy: mode === 'Async' ? 'Retry' : 'Fail',
    outputKey: mode === 'Async' ? null : step.outputKey })
}
</script>

<template>
  <div class="actions-editor">
    <el-alert v-if="!meta.actions?.length" title="当前账号没有动作元数据，或宿主尚未启用动作处理器。" type="info" :closable="false" />
    <div class="actions-editor__toolbar">
      <el-button size="small" :disabled="!meta.actions?.length || steps.length >= 20" @click="add">添加动作</el-button>
      <el-button v-if="preview" size="small" @click="preview(nodeId)">预演动作</el-button>
    </div>
    <section v-for="(step, index) in steps" :key="step.id" class="actions-editor__step"
      @dragover.prevent @drop.prevent="drop(index)">
      <div class="actions-editor__header">
        <button type="button" class="actions-editor__handle" draggable="true" aria-label="拖动排序"
          @dragstart.stop="startDrag($event, step.id)" @dragend="dragged = undefined">⠿</button>
        <el-button text size="small" :aria-expanded="selected === step.id"
          @click="selected = selected === step.id ? undefined : step.id">
          {{ index + 1 }}. {{ step.name || step.type }}
        </el-button>
        <el-tag size="small" :type="step.mode === 'Async' ? 'warning' : 'info'">{{ step.mode === 'Async' ? '异步' : '同步' }}</el-tag>
      </div>
      <div v-if="selected === step.id" class="actions-editor__config">
        <el-alert v-if="!descriptor(step)" type="warning" :closable="false" :title="`未注册的动作类型 ${step.type}`" />
        <el-form-item label="动作类型">
          <el-select :model-value="step.type" @update:model-value="(type: string) => update(step.id, changeType(step, type) ?? {})">
            <el-option v-for="item in meta.actions" :key="item.type" :label="item.displayName" :value="item.type!" />
          </el-select>
        </el-form-item>
        <el-form-item label="名称">
          <el-input :model-value="step.name ?? ''" maxlength="200" @update:model-value="(name: string) => update(step.id, { name })" />
        </el-form-item>
        <el-form-item label="执行模式">
          <el-select :model-value="step.mode" @update:model-value="(mode: ActionMode) => setMode(step, mode)">
            <el-option v-for="mode in descriptor(step)?.supportedModes" :key="mode" :label="mode === 'Sync' ? '同步' : '提交后投递'" :value="mode" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="step.mode === 'Sync' && step.type !== 'Notification'" label="输出变量">
          <el-input :model-value="step.outputKey ?? ''" maxlength="64" placeholder="如 risk" @update:model-value="(key: string) => update(step.id, { outputKey: key || null })" />
        </el-form-item>
        <el-form-item label="失败策略">
          <el-select :model-value="step.failurePolicy" :disabled="step.mode === 'Async'"
            @update:model-value="(failurePolicy: ActionStep['failurePolicy']) => update(step.id, { failurePolicy })">
            <el-option v-if="step.mode === 'Async'" label="Outbox 重试" value="Retry" />
            <el-option v-else label="回滚审批操作" value="Fail" />
            <el-option v-if="step.mode === 'Sync' && descriptor(step)?.allowsContinue" label="记录失败后继续" value="Continue" />
          </el-select>
        </el-form-item>
        <ActionConfigEditor :key="step.type" :step="step" :schema="descriptor(step)?.configSchema" :meta="meta"
          :roles="roles" :available-outputs="outputPaths([...upstreamActions, ...steps.slice(0, index)])"
          @update="config => update(step.id, { config })" />
        <div class="actions-editor__toolbar">
          <el-button size="small" :disabled="index === 0" @click="move(step.id, index - 1)">上移</el-button>
          <el-button size="small" :disabled="index === steps.length - 1" @click="move(step.id, index + 1)">下移</el-button>
          <el-button size="small" :disabled="steps.length >= 20" @click="copy(step)">复制</el-button>
          <el-button size="small" type="danger" text @click="emit('update', steps.filter(item => item.id !== step.id))">删除</el-button>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.actions-editor, .actions-editor__config { display: grid; gap: 8px; }
.actions-editor__toolbar, .actions-editor__header { display: flex; align-items: center; flex-wrap: wrap; gap: 4px; }
.actions-editor__step { border: 1px solid var(--el-border-color); border-radius: 6px; padding: 6px; min-width: 0; }
.actions-editor__handle { cursor: grab; border: 0; background: transparent; color: var(--el-text-color-secondary); }
.actions-editor__config { margin-top: 8px; }
</style>
