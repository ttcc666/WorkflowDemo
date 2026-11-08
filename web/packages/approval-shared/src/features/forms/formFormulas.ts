import type { FormField, FormSchema, FormValues } from '../../contracts/forms.js'
import { sumKey, tableSum } from './formFields.js'
import { parseFormula, roundTo, type ParsedFormula } from './formula.js'

interface FormulaStep {
  field: FormField
  formula: ParsedFormula
}

/** 解析一组字段中的公式并按依赖排序（先算被依赖的）；公式无效或存在循环引用时不计算 */
export function formulaOrder(fields: FormField[]): FormulaStep[] {
  const parsed = new Map<string, FormulaStep>()
  for (const field of fields) {
    if (field.type !== 'Formula') continue
    const { formula } = parseFormula(field.formula)
    if (formula) parsed.set(field.key.toLowerCase(), { field, formula })
  }

  const order: FormulaStep[] = []
  const state = new Map<string, 'visiting' | 'done'>()
  const visit = (step: FormulaStep): boolean => {
    const key = step.field.key.toLowerCase()
    const current = state.get(key)
    if (current) return current === 'done'
    state.set(key, 'visiting')
    for (const name of step.formula.identifiers) {
      const dependency = parsed.get(name.toLowerCase())
      if (dependency && !visit(dependency)) return false
    }
    state.set(key, 'done')
    order.push(step)
    return true
  }
  for (const step of parsed.values()) if (!visit(step)) return []
  return order
}

function toNumber(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null
  if (typeof value === 'string' && value.trim() !== '' && Number.isFinite(Number(value))) return Number(value)
  return null
}

/** 不区分大小写取值（与服务端一致） */
function lookup(values: FormValues, name: string): unknown {
  if (name in values) return values[name]
  const key = Object.keys(values).find((k) => k.toLowerCase() === name.toLowerCase())
  return key === undefined ? undefined : values[key]
}

function compute(step: FormulaStep, variable: (name: string) => number | null) {
  const result = step.formula.evaluate(variable)
  return result === null ? null : roundTo(result, step.field.precision ?? 2)
}

/** 计算全部公式并写回 values（明细行的计算列、顶层计算字段），返回是否有变化；提交后服务端会重新计算 */
export function applyFormulas(schema: FormSchema | undefined, values: FormValues): boolean {
  let changed = false
  const set = (target: FormValues, key: string, value: number | null) => {
    if (target[key] !== value) {
      target[key] = value
      changed = true
    }
  }

  const fields = schema?.fields ?? []
  const sums = new Map<string, number>()
  for (const table of fields.filter((f) => f.type === 'Table')) {
    const columns = table.columns ?? []
    const steps = formulaOrder(columns)
    const rows = Array.isArray(values[table.key]) ? (values[table.key] as FormValues[]) : []
    for (const row of rows) {
      for (const column of columns) if (column.type === 'Formula' && !steps.some((s) => s.field === column)) set(row, column.key, null)
      for (const step of steps) set(row, step.field.key, compute(step, (name) => toNumber(lookup(row, name))))
    }
    for (const column of columns.filter((c) => c.sum)) sums.set(sumKey(table.key, column.key).toLowerCase(), tableSum(rows, column.key))
  }

  const steps = formulaOrder(fields)
  for (const field of fields) if (field.type === 'Formula' && !steps.some((s) => s.field === field)) set(values, field.key, null)
  for (const step of steps) {
    set(values, step.field.key, compute(step, (name) => toNumber(lookup(values, name)) ?? sums.get(name.toLowerCase()) ?? null))
  }
  return changed
}
