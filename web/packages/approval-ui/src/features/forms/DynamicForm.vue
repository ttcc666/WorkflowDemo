<script setup lang="ts">
import { computed, useTemplateRef, watch } from 'vue'
import type { FormInstance } from 'element-plus'
import FormFieldInput from './FormFieldInput.vue'
import FormTableField from './FormTableField.vue'
import FormValueText from './FormValueText.vue'
import { applyFormulas, fieldRules, type FormField, type FormSchema, type FormValues } from './formModel'

/**
 * 按表单结构渲染表单：填写（发起、重新提交、设计器预览）或只读展示（审批详情）。
 * 只读时 editableFields 中的字段仍可填写（审批人在节点可修改的字段），并标注“可修改”。
 * errors 为服务端返回的字段错误（字段路径 → 错误），显示在对应表单项下。
 * 填写时计算字段随输入实时计算（提交后以服务端计算为准）。
 */
const props = defineProps<{
  schema: FormSchema
  readonly?: boolean
  editableFields?: string[]
  errors?: Record<string, string[]>
}>()

const model = defineModel<FormValues>({ required: true })
const formRef = useTemplateRef<FormInstance>('form')

// 字段标识不区分大小写（与服务端一致）
const editableSet = computed(() => new Set((props.editableFields ?? []).map((k) => k.toLowerCase())))
const isEditable = (field: FormField) => !props.readonly || editableSet.value.has(field.key.toLowerCase())
const editing = computed(() => !props.readonly || editableSet.value.size > 0)

// 只在有变化时写回，写回触发的下一次回调不再修改，不会循环
watch([() => props.schema, model, editing], () => {
  if (editing.value) applyFormulas(props.schema, model.value)
}, { deep: true, immediate: true })

const spanOf = (field: FormField) => (field.type === 'Table' || field.type === 'Description' ? 24 : (field.span ?? 24))
const errorOf = (key: string) => props.errors?.[key]?.[0] ?? ''
// 只读展示中的可编辑字段加标注
const marked = (field: FormField) => props.readonly && isEditable(field)

const tableRows = (field: FormField) => (Array.isArray(model.value[field.key]) ? (model.value[field.key] as FormValues[]) : [])

/** 校验全部可填写的字段，返回是否通过 */
async function validate() {
  if (!editing.value || !formRef.value) return true
  return formRef.value.validate().then(
    () => true,
    () => false,
  )
}

defineExpose({ validate, clearValidate: () => formRef.value?.clearValidate() })
</script>

<template>
  <el-form ref="form" :model="model" label-position="top" class="dynamic-form" :class="{ 'dynamic-form--readonly': readonly }"
    @submit.prevent>
    <el-row :gutter="16">
      <el-col v-for="field in schema.fields" :key="field.key" :span="spanOf(field)" :xs="24">
        <div v-if="field.type === 'Description'" class="dynamic-form__description">
          <div class="dynamic-form__description-title">{{ field.label }}</div>
          <div v-if="field.help" class="dynamic-form__description-text">{{ field.help }}</div>
        </div>

        <el-form-item v-else-if="field.type === 'Table'" :prop="field.key" :rules="isEditable(field) ? fieldRules(field) : []"
          :required="field.required && isEditable(field)" :error="errorOf(field.key)" :class="{ 'dynamic-form__item--editable': marked(field) }">
          <template #label>
            {{ field.label }}<el-tag v-if="marked(field)" size="small" type="warning" effect="plain" class="dynamic-form__tag">可修改</el-tag>
          </template>
          <FormTableField :model-value="tableRows(field)" :field="field" :readonly="!isEditable(field)" :errors="errors"
            @update:model-value="(rows) => (model[field.key] = rows)" />
          <div v-if="field.help && isEditable(field)" class="dynamic-form__help">{{ field.help }}</div>
        </el-form-item>

        <el-form-item v-else :prop="field.key" :rules="isEditable(field) ? fieldRules(field) : []" :error="errorOf(field.key)"
          :class="{ 'dynamic-form__item--editable': marked(field) }">
          <template #label>
            {{ field.label }}<el-tag v-if="marked(field)" size="small" type="warning" effect="plain" class="dynamic-form__tag">可修改</el-tag>
          </template>
          <template v-if="isEditable(field)">
            <FormFieldInput v-model="model[field.key]" :field="field" />
            <div v-if="field.help" class="dynamic-form__help">{{ field.help }}</div>
          </template>
          <FormValueText v-else :field="field" :value="model[field.key]" />
        </el-form-item>
      </el-col>
    </el-row>
    <el-empty v-if="schema.fields.length === 0" :image-size="60" description="没有表单字段" />
  </el-form>
</template>

<style scoped>
.dynamic-form__help {
  width: 100%;
  margin-top: 2px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}

.dynamic-form__description {
  margin-bottom: 18px;
  padding: 8px 12px;
  border-left: 3px solid var(--el-color-primary-light-5);
  border-radius: 4px;
  background: #f4f8fe;
}

.dynamic-form__description-title {
  font-weight: 600;
}

.dynamic-form__description-text {
  margin-top: 2px;
  color: #606266;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
}

.dynamic-form__tag {
  margin-left: 6px;
}

.dynamic-form--readonly :deep(.el-form-item) {
  margin-bottom: 12px;
}

.dynamic-form--readonly :deep(.el-form-item__label) {
  margin-bottom: 2px;
  color: #909399;
}

/* 只读展示中可修改的字段：留出校验提示的位置 */
.dynamic-form--readonly .dynamic-form__item--editable {
  margin-bottom: 18px;
}
</style>
