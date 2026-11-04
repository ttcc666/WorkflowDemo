<script setup lang="ts">
import { computed } from 'vue'
import FormFieldInput from './FormFieldInput.vue'
import FormulaInput from './FormulaInput.vue'
import OptionsEditor from './OptionsEditor.vue'
import {
  columnTypes,
  defaultFileExtensions,
  fieldTypeLabels,
  isValidFieldKey,
  newFieldKey,
  numberTypes,
  numericTypes,
  optionTypes,
  textTypes,
  type FormField,
  type FormFieldType,
} from './formModel'

/** 字段属性面板。isColumn 为明细表的列（不支持宽度、嵌套）。 */
const props = defineProps<{
  field: FormField
  /** 其他字段的标识，用于检查重复 */
  otherKeys: string[]
  isColumn?: boolean
  /** 计算字段可引用的数值（其他数值字段与明细合计） */
  formulaVariables?: { name: string; label: string }[]
}>()

const emit = defineEmits<{ update: [patch: Partial<FormField>] }>()

const keyError = computed(() => {
  if (props.field.type === 'Description') return ''
  if (!isValidFieldKey(props.field.key)) return '只能包含字母、数字和下划线，且不能以数字开头'
  if (props.otherKeys.some((k) => k.toLowerCase() === props.field.key.toLowerCase())) return '与其他字段重复'
  return ''
})

const isNumber = computed(() => numberTypes.has(props.field.type))
const conditionExample = computed(() => {
  const { key, type, options } = props.field
  if (numberTypes.has(type)) return `${key} > 3`
  if (type === 'Switch') return `${key} == true`
  if (optionTypes.has(type) && type !== 'MultiSelect' && options?.[0]) return `${key} == "${options[0].value}"`
  return `${key} == "…"`
})
const supportsDefault = computed(() => !['Table', 'Description', 'User', 'Users', 'DateRange', 'Formula', 'File'].includes(props.field.type))
const isFormula = computed(() => props.field.type === 'Formula')
const defaultField = computed<FormField>(() => ({ ...props.field, required: false, defaultValue: undefined }))

const numberOrNull = (v: number | null | undefined) => (v === undefined || v === null || Number.isNaN(v) ? null : v)

// ───── 明细表的列 ─────

const columns = computed(() => props.field.columns ?? [])

function updateColumn(index: number, patch: Partial<FormField>) {
  emit('update', { columns: columns.value.map((c, i) => (i === index ? { ...c, ...patch } : c)) })
}

function changeColumnType(index: number, type: FormFieldType) {
  const column = columns.value[index]!
  const patch: Partial<FormField> = { type, sum: numericTypes.has(type) ? column.sum : false, defaultValue: undefined }
  if (optionTypes.has(type) && !column.options?.length) patch.options = [{ label: '选项一', value: 'option1' }]
  if (type === 'Money') patch.precision = 2
  if (type === 'Formula') Object.assign(patch, { formula: column.formula ?? '', required: false })
  updateColumn(index, patch)
}

function addColumn() {
  emit('update', { columns: [...columns.value, { key: newFieldKey('Text', columns.value.map((c) => c.key)), label: '新列', type: 'Text' }] })
}

const removeColumn = (index: number) => emit('update', { columns: columns.value.filter((_, i) => i !== index) })
/** 明细列公式只能引用本行的其他数值列 */
const columnVariables = (index: number) =>
  columns.value.filter((c, i) => i !== index && numericTypes.has(c.type)).map((c) => ({ name: c.key, label: c.label }))
const columnKeyInvalid = (index: number) => {
  const key = columns.value[index]!.key
  return !isValidFieldKey(key) || columns.value.some((c, i) => i !== index && c.key.toLowerCase() === key.toLowerCase())
}
</script>

