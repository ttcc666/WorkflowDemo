<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { mappingTransforms, newMappingRule, type MappingRule } from './actionMappingModel'

const props = defineProps<{ rule: MappingRule; paths: string[]; fieldList?: string; depth?: number }>()
const emit = defineEmits<{ update: [rule: MappingRule]; remove: [] }>()
const error = shallowRef('')
const nested = computed(() => props.rule.transform === 'Array' || props.rule.transform === 'Object')
const depth = computed(() => props.depth ?? 0)
const input = (event: Event) => (event.target as HTMLInputElement).value
const update = (patch: Partial<MappingRule>) => emit('update', { ...props.rule, ...patch })

function transform(value: string) {
  update({
    transform: value, precision: value === 'Number' ? props.rule.precision : undefined,
    items: value === 'Object' || value === 'Array' ? props.rule.items : undefined,
    filterPath: value === 'Array' ? props.rule.filterPath : undefined,
    filterEquals: value === 'Array' ? props.rule.filterEquals : undefined,
  })
}

function setJson(key: 'default' | 'filterEquals', value: string) {
  try {
    const parsed: unknown = value.trim() ? JSON.parse(value) : undefined
    if (key === 'filterEquals' && parsed !== null && typeof parsed === 'object') throw new Error()
    error.value = ''
    update({ [key]: parsed })
  } catch { error.value = '请输入合法的 JSON 值' }
}

function drop(event: DragEvent) {
  const path = event.dataTransfer?.getData('text/plain')
  if (path && props.paths.includes(path)) update({ source: path })
}

function updateItem(index: number, item: MappingRule) {
  update({ items: props.rule.items?.map((current, i) => i === index ? item : current) })
}
</script>

<template>
  <div class="mapping-rule">
    <input :value="rule.source" :list="fieldList" aria-label="源路径" :placeholder="depth ? '元素内的相对路径' : '源字段路径'"
      @dragover.prevent @drop.stop.prevent="drop" @change="update({ source: input($event) })" />
    <input :value="rule.target" aria-label="目标路径" placeholder="目标字段路径" @change="update({ target: input($event) })" />
    <select :value="rule.transform ?? 'Copy'" aria-label="转换类型" @change="transform(input($event))">
      <option v-for="type in mappingTransforms" :key="type">{{ type }}</option>
    </select>
    <details>
      <summary>默认值与转换选项</summary>
      <label>默认值 JSON <input :value="JSON.stringify(rule.default)" @change="setJson('default', input($event))" /></label>
      <label><input type="checkbox" :checked="rule.omitNull" @change="update({ omitNull: ($event.target as HTMLInputElement).checked })" /> 省略空值</label>
      <label v-if="rule.transform === 'Number'">精度
        <input type="number" min="0" max="10" :value="rule.precision" @change="update({ precision: input($event) === '' ? undefined : Number(input($event)) })" />
      </label>
      <template v-if="rule.transform === 'Array'">
        <label>按路径过滤 <input :value="rule.filterPath" @change="update({
          filterPath: input($event) || undefined, filterEquals: input($event) ? rule.filterEquals ?? null : undefined,
        })" /></label>
        <label v-if="rule.filterPath">等于 JSON 值 <input :value="JSON.stringify(rule.filterEquals)" @change="setJson('filterEquals', input($event))" /></label>
      </template>
      <small v-if="error" role="status">{{ error }}</small>
    </details>
    <div v-if="nested && depth < 4" class="mapping-rule__children">
      <ActionMappingRuleEditor v-for="(item, index) in rule.items ?? []" :key="index" :rule="item" :paths="[]" :depth="depth + 1"
        @update="value => updateItem(index, value)" @remove="update({ items: rule.items!.length > 1 ? rule.items!.filter((_, i) => i !== index) : undefined })" />
      <el-button size="small" :disabled="(rule.items?.length ?? 0) >= 64"
        @click="update({ items: [...(rule.items ?? []), newMappingRule()] })">添加子字段</el-button>
    </div>
    <el-button text type="danger" size="small" @click="emit('remove')">删除字段</el-button>
  </div>
</template>

<style scoped>
.mapping-rule { display: grid; gap: 4px; min-width: 0; padding: 4px; border: 1px solid var(--el-border-color); border-radius: 4px; }
input { min-width: 0; }
details label, .mapping-rule__children { display: grid; gap: 4px; margin-top: 4px; }
small { color: var(--el-color-danger); }
</style>
