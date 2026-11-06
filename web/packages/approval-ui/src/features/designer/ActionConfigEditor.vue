<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import type { ApprovalMeta } from '@ui/contracts/api'
import type { ActionStep, ApproverRule } from '@ui/contracts/flow'
import ActionJsonEditor from './ActionJsonEditor.vue'
import ActionMappingEditor from './ActionMappingEditor.vue'
import ApproverRulesEditor from './ApproverRulesEditor.vue'

const ActionScriptEditor = defineAsyncComponent(() => import('./ActionScriptEditor.vue'))

type Field = { type?: string; title?: string; description?: string; enum?: string[]; default?: unknown; minimum?: number; maximum?: number }
type SqlQuery = { dataSource: string; queryId: string; parameters: Record<string, string>; maxRows: number; timeoutSeconds: number }
type Schema = { properties?: Record<string, Field>; required?: string[]; queries?: SqlQuery[] }
const props = defineProps<{ step: ActionStep; schema: unknown; meta: ApprovalMeta; roles: string[]; availableOutputs: string[] }>()
const emit = defineEmits<{ update: [config: Record<string, unknown>] }>()
const schema = computed(() => props.schema as Schema | null | undefined)
const fields = computed(() => Object.entries(schema.value?.properties ?? {}).map(([key, field]) =>
  [key, key === 'queryId' && schema.value?.queries && props.step.config.dataSource
    ? { ...field, enum: schema.value.queries.filter(q => q.dataSource === props.step.config.dataSource).map(q => q.queryId) }
    : field] as const))
const sqlQuery = computed(() => schema.value?.queries?.find(q =>
  q.dataSource === props.step.config.dataSource && q.queryId === props.step.config.queryId))
const labels: Record<string, string> = {
  endpoint: '宿主目标', method: 'HTTP 方法', timeoutSeconds: '超时（秒）', query: '查询参数', headers: '请求头',
  body: 'JSON 请求体', form: '表单请求体', dataSource: '数据源', queryId: '查询 ID', parameters: '查询参数',
}
const config = computed(() => props.step.config)
function set(key: string, value: unknown) {
  const next = { ...config.value }
  if (value === undefined) delete next[key]
  else next[key] = value
  if (key === 'dataSource' && value !== config.value.dataSource) {
    delete next.queryId
    delete next.parameters
  }
  emit('update', next)
}
</script>

<template>
  <ActionScriptEditor v-if="step.type === 'JavaScript'" :script="String(config.script ?? '')"
    :outputs="availableOutputs" @update="script => set('script', script)" />
  <ActionMappingEditor v-else-if="step.type === 'Mapping'" :value="config.mappings" :outputs="availableOutputs"
    @update="mappings => set('mappings', mappings)" />
  <template v-else-if="step.type === 'Notification'">
    <el-form-item label="接收人">
      <ApproverRulesEditor :model-value="(config.recipients ?? []) as ApproverRule[]" :types="meta.recipientTypes"
        :roles="roles" default-type="Initiator" @update:model-value="value => set('recipients', value)" />
    </el-form-item>
    <el-form-item label="渠道">
      <el-select multiple :model-value="(config.channels ?? []) as string[]" @update:model-value="value => set('channels', value)">
        <el-option v-for="channel in meta.channels" :key="channel.name" :label="channel.displayName" :value="channel.name!" />
      </el-select>
    </el-form-item>
    <el-form-item label="消息标题"><el-input :model-value="String(config.title ?? '')" @update:model-value="value => set('title', value)" /></el-form-item>
    <el-form-item label="消息内容">
      <el-input type="textarea" :model-value="String(config.content ?? '')" placeholder="例如：订单 {{business.key}} 已审批"
        @update:model-value="value => set('content', value)" />
    </el-form-item>
  </template>
  <template v-else-if="fields.length">
    <template v-for="[key, field] in fields" :key="key">
      <ActionJsonEditor v-if="field.type === 'object' || field.type === 'array'" :kind="field.type"
        :label="field.title ?? labels[key] ?? key" :value="config[key]" :clearable="!schema?.required?.includes(key)"
        @update="value => set(key, value)" @clear="set(key, undefined)" />
      <el-form-item v-else :label="field.title ?? labels[key] ?? key" :required="schema?.required?.includes(key)">
        <el-select v-if="field.enum" clearable :model-value="String(config[key] ?? field.default ?? '')"
          @update:model-value="value => set(key, value || undefined)">
          <el-option v-for="option in field.enum" :key="option" :label="option" :value="option" />
        </el-select>
        <el-input-number v-else-if="field.type === 'integer' || field.type === 'number'"
          :model-value="(config[key] ?? field.default) as number | undefined" :min="field.minimum" :max="field.maximum"
          @change="value => set(key, value)" />
        <el-checkbox v-else-if="field.type === 'boolean'" :model-value="(config[key] ?? field.default) === true"
          @update:model-value="value => set(key, value)" />
        <el-input v-else :model-value="String(config[key] ?? field.default ?? '')" @update:model-value="value => set(key, value)" />
        <small v-if="field.description">{{ field.description }}</small>
      </el-form-item>
    </template>
    <small v-if="step.type === 'Http'">写请求提交后投递。凭证由宿主配置。参数和请求体可引用 <code v-text="'{{form.field}}'" />。</small>
    <small v-if="step.type === 'Sql'">数据源和查询由宿主维护，参数值可引用表单或上游输出。</small>
    <small v-if="sqlQuery">最多 {{ sqlQuery.maxRows }} 行 · {{ sqlQuery.timeoutSeconds }} 秒 ·
      {{ Object.entries(sqlQuery.parameters).map(([name, type]) => `${name} (${type})`).join('、') || '无参数' }}</small>
  </template>
  <ActionJsonEditor v-else kind="object" label="动作配置 JSON" :value="config"
    @update="value => emit('update', value as Record<string, unknown>)" />
</template>
