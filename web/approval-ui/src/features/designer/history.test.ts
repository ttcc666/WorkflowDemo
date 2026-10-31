import { describe, expect, it } from 'vitest'
import { createHistory } from './history'

describe('history', () => {
  it('undoes and redoes snapshots', () => {
    const h = createHistory()
    h.reset('a')
    h.record('b', undefined, 0)
    h.record('c', undefined, 5000)
    expect(h.undo()).toBe('b')
    expect(h.undo()).toBe('a')
    expect(h.undo()).toBeNull()
    expect(h.redo()).toBe('b')
    h.record('d', undefined, 9000) // 新修改清空重做栈
    expect(h.canRedo).toBe(false)
    expect(h.undo()).toBe('b')
  })

  it('ignores unchanged snapshots and coalesces typing', () => {
    const h = createHistory({ coalesceMs: 1000 })
    h.reset('a')
    expect(h.record('a')).toBe(false)
    h.record('ab', 'name', 0)
    h.record('abc', 'name', 500)
    h.record('abcd', 'name', 900)
    expect(h.undo()).toBe('a')
  })

  it('respects the limit', () => {
    const h = createHistory({ limit: 2 })
    h.reset('0')
    for (let i = 1; i <= 5; i++) h.record(String(i), undefined, i * 10000)
    expect(h.undo()).toBe('4')
    expect(h.undo()).toBe('3')
    expect(h.undo()).toBeNull()
  })
})
