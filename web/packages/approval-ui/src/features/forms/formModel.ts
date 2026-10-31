// 表单结构（与 Approval.Core.Forms.FormSchema / FlowSettings 对应）与表单工具函数

export type FormFieldType =
  | 'Text'
  | 'TextArea'
  | 'Number'
  | 'Money'
  | 'Date'
  | 'DateRange'
  | 'Select'
  | 'MultiSelect'
  | 'Radio'
  | 'Switch'
  | 'User'
  | 'Users'
  | 'Table'
  | 'Description'

export interface FormFieldOption {
  label: string
  value: string
}

export interface FormField {
  key: string
  label: string
  type: FormFieldType
  required?: boolean
  placeholder?: string | null
  help?: string | null
  defaultValue?: unknown
  /** 栅格宽度（共 24），默认整行 */
  span?: number
  options?: FormFieldOption[]
  min?: number | null
  max?: number | null
  precision?: number | null
  maxLength?: number | null
  maxRows?: number | null
  /** 明细表的列 */
  columns?: FormField[]
  /** 明细表数值列：合计写入顶层字段 {表格}_{列}_sum */
  sum?: boolean
}

export interface FormSchema {
  fields: FormField[]
}

/** 发起设置 */
export interface FlowSettings {
  startable?: boolean
  category?: string | null
  description?: string | null
  initiatorRoles?: string[]
  titleTemplate?: string | null
}

export type FormValues = Record<string, unknown>

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
  { type: 'Date', label: '日期', icon: '📅', group: '基础' },
  { type: 'DateRange', label: '日期区间', icon: '⇔', group: '基础' },
  { type: 'Select', label: '下拉选择', icon: '▾', group: '选择' },
  { type: 'MultiSelect', label: '多选', icon: '☑', group: '选择' },
  { type: 'Radio', label: '单选', icon: '◉', group: '选择' },
  { type: 'Switch', label: '开关', icon: '⏻', group: '选择' },
  { type: 'User', label: '人员', icon: '👤', group: '人员' },
  { type: 'Users', label: '多个人员', icon: '👥', group: '人员' },
  { type: 'Table', label: '明细表', icon: '▦', group: '其他' },
  { type: 'Description', label: '说明文字', icon: 'ℹ', group: '其他' },
]

export const fieldTypeLabels = Object.fromEntries(fieldTypes.map((t) => [t.type, t.label])) as Record<FormFieldType, string>

/** 明细表列支持的类型 */
export const columnTypes: FormFieldType[] = ['Text', 'Number', 'Money', 'Date', 'Select']

export const optionTypes = new Set<FormFieldType>(['Select', 'MultiSelect', 'Radio'])
export const numberTypes = new Set<FormFieldType>(['Number', 'Money'])
export const textTypes = new Set<FormFieldType>(['Text', 'TextArea'])
export const userTypes = new Set<FormFieldType>(['User', 'Users'])
const arrayTypes = new Set<FormFieldType>(['MultiSelect', 'Users', 'Table'])
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

// ───── 校验规则（与服务端 FormDataValidator 一致） ─────

export interface FieldRule {
  required?: boolean
  message?: string
  trigger?: string | string[]
  validator?: (rule: unknown, value: unknown, callback: (error?: Error) => void) => void
}

const isEmpty = (value: unknown) =>
  value === null || value === undefined || (typeof value === 'string' && value.trim() === '') || (Array.isArray(value) && value.length === 0)

export function requiredMessage(field: FormField) {
  if (field.type === 'Switch') return `请勾选${field.label}`
  if (field.type === 'Table') return `请至少添加一行${field.label}`
  const choose: FormFieldType[] = ['Select', 'MultiSelect', 'Radio', 'User', 'Users', 'Date', 'DateRange']
  return choose.includes(field.type) ? `请选择${field.label}` : `请填写${field.label}`
}

const formatNumber = (n: number) => String(n)

/** 返回字段值的错误信息（无错误时为 null），供 Element Plus 规则与单元测试使用 */
export function validateValue(field: FormField, value: unknown): string | null {
  if (field.type === 'Description') return null
  if (field.type === 'Switch') return field.required && value !== true ? requiredMessage(field) : null
  if (isEmpty(value)) return field.required ? requiredMessage(field) : null

  if (textTypes.has(field.type)) {
    const limit = Math.min(field.maxLength ?? (field.type === 'Text' ? 200 : 2000), 4000)
    return String(value).trim().length > limit ? `${field.label}不能超过 ${limit} 个字符` : null
  }
  if (numberTypes.has(field.type)) {
    const n = Number(value)
    if (!Number.isFinite(n)) return `${field.label}应为数字`
    const precision = field.precision ?? (field.type === 'Money' ? 2 : null)
    if (precision !== null && Math.round(n * 10 ** precision) / 10 ** precision !== n)
      return precision === 0 ? `${field.label}应为整数` : `${field.label}最多 ${precision} 位小数`
    if (field.min !== null && field.min !== undefined && n < field.min) return `${field.label}不能小于 ${formatNumber(field.min)}`
    if (field.max !== null && field.max !== undefined && n > field.max) return `${field.label}不能大于 ${formatNumber(field.max)}`
    return null
  }
  if (field.type === 'DateRange') {
    const [from, to] = (value as string[]) ?? []
    return from && to && from > to ? `${field.label}的开始日期不能晚于结束日期` : null
  }
  if (field.type === 'Table') {
    const maxRows = field.maxRows ?? 100
    return (value as unknown[]).length > maxRows ? `${field.label}最多 ${maxRows} 行` : null
  }
  if (field.type === 'Users' && (value as unknown[]).length > 100) return `${field.label}最多选择 100 人`
  return null
}

