import { describe, expect, it } from 'vitest'
import { createSubmissionIdentity } from '@/features/certificates/submissionIdentity'

describe('注册证提交标识', () => {
  it('相同内容重试保持同一标识', () => {
    let sequence = 0
    const identity = createSubmissionIdentity(() => `key-${++sequence}`)
    expect(identity.forPayload({ name: '注册证' })).toBe('key-1')
    expect(identity.forPayload({ name: '注册证' })).toBe('key-1')
    expect(sequence).toBe(1)
  })
  it('内容变化或新申请会生成新标识', () => {
    let sequence = 0
    const identity = createSubmissionIdentity(() => `key-${++sequence}`)
    expect(identity.forPayload({ name: '版本一' })).toBe('key-1')
    expect(identity.forPayload({ name: '版本二' })).toBe('key-2')
    identity.reset()
    expect(identity.forPayload({ name: '版本二' })).toBe('key-3')
  })
})
