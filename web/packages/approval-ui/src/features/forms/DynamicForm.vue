<script setup lang="ts">
import { useTemplateRef } from 'vue'
import type { FormInstance } from 'element-plus'
import FormFieldInput from './FormFieldInput.vue'
import FormTableField from './FormTableField.vue'
import FormValueText from './FormValueText.vue'
import { fieldRules, type FormField, type FormSchema, type FormValues } from './formModel'

/**
 * 按表单结构渲染表单：填写（发起、重新提交、设计器预览）或只读展示（审批详情）。
 * errors 为服务端返回的字段错误（字段路径 → 错误），显示在对应表单项下。
 */
const props = defineProps<{
  schema: FormSchema
  readonly?: boolean
  errors?: Record<string, string[]>
}>()

const model = defineModel<FormValues>({ required: true })
const formRef = useTemplateRef<FormInstance>('form')

const spanOf = (field: FormField) => (field.type === 'Table' || field.type === 'Description' ? 24 : (field.span ?? 24))
const errorOf = (key: string) => props.errors?.[key]?.[0] ?? ''

const tableRows = (field: FormField) => (Array.isArray(model.value[field.key]) ? (model.value[field.key] as FormValues[]) : [])

/** 校验全部字段，返回是否通过 */
async function validate() {
  if (props.readonly || !formRef.value) return true
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

        <el-form-item v-else-if="field.type === 'Table'" :label="field.label" :prop="field.key" :rules="readonly ? [] : fieldRules(field)"
          :required="field.required && !readonly" :error="errorOf(field.key)">
          <FormTableField :model-value="tableRows(field)" :field="field" :readonly="readonly" :errors="errors"
            @update:model-value="(rows) => (model[field.key] = rows)" />
          <div v-if="field.help && !readonly" class="dynamic-form__help">{{ field.help }}</div>
        </el-form-item>

        <el-form-item v-else :label="field.label" :prop="field.key" :rules="readonly ? [] : fieldRules(field)" :error="errorOf(field.key)">
          <FormValueText v-if="readonly" :field="field" :value="model[field.key]" />
          <template v-else>
            <FormFieldInput v-model="model[field.key]" :field="field" />
            <div v-if="field.help" class="dynamic-form__help">{{ field.help }}</div>
          </template>
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

.dynamic-form--readonly :deep(.el-form-item) {
  margin-bottom: 12px;
}

.dynamic-form--readonly :deep(.el-form-item__label) {
  margin-bottom: 2px;
  color: #909399;
}
</style>
