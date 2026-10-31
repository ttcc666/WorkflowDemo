import { describe, expect, it } from 'vitest'
import {
  checkSchema,
  cleanField,
  cleanSettings,
  fieldVariables,
  initialValues,
  newField,
  newFieldKey,
  newRow,
  tableSum,
  userFields,
  validateValue,
  type FormField,
  type FormSchema,
} from './formModel'

const days: FormField = { key: 'days', label: '天数', type: 'Number', required: true, min: 0.5, max: 30, precision: 1 }
const amount: FormField = { key: 'amount', label: '金额', type: 'Money', required: true, sum: true }
const items: FormField = { key: 'items', label: '明细', type: 'Table', required: true, maxRows: 2, columns: [{ key: 'name', label: '项目', type: 'Text' }, amount] }

describe('字段标识与新字段', () => {
  it('生成不重复的标识（不区分大小写）', () => {
    expect(newFieldKey('Text', [])).toBe('text1')
    expect(newFieldKey('Text', ['text1', 'TEXT2'])).toBe('text3')
    expect(newFieldKey('MultiSelect', ['multiSelect1'])).toBe('multiSelect2')
  })

  it('新字段带类型相关的默认配置', () => {
    expect(newField('Select', []).options).toHaveLength(2)
    expect(newField('Money', []).precision).toBe(2)
    expect(newField('Table', []).columns?.some((c) => c.sum)).toBe(true)
    expect(newField('TextArea', []).span).toBe(24)
    expect(newField('Text', []).span).toBe(12)
  })
})

describe('初始值', () => {
  it('按类型给空值，默认值优先，保留已有数据', () => {
    const schema: FormSchema = {
      fields: [
        days,
        { key: 'agree', label: '同意', type: 'Switch' },
        { key: 'tags', label: '标签', type: 'MultiSelect', options: [{ label: 'A', value: 'a' }] },
        { key: 'type', label: '类型', type: 'Select', defaultValue: 'a', options: [{ label: 'A', value: 'a' }] },
        { key: 'tip', label: '说明', type: 'Description' },
        items,
      ],
    }

    expect(initialValues(schema)).toEqual({ days: null, agree: false, tags: [], type: 'a', items: [] })
    expect(initialValues(schema, { days: 3, extra: 1 })).toMatchObject({ days: 3, type: 'a' })
    expect(initialValues(schema, { days: 3 })).not.toHaveProperty('extra')
    expect(newRow(items)).toEqual({ name: null, amount: null })
  })
})

describe('校验（与服务端一致）', () => {
  it('必填、范围、精度', () => {
    expect(validateValue(days, null)).toBe('请填写天数')
    expect(validateValue(days, 0)).toBe('天数不能小于 0.5')
    expect(validateValue(days, 31)).toBe('天数不能大于 30')
    expect(validateValue(days, 1.25)).toBe('天数最多 1 位小数')
    expect(validateValue(days, 2.5)).toBeNull()
    expect(validateValue(amount, 10.123)).toBe('金额最多 2 位小数')
  })

  it('选择类、开关、文本长度、日期区间、行数', () => {
    expect(validateValue({ key: 'u', label: '审批人', type: 'User', required: true }, '')).toBe('请选择审批人')
    expect(validateValue({ key: 's', label: '承诺', type: 'Switch', required: true }, false)).toBe('请勾选承诺')
    expect(validateValue({ key: 't', label: '标题', type: 'Text', maxLength: 3 }, 'abcd')).toBe('标题不能超过 3 个字符')
    expect(validateValue({ key: 'r', label: '期间', type: 'DateRange' }, ['2026-02-01', '2026-01-01'])).toBe('期间的开始日期不能晚于结束日期')
    expect(validateValue(items, [])).toBe('请至少添加一行明细')
    expect(validateValue(items, [{}, {}, {}])).toBe('明细最多 2 行')
  })
})

describe('变量与人员字段', () => {
  const schema: FormSchema = { fields: [days, { key: 'handover', label: '交接人', type: 'User' }, items] }

  it('条件 / 模板可用字段含明细合计', () => {
    expect(fieldVariables(schema).map((v) => v.name)).toEqual(['days', 'handover', 'items_amount_sum'])
    expect(userFields(schema).map((f) => f.key)).toEqual(['handover'])
  })

  it('明细合计忽略空值与非数字', () => {
    expect(tableSum([{ amount: 1.1 }, { amount: '2.2' }, { amount: null }, { amount: 'x' }], 'amount')).toBe(3.3)
  })
})

describe('保存前整理与本地检查', () => {
  it('只保留与类型相关的属性', () => {
    const cleaned = cleanField({ key: ' title ', label: '标题', type: 'Text', options: [{ label: 'x', value: 'x' }], min: 1, span: 24, required: false })
    expect(cleaned).toEqual({ key: 'title', label: '标题', type: 'Text' })
    expect(cleanField(items).columns?.[1]).toEqual({ key: 'amount', label: '金额', type: 'Money', required: true, sum: true })
    expect(cleanSettings({ startable: false, initiatorRoles: [], titleTemplate: ' ' })).toBeUndefined()
    expect(cleanSettings({ startable: true, category: ' 人事 ' })).toEqual({ startable: true, category: '人事' })
  })

  it('标识非法、重复、缺少选项', () => {
    const errors = checkSchema({
      fields: [
        { key: '1a', label: '甲', type: 'Text' },
        { key: 'b', label: '乙', type: 'Text' },
        { key: 'B', label: '丙', type: 'Select', options: [] },
      ],
    })
    expect(errors.join('\n')).toMatch(/「甲」的标识只能包含/)
    expect(errors.join('\n')).toMatch(/标识 B 重复/)
    expect(errors.join('\n')).toMatch(/「丙」没有选项/)
  })
})
