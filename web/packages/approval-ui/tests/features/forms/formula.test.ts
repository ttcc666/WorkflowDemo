import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { parseFormula, roundTo } from '@ui/features/forms/formula'

interface FormulaCase {
  expr: string
  vars?: Record<string, number | null>
  result?: number | null
  precision?: number
  error?: boolean
}

// 与后端 FormulaTests 共用的用例（tests/shared/formula-cases.json）
const { cases } = JSON.parse(
  readFileSync(new URL('../../../../../../tests/shared/formula-cases.json', import.meta.url), 'utf-8'),
) as { cases: FormulaCase[] }

describe('公式（共享用例）', () => {
  it('用例已加载', () => expect(cases.length).toBeGreaterThan(50))

  it.each(cases.map((c, i) => [i, c] as const))('#%i %o', (_, c) => {
    const parsed = parseFormula(c.expr)
    if (c.error) {
      expect(parsed.error, `${c.expr} 应解析失败`).toBeTruthy()
      return
    }
    expect(parsed.error, c.expr).toBeUndefined()
    const vars = new Map(Object.entries(c.vars ?? {}).map(([k, v]) => [k.toLowerCase(), v]))
    const value = parsed.formula!.evaluate((name) => vars.get(name.toLowerCase()))
    expect(value === null ? null : roundTo(value, c.precision ?? 6)).toBe(c.result ?? null)
  })
})

describe('公式辅助', () => {
  it('收集引用的字段（不区分大小写去重）', () => {
    const { formula } = parseFormula('if(Total > 100, total - ifnull(advance, 0), round(qty * price, 2))')
    expect(formula?.identifiers).toEqual(['Total', 'advance', 'qty', 'price'])
  })

  it('四舍五入远离零，且不返回 -0', () => {
    expect(roundTo(1.005, 2)).toBe(1.01)
    expect(roundTo(-1.005, 2)).toBe(-1.01)
    expect(Object.is(roundTo(-0.001, 2), 0)).toBe(true)
  })
})