export function fieldRules(field: FormField): FieldRule[] {
  if (field.type === 'Description') return []
  return [
    {
      // required 只用于显示星号；实际校验在 validator 中完成，与服务端提示一致
      required: field.type === 'Table' ? false : !!field.required,
      trigger: textTypes.has(field.type) || numberTypes.has(field.type) ? 'blur' : 'change',
      validator: (_rule, value, callback) => {
        const error = validateValue(field, value)
        if (error) callback(new Error(error))
        else callback()
      },
    },
  ]
}

// ───── 保存前整理（只保留与类型相关的属性，保持定义简洁） ─────

export function cleanField(field: FormField, isColumn = false): FormField {
  const result: FormField = { key: field.key.trim(), label: field.label.trim(), type: field.type }
  if (field.required && field.type !== 'Description') result.required = true
  if (field.placeholder?.trim()) result.placeholder = field.placeholder.trim()
  if (field.help?.trim()) result.help = field.help.trim()
  if (!isColumn && field.span && field.span !== 24) result.span = field.span
  if (field.defaultValue !== undefined && field.defaultValue !== null && field.defaultValue !== '' && field.type !== 'Table')
    result.defaultValue = cloneField(field.defaultValue)
  if (optionTypes.has(field.type)) result.options = (field.options ?? []).map((o) => ({ label: o.label.trim(), value: o.value.trim() }))
  if (numberTypes.has(field.type)) {
    if (field.min !== null && field.min !== undefined) result.min = field.min
    if (field.max !== null && field.max !== undefined) result.max = field.max
    if (field.precision !== null && field.precision !== undefined) result.precision = field.precision
    if (isColumn && field.sum) result.sum = true
  }
  if (textTypes.has(field.type) && field.maxLength) result.maxLength = field.maxLength
  if (field.type === 'Table') {
    if (field.maxRows) result.maxRows = field.maxRows
    result.columns = (field.columns ?? []).map((c) => cleanField(c, true))
  }
  return result
}

export function cleanSchema(schema: FormSchema): FormSchema {
  return { fields: schema.fields.map((f) => cleanField(f)) }
}

export function cleanSettings(settings: FlowSettings): FlowSettings | undefined {
  const result: FlowSettings = {}
  if (settings.startable) result.startable = true
  if (settings.category?.trim()) result.category = settings.category.trim()
  if (settings.description?.trim()) result.description = settings.description.trim()
  if (settings.initiatorRoles?.length) result.initiatorRoles = [...settings.initiatorRoles]
  if (settings.titleTemplate?.trim()) result.titleTemplate = settings.titleTemplate.trim()
  return Object.keys(result).length ? result : undefined
}

/** 设计器本地检查：标识合法且不重复、名称、选项（其余由服务端校验） */
export function checkSchema(schema: FormSchema): string[] {
  const errors: string[] = []
  const seen = new Set<string>()
  const check = (field: FormField, owner: string) => {
    const name = `${owner}「${field.label || field.key}」`
    if (!field.label.trim()) errors.push(`${owner}字段 ${field.key} 缺少名称`)
    if (field.type !== 'Description' && !isValidFieldKey(field.key)) errors.push(`${name}的标识只能包含字母、数字和下划线，且不能以数字开头`)
    if (optionTypes.has(field.type) && !(field.options ?? []).length) errors.push(`${name}没有选项`)
  }
  for (const field of schema.fields) {
    check(field, '表单字段')
    if (field.type !== 'Description') {
      if (seen.has(field.key.toLowerCase())) errors.push(`表单字段标识 ${field.key} 重复`)
      seen.add(field.key.toLowerCase())
    }
    if (field.type === 'Table') {
      if (!(field.columns ?? []).length) errors.push(`明细表「${field.label}」没有列`)
      for (const column of field.columns ?? []) check(column, `明细表「${field.label}」的列`)
    }
  }
  return errors
}

/** 从流程定义 JSON 中取表单结构（无表单时返回 undefined） */
export function formOfDefinition(definitionJson: string | undefined): { form?: FormSchema; settings?: FlowSettings } {
  if (!definitionJson) return {}
  try {
    const parsed = JSON.parse(definitionJson) as { form?: FormSchema; settings?: FlowSettings }
    return { form: parsed.form?.fields?.length ? parsed.form : undefined, settings: parsed.settings }
  } catch {
    return {}
  }
}

export function parseFormValues(json: string | undefined): FormValues {
  try {
    const value = JSON.parse(json ?? '{}')
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as FormValues) : {}
  } catch {
    return {}
  }
}
