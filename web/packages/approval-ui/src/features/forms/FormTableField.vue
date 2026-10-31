<script setup lang="ts">
import { computed } from 'vue'
import FormFieldInput from './FormFieldInput.vue'
import FormValueText from './FormValueText.vue'
import { fieldRules, newRow, tableSum, type FormField, type FormValues } from './formModel'

/** 明细表：行增删、单元格校验（prop 为 表格.行号.列，与服务端错误路径一致）、数值列合计。 */
const props = defineProps<{
  field: FormField
  readonly?: boolean
  errors?: Record<string, string[]>
}>()

const rows = defineModel<FormValues[]>({ required: true })

const columns = computed(() => props.field.columns ?? [])
const sumColumns = computed(() => columns.value.filter((c) => c.sum))
const maxRows = computed(() => props.field.maxRows ?? 100)

const add = () => (rows.value = [...rows.value, newRow(props.field)])
const remove = (index: number) => (rows.value = rows.value.filter((_, i) => i !== index))
const errorOf = (path: string) => props.errors?.[path]?.[0] ?? ''
const formatSum = (column: FormField) =>
  tableSum(rows.value, column.key).toLocaleString('zh-CN', { minimumFractionDigits: column.type === 'Money' ? 2 : 0, maximumFractionDigits: 6 })
</script>

<template>
  <div class="form-table">
    <el-table :data="rows" border size="small" empty-text="暂无明细" class="form-table__table">
      <el-table-column type="index" label="#" width="48" align="center" />
      <el-table-column v-for="column in columns" :key="column.key" :min-width="column.type === 'Text' ? 160 : 130">
        <template #header>
          <span :class="{ 'form-table__required': column.required && !readonly }">{{ column.label }}</span>
        </template>
        <template #default="{ row, $index }">
          <FormValueText v-if="readonly" :field="column" :value="row[column.key]" />
          <el-form-item v-else :prop="`${field.key}.${$index}.${column.key}`" :rules="fieldRules(column)"
            :error="errorOf(`${field.key}.${$index}.${column.key}`)" class="form-table__cell">
            <FormFieldInput v-model="row[column.key]" :field="column" size="small" />
          </el-form-item>
        </template>
      </el-table-column>
      <el-table-column v-if="!readonly" label="" width="60" align="center">
        <template #default="{ $index }">
          <el-button link type="danger" size="small" @click="remove($index)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <div class="form-table__footer">
      <el-button v-if="!readonly" size="small" plain type="primary" :disabled="rows.length >= maxRows" @click="add">+ 添加一行</el-button>
      <span v-for="column in sumColumns" :key="column.key" class="form-table__sum">
        {{ column.label }}合计：<strong>{{ formatSum(column) }}</strong>
      </span>
    </div>
  </div>
</template>

<style scoped>
.form-table {
  width: 100%;
}

.form-table__table :deep(.cell) {
  overflow: visible;
}

.form-table__cell {
  margin-bottom: 0;
}

/* 单元格错误提示显示在输入框下方，留出空间 */
.form-table__cell:deep(.el-form-item__error) {
  position: static;
}

.form-table__required::before {
  margin-right: 2px;
  color: var(--el-color-danger);
  content: '*';
}

.form-table__footer {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-top: 8px;
  color: #606266;
  font-size: 13px;
}

.form-table__sum strong {
  color: #303133;
}
</style>