<template>
  <el-form label-position="top" size="small" class="field-inspector" @submit.prevent>
    <div class="field-inspector__type">{{ fieldTypeLabels[field.type] }}</div>

    <el-form-item :label="field.type === 'Description' ? '标题' : '名称'" required>
      <el-input :model-value="field.label" maxlength="50" @update:model-value="(v: string) => emit('update', { label: v })" />
    </el-form-item>

    <el-form-item v-if="field.type !== 'Description'" label="字段标识" required :error="keyError">
      <el-input :model-value="field.key" maxlength="50" @update:model-value="(v: string) => emit('update', { key: v.trim() })" />
      <div class="field-inspector__help">
        <template v-if="field.type === 'Table'">明细表通过合计列用于条件分支（见下方“列”）；</template>
        <template v-else>用于条件分支与消息模板，如 <code>{{ conditionExample }}</code>、<code>{form.{{ field.key }}}</code>；</template>
        发布后不建议修改。
      </div>
    </el-form-item>

    <el-form-item v-if="isFormula" label="公式" required>
      <FormulaInput :model-value="field.formula ?? ''" :variables="formulaVariables ?? []"
        placeholder="如 items_amount_sum - ifnull(advance, 0)" @update:model-value="(v) => emit('update', { formula: v })" />
    </el-form-item>

    <el-form-item v-if="isFormula" label="小数位数">
      <el-input-number :model-value="field.precision ?? undefined" :min="0" :max="6" :value-on-clear="null" controls-position="right"
        placeholder="2" @update:model-value="(v) => emit('update', { precision: numberOrNull(v) })" />
    </el-form-item>

    <el-form-item v-if="field.type !== 'Description' && !isFormula">
      <el-checkbox :model-value="!!field.required" @update:model-value="(v) => emit('update', { required: v === true })">
        {{ field.type === 'Switch' ? '必须打开（如“我已阅读并同意”）' : '必填' }}
      </el-checkbox>
    </el-form-item>

    <el-form-item v-if="!isColumn" label="宽度">
      <el-radio-group :model-value="field.span ?? 24" :disabled="field.type === 'Table' || field.type === 'Description'"
        @update:model-value="(v) => emit('update', { span: Number(v) })">
        <el-radio-button :value="12">半行</el-radio-button>
        <el-radio-button :value="24">整行</el-radio-button>
      </el-radio-group>
    </el-form-item>

    <el-form-item v-if="optionTypes.has(field.type)" label="选项" required>
      <OptionsEditor :model-value="field.options ?? []" @update:model-value="(v) => emit('update', { options: v })" />
    </el-form-item>

    <template v-if="isNumber">
      <div class="field-inspector__row">
        <el-form-item label="最小值">
          <el-input-number :model-value="field.min ?? undefined" :value-on-clear="null" controls-position="right"
            @update:model-value="(v) => emit('update', { min: numberOrNull(v) })" />
        </el-form-item>
        <el-form-item label="最大值">
          <el-input-number :model-value="field.max ?? undefined" :value-on-clear="null" controls-position="right"
            @update:model-value="(v) => emit('update', { max: numberOrNull(v) })" />
        </el-form-item>
      </div>
      <el-form-item label="小数位数">
        <el-input-number :model-value="field.precision ?? undefined" :min="0" :max="6" :value-on-clear="null" controls-position="right"
          :placeholder="field.type === 'Money' ? '2' : '不限'" @update:model-value="(v) => emit('update', { precision: numberOrNull(v) })" />
      </el-form-item>
      <el-form-item v-if="isColumn">
        <el-checkbox :model-value="!!field.sum" @update:model-value="(v) => emit('update', { sum: v === true })">合计</el-checkbox>
      </el-form-item>
    </template>

    <el-form-item v-if="textTypes.has(field.type)" label="最大长度">
      <el-input-number :model-value="field.maxLength ?? undefined" :min="1" :max="4000" :value-on-clear="null" controls-position="right"
        :placeholder="field.type === 'Text' ? '200' : '2000'" @update:model-value="(v) => emit('update', { maxLength: numberOrNull(v) })" />
    </el-form-item>

    <template v-if="field.type === 'File'">
      <el-form-item label="最多文件数">
        <el-input-number :model-value="field.maxFiles ?? undefined" :min="1" :max="50" :value-on-clear="null" controls-position="right"
          placeholder="10" @update:model-value="(v) => emit('update', { maxFiles: numberOrNull(v) })" />
      </el-form-item>
      <el-form-item label="允许的类型">
        <el-select :model-value="field.accept ?? []" multiple filterable allow-create default-first-option
          placeholder="不限（按系统允许的类型）" style="width: 100%"
          @update:model-value="(v: string[]) => emit('update', { accept: v.map((e) => e.replace(/^\./, '').toLowerCase()) })">
          <el-option v-for="e in defaultFileExtensions" :key="e" :label="e" :value="e" />
        </el-select>
      </el-form-item>
    </template>

    <el-form-item v-if="!['Description', 'Table', 'Switch', 'Formula', 'File'].includes(field.type)" label="占位提示">
      <el-input :model-value="field.placeholder ?? ''" maxlength="100" @update:model-value="(v: string) => emit('update', { placeholder: v || null })" />
    </el-form-item>

    <el-form-item :label="field.type === 'Description' ? '说明内容' : '帮助文字'">
      <el-input :model-value="field.help ?? ''" type="textarea" :rows="field.type === 'Description' ? 4 : 2" maxlength="500"
        @update:model-value="(v: string) => emit('update', { help: v || null })" />
    </el-form-item>

    <el-form-item v-if="supportsDefault" label="默认值">
      <FormFieldInput :model-value="field.defaultValue" :field="defaultField" @update:model-value="(v) => emit('update', { defaultValue: v ?? undefined })" />
    </el-form-item>

    <template v-if="field.type === 'Table'">
      <el-form-item label="最多行数">
        <el-input-number :model-value="field.maxRows ?? undefined" :min="1" :max="500" :value-on-clear="null" controls-position="right"
          placeholder="100" @update:model-value="(v) => emit('update', { maxRows: numberOrNull(v) })" />
      </el-form-item>
      <el-form-item label="列" required>
        <div class="columns">
          <div v-for="(column, index) in columns" :key="index" class="column">
            <div class="column__line">
              <el-input :model-value="column.label" placeholder="列名" @update:model-value="(v: string) => updateColumn(index, { label: v })" />
              <el-input :model-value="column.key" placeholder="标识" :class="{ 'column__key--error': columnKeyInvalid(index) }"
                @update:model-value="(v: string) => updateColumn(index, { key: v.trim() })" />
              <el-select :model-value="column.type" @update:model-value="(v: FormFieldType) => changeColumnType(index, v)">
                <el-option v-for="t in columnTypes" :key="t" :label="fieldTypeLabels[t]" :value="t" />
              </el-select>
            </div>
            <div class="column__line">
              <el-checkbox v-if="column.type !== 'Formula'" :model-value="!!column.required" @update:model-value="(v) => updateColumn(index, { required: v === true })">必填</el-checkbox>
              <el-checkbox v-if="numericTypes.has(column.type)" :model-value="!!column.sum"
                @update:model-value="(v) => updateColumn(index, { sum: v === true })">合计</el-checkbox>
              <el-popover v-if="optionTypes.has(column.type)" trigger="click" width="320" placement="left">
                <template #reference>
                  <el-button link type="primary" size="small">选项（{{ column.options?.length ?? 0 }}）</el-button>
                </template>
                <OptionsEditor :model-value="column.options ?? []" @update:model-value="(v) => updateColumn(index, { options: v })" />
              </el-popover>
              <el-button link type="danger" size="small" class="column__remove" @click="removeColumn(index)">删除</el-button>
            </div>
            <FormulaInput v-if="column.type === 'Formula'" :model-value="column.formula ?? ''" :variables="columnVariables(index)"
              placeholder="按行计算，如 qty * price" class="column__formula" @update:model-value="(v) => updateColumn(index, { formula: v })" />
          </div>
          <el-button link type="primary" size="small" @click="addColumn">+ 添加列</el-button>
          <div class="field-inspector__help">数值列勾选“合计”后，合计值可在条件中使用：<code>{{ field.key }}_列标识_sum &gt; 5000</code></div>
        </div>
      </el-form-item>
    </template>
  </el-form>
</template>

<style scoped>
.field-inspector__type {
  margin-bottom: 8px;
  font-weight: 600;
}

.field-inspector__help {
  width: 100%;
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}

.field-inspector__help code {
  padding: 0 3px;
  border-radius: 3px;
  background: #f4f4f5;
}

.field-inspector__row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}

.field-inspector__row :deep(.el-input-number) {
  width: 100%;
}

.columns {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
}

.column {
  padding: 6px;
  border: 1px solid #ebeef5;
  border-radius: 4px;
  background: #fafbfc;
}

.column__line {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  align-items: center;
  gap: 4px;
}

.column__line + .column__line {
  display: flex;
  margin-top: 4px;
}

.column__remove {
  margin-left: auto;
}

.column__formula {
  margin-top: 4px;
}

.column__key--error :deep(.el-input__wrapper) {
  box-shadow: 0 0 0 1px var(--el-color-danger) inset;
}
</style>
