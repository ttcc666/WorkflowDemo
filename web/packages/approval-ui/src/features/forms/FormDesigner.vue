<script setup lang="ts">
import { computed, ref, shallowRef } from 'vue'
import DynamicForm from './DynamicForm.vue'
import FieldInspector from './FieldInspector.vue'
import { cloneField, fieldTypeLabels, fieldTypes, initialValues, newField, newFieldKey, numericVariables, type FormField, type FormFieldType, type FormSchema, type FormValues } from './formModel'

/**
 * 表单设计器：左侧字段类型（点击或拖入添加）、中间字段列表（拖拽排序、复制、删除）、右侧字段属性；可切换预览。
 * 每次修改都整体替换 v-model 的值，由流程设计器记录撤销 / 重做快照。
 */
const schema = defineModel<FormSchema>({ required: true })

const FIELD_TYPE = 'application/x-approval-field-type'
const FIELD_INDEX = 'application/x-approval-field-index'

const selected = shallowRef<number | null>(schema.value.fields.length ? 0 : null)
const preview = shallowRef(false)
const previewData = ref<FormValues>({})
const dropIndex = shallowRef<number | null>(null)

const fields = computed(() => schema.value.fields)
const selectedField = computed(() => (selected.value === null ? undefined : fields.value[selected.value]))
const otherKeys = computed(() => fields.value.filter((_, i) => i !== selected.value && fields.value[i]!.type !== 'Description').map((f) => f.key))
// 计算字段可引用的数值（不含自身）
const formulaVariables = computed(() =>
  numericVariables(schema.value).filter((v) => v.name.toLowerCase() !== selectedField.value?.key.toLowerCase()),
)
const groups = computed(() => [...new Set(fieldTypes.map((t) => t.group))].map((g) => ({ name: g, types: fieldTypes.filter((t) => t.group === g) })))

function setFields(next: FormField[]) {
  schema.value = { ...schema.value, fields: next }
}

function insert(type: FormFieldType, index = fields.value.length) {
  const field = newField(type, fields.value.map((f) => f.key))
  setFields([...fields.value.slice(0, index), field, ...fields.value.slice(index)])
  selected.value = index
}

function update(patch: Partial<FormField>) {
  if (selected.value === null) return
  const index = selected.value
  setFields(fields.value.map((f, i) => (i === index ? { ...f, ...patch } : f)))
}

function duplicate(index: number) {
  const copy = cloneField(fields.value[index]!)
  copy.key = newFieldKey(copy.type, fields.value.map((f) => f.key))
  copy.label = `${copy.label}（副本）`
  setFields([...fields.value.slice(0, index + 1), copy, ...fields.value.slice(index + 1)])
  selected.value = index + 1
}

function remove(index: number) {
  setFields(fields.value.filter((_, i) => i !== index))
  if (selected.value === null) return
  if (selected.value === index) selected.value = fields.value.length ? Math.min(index, fields.value.length - 1) : null
  else if (selected.value > index) selected.value--
}

function move(from: number, to: number) {
  if (from === to || from + 1 === to) return
  const next = [...fields.value]
  const [field] = next.splice(from, 1)
  const target = to > from ? to - 1 : to
  next.splice(target, 0, field!)
  setFields(next)
  selected.value = target
}

// ───── 拖拽：从左侧拖入新字段，或在列表中拖动排序 ─────

function onPaletteDragStart(e: DragEvent, type: FormFieldType) {
  e.dataTransfer?.setData(FIELD_TYPE, type)
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'copy'
}

function onFieldDragStart(e: DragEvent, index: number) {
  e.dataTransfer?.setData(FIELD_INDEX, String(index))
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move'
}

function onDragOver(e: DragEvent, index: number) {
  const types = e.dataTransfer?.types ?? []
  if (!types.includes(FIELD_TYPE) && !types.includes(FIELD_INDEX)) return
  e.preventDefault()
  dropIndex.value = index
}

function onDrop(e: DragEvent) {
  const index = dropIndex.value ?? fields.value.length
  dropIndex.value = null
  const type = e.dataTransfer?.getData(FIELD_TYPE) as FormFieldType | undefined
  if (type) return insert(type, index)
  const from = e.dataTransfer?.getData(FIELD_INDEX)
  if (from) move(Number(from), index)
}

function togglePreview() {
  preview.value = !preview.value
  if (preview.value) previewData.value = initialValues(schema.value)
}
</script>

