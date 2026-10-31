import { describe, expect, it } from 'vitest'
import { applyFormulas, checkSchema, cleanField, formulaOrder, numericVariables, type FormField, type FormSchema, type FormValues } from './formModel'

const schema: FormSchema = {
  fields: [
    {
      key: 'items',
      label: '明细',
      type: 'Table',
      columns: [
        { key: 'qty', label: '数量', type: 'Number' },
        { key: 'price', label: '单价', type: 'Money' },
        { key: 'amount', label: '金额', type: 'Formula', formula: 'qty * price', sum: true },
      ],
    },
    { key: 'advance', label: '预付', type: 'Money' },
    // 依赖排在后面的 payable：按依赖顺序计算
    { key: 'discounted', label: '折后', type: 'Formula', formula: 'round(payable * 0.9, 2)' },
    { key: 'payable', label: '应付', type: 'Formula', formula: 'Items_Amount_Sum - ifnull(advance, 0)', precision: 1 },
  ],
}

describe('计算字段（与服务端一致）', () => {
  it('明细列逐行计算 → 合计 → 顶层公式（依赖顺序），只在变化时写回', () => {
    const values: FormValues = { items: [{ qty: 3, price: 2.5, amount: 999 }, { qty: '2', price: 10 }], advance: 5 }

    expect(applyFormulas(schema, values)).toBe(true)
    expect((values.items as FormValues[]).map((r) => r.amount)).toEqual([7.5, 20])
    expect(values.payable).toBe(22.5)
    expect(values.discounted).toBe(20.25)
    expect(applyFormulas(schema, values)).toBe(false)
  })

  it('空值参与运算为空，ifnull 提供默认值', () => {
    const values: FormValues = { items: [{ qty: null, price: 3 }], advance: null }

    applyFormulas(schema, values)

    expect((values.items as FormValues[])[0]!.amount).toBeNull()
    expect(values.payable).toBe(0)
  })

  it('循环引用或无效公式不计算并置空', () => {
    const cyclic: FormField[] = [
      { key: 'a', label: 'A', type: 'Formula', formula: 'b + 1' },
      { key: 'b', label: 'B', type: 'Formula', formula: 'a + 1' },
      { key: 'c', label: 'C', type: 'Formula', formula: '1 +' },
    ]
    const values: FormValues = { a: 1, b: 2, c: 3 }

    expect(formulaOrder(cyclic)).toEqual([])
    applyFormulas({ fields: cyclic }, values)
    expect(values).toEqual({ a: null, b: null, c: null })
  })

  it('可引用的数值变量含计算字段与明细合计', () => {
    expect(numericVariables(schema).map((v) => v.name)).toEqual(['items_amount_sum', 'advance', 'discounted', 'payable'])
  })

  it('保存时保留公式与精度，去掉必填与默认值；本地检查公式语法', () => {
    expect(cleanField({ key: 'p', label: '应付', type: 'Formula', formula: ' a + 1 ', precision: 1, required: true, defaultValue: 3 })).toEqual({
      key: 'p',
      label: '应付',
      type: 'Formula',
      precision: 1,
      formula: 'a + 1',
    })
    expect(checkSchema({ fields: [{ key: 'p', label: '应付', type: 'Formula', formula: 'a +' }] }).join()).toMatch(/「应付」的公式有误/)
  })
})
