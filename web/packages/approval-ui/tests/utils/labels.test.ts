import { expect, it } from 'vitest'
import { dueText } from '@ui/utils/labels'

it.each([
  { case: 'before deadline', offset: 60000, overdue: false, text: '剩余 1 分钟' },
  { case: 'at deadline', offset: 0, overdue: true, text: '已超时 0 分钟' },
  { case: 'after deadline', offset: -60000, overdue: true, text: '已超时 1 分钟' },
])('dueText uses inclusive expiry: $case', ({ offset, overdue, text }) => {
  const now = Date.parse('2026-11-05T00:00:00Z')
  expect(dueText(new Date(now + offset).toISOString(), now)).toEqual({ overdue, text })
})
