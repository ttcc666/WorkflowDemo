import type { FormFieldType, FormField, FormSchema, FormValues } from '@ui/contracts/forms'

/** 系统默认允许上传的扩展名（与服务端 ApprovalFileOptions.AllowedExtensions 默认值一致） */
export const defaultFileExtensions = ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'txt', 'csv', 'png', 'jpg', 'jpeg', 'gif', 'zip']

export interface FieldTypeMeta {
  type: FormFieldType
  label: string
  icon: string
  group: '基础' | '选择' | '人员' | '其他'
}

export const fieldTypes: FieldTypeMeta[] = [
  { type: 'Text', label: '单行文本', icon: 'T', group: '基础' },
  { type: 'TextArea', label: '多行文本', icon: '¶', group: '基础' },
  { type: 'Number', label: '数字', icon: '#', group: '基础' },
  { type: 'Money', label: '金额', icon: '¥', group: '基础' },
  { type: 'Formula', label: '计算字段', icon: 'ƒ', group: '基础' },
  { type: 'Date', label: '日期', icon: '📅', group: '基础' },
  { type: 'DateRange', label: '日期区间', icon: '⇔', group: '基础' },
  { type: 'Select', label: '下拉选择', icon: '▾', group: '选择' },
  { type: 'MultiSelect', label: '多选', icon: '☑', group: '选择' },
  { type: 'Radio', label: '单选', icon: '◉', group: '选择' },
  { type: 'Switch', label: '开关', icon: '⏻', group: '选择' },
  { type: 'User', label: '人员', icon: '👤', group: '人员' },
  { type: 'Users', label: '多个人员', icon: '👥', group: '人员' },
  { type: 'Table', label: '明细表', icon: '▦', group: '其他' },
  { type: 'File', label: '附件', icon: '📎', group: '其他' },
  { type: 'Description', label: '说明文字', icon: 'ℹ', group: '其他' },
]

export const fieldTypeLabels = Object.fromEntries(fieldTypes.map((t) => [t.type, t.label])) as Record<FormFieldType, string>

/** 明细表列支持的类型 */
export const columnTypes: FormFieldType[] = ['Text', 'Number', 'Money', 'Date', 'Select', 'Formula']

export const optionTypes = new Set<FormFieldType>(['Select', 'MultiSelect', 'Radio'])
/** 可填写的数值类型（最小 / 最大值、小数位数） */
export const numberTypes = new Set<FormFieldType>(['Number', 'Money'])
/** 数值类型（可合计、可被公式引用） */
export const numericTypes = new Set<FormFieldType>(['Number', 'Money', 'Formula'])
export const textTypes = new Set<FormFieldType>(['Text', 'TextArea'])
export const userTypes = new Set<FormFieldType>(['User', 'Users'])
const arrayTypes = new Set<FormFieldType>(['MultiSelect', 'Users', 'Table', 'File'])
/** 默认整行显示的类型 */
const wideTypes = new Set<FormFieldType>(['TextArea', 'Table', 'Description'])

export const FIELD_KEY_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/

export function isValidFieldKey(key: string) {
  return FIELD_KEY_PATTERN.test(key)
}

export function sumKey(tableKey: string, columnKey: string) {
  return `${tableKey}_${columnKey}_sum`
}

/** 生成不重复的字段标识，如 text1、text2 */
export function newFieldKey(type: FormFieldType, existing: Iterable<string>) {
  const taken = new Set([...existing].map((k) => k.toLowerCase()))
  const base = type.charAt(0).toLowerCase() + type.slice(1)
  let i = 1
  while (taken.has(`${base}${i}`.toLowerCase())) i++
  return `${base}${i}`
}

/** 新字段的默认配置 */
export function newField(type: FormFieldType, existingKeys: Iterable<string>): FormField {
  const field: FormField = { key: newFieldKey(type, existingKeys), label: fieldTypeLabels[type], type, span: wideTypes.has(type) ? 24 : 12 }
  if (optionTypes.has(type)) {
    field.options = [
      { label: '选项一', value: 'option1' },
      { label: '选项二', value: 'option2' },
    ]
  }
  if (type === 'Money') field.precision = 2
  if (type === 'Formula') field.formula = ''
  if (type === 'Table') {
    field.span = 24
    field.columns = [
      { key: 'name', label: '项目', type: 'Text', required: true },
      { key: 'amount', label: '金额', type: 'Money', required: true, sum: true },
    ]
  }
  if (type === 'Description') field.help = '在这里填写说明内容'
  return field
}

export function cloneField<T>(value: T): T {
  return value === undefined ? value : (JSON.parse(JSON.stringify(value)) as T)
}

/** 字段的初始值：默认值，否则按类型给空值（数组类为 []、开关为 false） */
export function initialValue(field: FormField): unknown {
  if (field.defaultValue !== undefined && field.defaultValue !== null) return cloneField(field.defaultValue)
  if (field.type === 'Switch') return false
  if (arrayTypes.has(field.type)) return []
  return null
}

/** 按表单结构生成初始数据；传入 existing 时保留已有值（如重新提交时预填） */
export function initialValues(schema: FormSchema | undefined, existing: FormValues = {}): FormValues {
  const result: FormValues = {}
  for (const field of schema?.fields ?? []) {
    if (field.type === 'Description') continue
    result[field.key] = field.key in existing ? cloneField(existing[field.key]) : initialValue(field)
  }
  return result
}

/** 新的明细行 */
export function newRow(table: FormField): FormValues {
  return Object.fromEntries((table.columns ?? []).map((c) => [c.key, initialValue(c)]))
}

export function tableSum(rows: unknown, columnKey: string) {
  if (!Array.isArray(rows)) return 0
  const total = rows.reduce((sum: number, row) => {
    const value = Number((row as FormValues | null)?.[columnKey])
    return Number.isFinite(value) ? sum + value : sum
  }, 0)
  return Math.round(total * 1e6) / 1e6
}

/** 公式中可引用的数值：顶层数值字段（含计算字段）与明细合计字段 */
export function numericVariables(schema: FormSchema | undefined): { name: string; label: string }[] {
  return fieldVariables(schema)
    .filter((v) => numericTypes.has(v.type))
    .map(({ name, label }) => ({ name, label }))
}

/** 可在条件表达式与模板中引用的字段（含明细合计字段） */
export function fieldVariables(schema: FormSchema | undefined): { name: string; label: string; type: FormFieldType }[] {
  const result: { name: string; label: string; type: FormFieldType }[] = []
  for (const field of schema?.fields ?? []) {
    if (field.type === 'Description') continue
    if (field.type !== 'Table') result.push({ name: field.key, label: field.label, type: field.type })
    for (const column of field.columns ?? []) {
      if (field.type === 'Table' && column.sum) result.push({ name: sumKey(field.key, column.key), label: `${field.label}合计（${column.label}）`, type: 'Number' })
    }
  }
  return result
}

/** 人员字段（可作为审批人 / 通知接收人） */
export function userFields(schema: FormSchema | undefined) {
  return (schema?.fields ?? []).filter((f) => userTypes.has(f.type))
}
