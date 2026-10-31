<script setup lang="ts">
import { computed } from 'vue'
import UserPicker from '../../components/UserPicker.vue'
import FileUploadInput from './FileUploadInput.vue'
import type { FormField } from './formModel'

/** 单个字段的输入控件（明细表单元格也使用）。 */
const props = defineProps<{ field: FormField; size?: 'large' | 'default' | 'small' }>()
const model = defineModel<unknown>()

const textLimit = computed(() => Math.min(props.field.maxLength ?? (props.field.type === 'Text' ? 200 : 2000), 4000))
const precision = computed(() => props.field.precision ?? (props.field.type === 'Money' ? 2 : undefined))
const placeholder = computed(() => props.field.placeholder || undefined)

const stringValue = computed({
  get: () => (model.value as string | null | undefined) ?? '',
  set: (v: string) => (model.value = v),
})
const numberValue = computed({
  get: () => (typeof model.value === 'number' ? model.value : model.value == null || model.value === '' ? undefined : Number(model.value)),
  set: (v: number | undefined | null) => (model.value = v ?? null),
})
const arrayValue = computed({
  get: () => (Array.isArray(model.value) ? (model.value as string[]) : []),
  set: (v: string[]) => (model.value = v),
})
const rangeValue = computed({
  get: () => (Array.isArray(model.value) && model.value.length === 2 ? (model.value as [string, string]) : null),
  set: (v: [string, string] | null) => (model.value = v ?? null),
})
const userValue = computed({
  get: () => (model.value as string | string[] | null | undefined) ?? null,
  set: (v: string | string[] | null) => (model.value = v ?? (props.field.type === 'Users' ? [] : null)),
})
// 计算字段只读显示，值由公式自动计算
const formulaText = computed(() => {
  const value = typeof model.value === 'number' ? model.value : null
  const digits = props.field.precision ?? 2
  return value === null ? '' : value.toLocaleString('zh-CN', { minimumFractionDigits: digits, maximumFractionDigits: digits })
})
</script>

<template>
  <el-input v-if="field.type === 'Text'" v-model="stringValue" :maxlength="textLimit" :placeholder="placeholder" :size="size" clearable />
  <el-input v-else-if="field.type === 'TextArea'" v-model="stringValue" type="textarea" :rows="3" :maxlength="textLimit" show-word-limit
    :placeholder="placeholder" :size="size" />
  <el-input-number v-else-if="field.type === 'Number' || field.type === 'Money'" v-model="numberValue" :min="field.min ?? undefined"
    :max="field.max ?? undefined" :precision="precision" :value-on-clear="null" controls-position="right" :placeholder="placeholder"
    :size="size" class="field-input--full" />
  <el-date-picker v-else-if="field.type === 'Date'" v-model="stringValue" type="date" value-format="YYYY-MM-DD" :placeholder="placeholder ?? '选择日期'"
    :size="size" class="field-input--full" />
  <el-date-picker v-else-if="field.type === 'DateRange'" v-model="rangeValue" type="daterange" value-format="YYYY-MM-DD" start-placeholder="开始日期"
    end-placeholder="结束日期" :size="size" class="field-input--full" />
  <el-select v-else-if="field.type === 'Select'" v-model="stringValue" :placeholder="placeholder ?? '请选择'" clearable :size="size"
    class="field-input--full">
    <el-option v-for="o in field.options ?? []" :key="o.value" :label="o.label" :value="o.value" />
  </el-select>
  <el-select v-else-if="field.type === 'MultiSelect'" v-model="arrayValue" multiple :placeholder="placeholder ?? '请选择'" clearable :size="size"
    class="field-input--full">
    <el-option v-for="o in field.options ?? []" :key="o.value" :label="o.label" :value="o.value" />
  </el-select>
  <el-radio-group v-else-if="field.type === 'Radio'" v-model="stringValue" :size="size">
    <el-radio v-for="o in field.options ?? []" :key="o.value" :value="o.value">{{ o.label }}</el-radio>
  </el-radio-group>
  <el-switch v-else-if="field.type === 'Switch'" :model-value="model === true" :size="size" @update:model-value="(v) => (model = v === true)" />
  <UserPicker v-else-if="field.type === 'User' || field.type === 'Users'" v-model="userValue" :multiple="field.type === 'Users'"
    :placeholder="placeholder ?? '搜索人员'" :size="size" />
  <FileUploadInput v-else-if="field.type === 'File'" v-model="model" :field="field" />
  <el-input v-else-if="field.type === 'Formula'" :model-value="formulaText" disabled placeholder="自动计算" :size="size"
    class="field-input--formula" :title="field.formula ?? ''" />
</template>

<style scoped>
.field-input--full {
  width: 100%;
}

.field-input--formula :deep(.el-input__inner) {
  color: #303133;
  -webkit-text-fill-color: #303133;
}
</style>