<template>
  <div class="form-designer">
    <aside class="form-designer__palette">
      <div v-for="group in groups" :key="group.name" class="palette-group">
        <div class="palette-group__title">{{ group.name }}</div>
        <div class="palette-group__items">
          <div v-for="t in group.types" :key="t.type" class="palette-item" draggable="true" :title="`点击或拖入添加${t.label}`"
            @click="insert(t.type)" @dragstart="(e) => onPaletteDragStart(e, t.type)">
            <span class="palette-item__icon">{{ t.icon }}</span>{{ t.label }}
          </div>
        </div>
      </div>
    </aside>

    <section class="form-designer__canvas">
      <div class="form-designer__bar">
        <span>{{ fields.length }} 个字段</span>
        <el-button size="small" :type="preview ? 'primary' : 'default'" @click="togglePreview">{{ preview ? '返回编辑' : '预览' }}</el-button>
      </div>

      <div v-if="preview" class="form-designer__preview">
        <DynamicForm v-model="previewData" :schema="schema" />
      </div>
      <div v-else class="field-list" @dragover="(e) => onDragOver(e, fields.length)" @dragleave="dropIndex = null" @drop="onDrop">
        <div v-if="fields.length === 0" class="field-list__empty">从左侧点击或拖入字段</div>
        <div v-for="(field, index) in fields" :key="index" class="field-item"
          :class="{ 'field-item--selected': index === selected, 'field-item--drop': dropIndex === index, 'field-item--half': (field.span ?? 24) < 24 && field.type !== 'Table' && field.type !== 'Description' }"
          draggable="true" @click="selected = index" @dragstart="(e) => onFieldDragStart(e, index)"
          @dragover.stop="(e) => onDragOver(e, index)" @drop.stop="onDrop">
          <div class="field-item__main">
            <span class="field-item__label"><span v-if="field.required" class="field-item__required">*</span>{{ field.label || '（未命名）' }}</span>
            <span class="field-item__meta">{{ fieldTypeLabels[field.type] }}<template v-if="field.type !== 'Description'"> · {{ field.key }}</template></span>
          </div>
          <div class="field-item__actions">
            <el-button link size="small" @click.stop="duplicate(index)">复制</el-button>
            <el-button link size="small" type="danger" @click.stop="remove(index)">删除</el-button>
          </div>
        </div>
      </div>
    </section>

    <aside class="form-designer__inspector">
      <FieldInspector v-if="selectedField" :key="selected ?? -1" :field="selectedField" :other-keys="otherKeys"
        :formula-variables="formulaVariables" @update="update" />
      <div v-else class="form-designer__hint">选择一个字段编辑属性。表单字段可用于条件分支（如 <code>days &gt; 3</code>）、审批人（人员字段）和消息模板。</div>
    </aside>
  </div>
</template>

<style scoped>
.form-designer {
  display: flex;
  gap: 12px;
  height: 100%;
  min-height: 0;
}

.form-designer__palette,
.form-designer__inspector,
.form-designer__canvas {
  overflow-y: auto;
  padding: 12px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fff;
}

.form-designer__palette {
  width: 220px;
  flex: none;
}

.form-designer__inspector {
  width: 340px;
  flex: none;
}

.form-designer__canvas {
  flex: 1;
  min-width: 0;
  background: #f7f8fa;
}

.palette-group + .palette-group {
  margin-top: 12px;
}

.palette-group__title {
  margin-bottom: 6px;
  color: #909399;
  font-size: 12px;
}

.palette-group__items {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
}

.palette-item {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 6px 8px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  background: #fff;
  font-size: 12px;
  cursor: grab;
  user-select: none;
}

.palette-item:hover {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
}

.palette-item__icon {
  width: 16px;
  text-align: center;
}

.form-designer__bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
  color: #909399;
  font-size: 13px;
}

.form-designer__preview {
  padding: 16px;
  border-radius: 6px;
  background: #fff;
}

.field-list {
  display: flex;
  flex-wrap: wrap;
  align-content: flex-start;
  gap: 8px;
  min-height: 200px;
}

.field-list__empty {
  width: 100%;
  padding: 60px 0;
  border: 1px dashed #dcdfe6;
  border-radius: 6px;
  color: #c0c4cc;
  text-align: center;
}

.field-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  box-sizing: border-box;
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fff;
  cursor: pointer;
}

.field-item--half {
  width: calc(50% - 4px);
}

.field-item--selected {
  border-color: var(--el-color-primary);
  box-shadow: 0 0 0 1px var(--el-color-primary-light-7);
}

.field-item--drop {
  box-shadow: inset 0 3px 0 var(--el-color-primary);
}

.field-item__main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.field-item__label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.field-item__required {
  margin-right: 2px;
  color: var(--el-color-danger);
}

.field-item__meta {
  color: #909399;
  font-size: 12px;
}

.field-item__actions {
  flex: none;
  visibility: hidden;
}

.field-item:hover .field-item__actions,
.field-item--selected .field-item__actions {
  visibility: visible;
}

.form-designer__hint {
  color: #909399;
  font-size: 13px;
  line-height: 1.8;
}

.form-designer__hint code {
  padding: 0 3px;
  border-radius: 3px;
  background: #f4f4f5;
}
</style>
