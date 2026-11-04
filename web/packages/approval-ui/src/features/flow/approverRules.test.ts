import { describe, expect, it } from 'vitest'
import type { ApproverTypeMeta } from '../../contracts/api'
import { approverRuleText, approverSummary, createNodeData, formatLevel, levelRuleText, parseLevel } from './flowModel'

describe('组织架构审批人参数', () => {
  it('解析与格式化级数（可带 @字段）', () => {
    expect(parseLevel('2')).toEqual({ level: 2, field: null })
    expect(parseLevel(' 1@payee ')).toEqual({ level: 1, field: 'payee' })
    expect(parseLevel('0')).toBeNull()
    expect(parseLevel('11')).toBeNull()
    expect(parseLevel('1@')).toBeNull()
    expect(parseLevel('')).toBeNull()
    expect(formatLevel(3, null)).toBe('3')
    expect(formatLevel(2, 'payee')).toBe('2@payee')
  })

  it('说明文字', () => {
    expect(levelRuleText('DeptLeader', '1')).toBe('所在部门负责人')
    expect(levelRuleText('DeptLeader', '3')).toBe('上 2 级部门负责人')
    expect(levelRuleText('Manager', '1')).toBe('直属上级')
    expect(levelRuleText('Manager', '2')).toBe('第 2 级上级')
    expect(levelRuleText('ManagerChain', '2@payee', (k) => (k === 'payee' ? '收款人' : k))).toBe('逐级审批至第 2 级上级（基准：收款人）')
    expect(levelRuleText('Manager', 'x')).toBe('上级：x')
  })

  it('规则摘要优先使用元数据中的显示名与参数类型', () => {
    const types: ApproverTypeMeta[] = [
      { type: 'Project', displayName: '项目经理', valueKind: 'Text', help: null, options: null, ordered: false },
      { type: 'Owner', displayName: '负责人', valueKind: 'None', help: null, options: null, ordered: false },
    ]
    expect(approverRuleText({ type: 'Project', value: 'P-1' }, types)).toBe('项目经理：P-1')
    expect(approverRuleText({ type: 'Owner', value: '' }, types)).toBe('负责人')
    expect(approverRuleText({ type: 'Initiator', value: '' })).toBe('发起人')
    expect(approverRuleText({ type: 'Role', value: '' })).toBe('角色：未设置')

    const node = { ...createNodeData('Approval'), mode: 'Sequential' as const, approvers: [{ type: 'ManagerChain', value: '2' }] }
    expect(approverSummary(node)).toBe('依次审批 · 逐级审批至第 2 级上级')
  })
})
