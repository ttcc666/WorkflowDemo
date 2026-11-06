<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import ActionJsonEditor from './ActionJsonEditor.vue'
import ActionMappingRuleEditor from './ActionMappingRuleEditor.vue'
import { useDesignerForm } from './designerContext'
import { newMappingRule, type MappingRule } from './actionMappingModel'

const props = defineProps<{ value: unknown; outputs: string[] }>()
const emit = defineEmits<{ update: [rules: unknown[]] }>()
const form = useDesignerForm()
const rules = computed(() => Array.isArray(props.value) ? props.value as MappingRule[] : [])
const groups = computed(() => [
  { name: '表单', paths: form.value.fields.filter(field => field.key).flatMap(field => [
    `form.${field.key}`, ...(field.columns?.map(column => `form.${field.key}[0].${column.key}`) ?? []),
  ]) },
  { name: '动作输出', paths: props.outputs.map(output => `outputs.${output}`) },
  { name: '业务上下文', paths: ['business.type', 'business.key', 'initiator.id', 'instance.id', 'instance.title'] },
])
const paths = computed(() => groups.value.flatMap(group => group.paths))
const advanced = shallowRef(false)
const fieldList = 'workflow-mapping-fields'

function update(index: number, value: MappingRule) {
  emit('update', rules.value.map((rule, i) => i === index ? value : rule))
}
</script>

<template>
  <div class="mapping">
    <div class="mapping__sources" aria-label="源字段">
      <span>源字段</span>
      <details v-for="group in groups" :key="group.name" open>
        <summary>{{ group.name }}</summary>
        <button v-for="path in group.paths" :key="path" type="button" draggable="true" :title="path"
          @dragstart="event => event.dataTransfer?.setData('text/plain', path)">{{ path }}</button>
      </details>
    </div>
    <div class="mapping__rules" aria-label="目标字段">
      <datalist :id="fieldList"><option v-for="path in paths" :key="path" :value="path" /></datalist>
      <ActionMappingRuleEditor v-for="(rule, index) in rules" :key="index" :rule="rule" :paths="paths" :field-list="fieldList"
        @update="value => update(index, value)" @remove="emit('update', rules.filter((_, i) => i !== index))" />
      <el-button size="small" :disabled="rules.length >= 64" @click="emit('update', [...rules, newMappingRule()])">添加目标字段</el-button>
    </div>
    <el-checkbox v-model="advanced" class="mapping__advanced">查看映射规则 JSON</el-checkbox>
    <ActionJsonEditor v-if="advanced" class="mapping__advanced" kind="array" label="映射规则 JSON" :value="rules"
      @update="value => emit('update', value as unknown[])" />
  </div>
</template>

<style scoped>
.mapping { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 2fr); gap: 6px; }
.mapping__sources, .mapping__rules { display: grid; align-content: start; gap: 4px; min-width: 0; }
.mapping__sources button { max-width: 100%; overflow: hidden; text-overflow: ellipsis; text-align: left; cursor: grab; }
.mapping__advanced { grid-column: 1 / -1; }
</style>
