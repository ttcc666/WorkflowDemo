import type { FormField, FormSchema, FormValues, FlowSettings } from '../../contracts/forms.js'
import { cloneField, numberTypes, numericTypes, optionTypes, textTypes } from './formFields.js'

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
  }
  if (numericTypes.has(field.type)) {
    if (field.precision !== null && field.precision !== undefined) result.precision = field.precision
    if (isColumn && field.sum) result.sum = true
  }
  if (field.type === 'Formula') {
    // 值由公式计算：不保存必填、默认值、占位提示
    result.formula = (field.formula ?? '').trim()
    delete result.required
    delete result.defaultValue
    delete result.placeholder
  }
  if (textTypes.has(field.type) && field.maxLength) result.maxLength = field.maxLength
  if (field.type === 'File') {
    if (field.maxFiles) result.maxFiles = field.maxFiles
    if (field.accept?.length) result.accept = [...field.accept]
    delete result.defaultValue
  }
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

/** 字段标识不区分大小写（与服务端一致） */
const keySet = (keys: string[]) => new Set(keys.map((k) => k.toLowerCase()))

/** 去掉指定字段（如当前用户看不到的隐藏字段）后的表单结构 */
export function withoutFields(schema: FormSchema, keys: string[]): FormSchema {
  const hidden = keySet(keys)
  return hidden.size ? { ...schema, fields: schema.fields.filter((f) => !hidden.has(f.key.toLowerCase())) } : schema
}

/** 取出指定字段的值（同意时只提交可编辑字段） */
export function pickValues(values: FormValues, keys: string[]): FormValues {
  const wanted = keySet(keys)
  return Object.fromEntries(Object.entries(values).filter(([key]) => wanted.has(key.toLowerCase())))
}

/** 指定字段中值有变化的字段（按 JSON 比较，按表单顺序） */
export function changedFields(schema: FormSchema | undefined, before: FormValues, after: FormValues, keys: string[]): FormField[] {
  const wanted = keySet(keys)
  const json = (value: unknown) => JSON.stringify(value ?? null)
  return (schema?.fields ?? []).filter((f) => wanted.has(f.key.toLowerCase()) && json(before[f.key]) !== json(after[f.key]))
}
