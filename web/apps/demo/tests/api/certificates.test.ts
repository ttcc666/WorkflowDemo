import { beforeEach, describe, expect, it, vi } from 'vitest'
import { approval } from '@/approval'
import { certificateApi } from '@/api/certificates'

vi.mock('@/approval', () => ({ approval: { http: { get: vi.fn(), post: vi.fn(), put: vi.fn() } } }))
const http = approval.http
beforeEach(() => vi.clearAllMocks())

const data = { certNo: 'REG-1', name: '注册证', holder: '企业', certType: 'Normal', issueDate: '2026-01-01', expireDate: '2029-01-01' }
describe('注册证 API 请求', () => {
  it('新增重试发送稳定标识和来源申请', async () => {
    await certificateApi.submitCreate(data, 'same-key', 'previous')
    await certificateApi.submitCreate(data, 'same-key', 'previous')
    expect(http.post).toHaveBeenCalledTimes(2)
    expect(http.post).toHaveBeenNthCalledWith(1, '/api/certificates', { ...data, sourceRequestId: 'previous' }, { 'Idempotency-Key': 'same-key' })
    expect(http.post).toHaveBeenNthCalledWith(2, '/api/certificates', { ...data, sourceRequestId: 'previous' }, { 'Idempotency-Key': 'same-key' })
  })
  it('变更带上提交标识', async () => {
    await certificateApi.submitUpdate('c1', data, 'update-key')
    expect(http.put).toHaveBeenCalledWith('/api/certificates/c1', { ...data, sourceRequestId: undefined }, { 'Idempotency-Key': 'update-key' })
  })
  it('注销带上原因、来源和提交标识', async () => {
    await certificateApi.submitRevoke('c1', '停用', 'revoke-key', 'r1')
    expect(http.post).toHaveBeenCalledWith('/api/certificates/c1/revoke', { reason: '停用', sourceRequestId: 'r1' }, { 'Idempotency-Key': 'revoke-key' })
  })
  it('列表传递分页与编码后的关键字', async () => {
    await certificateApi.list({ page: 2, pageSize: 50, keyword: '注册证 A' })
    const url = new URL(vi.mocked(http.get).mock.calls[0]![0], 'http://localhost')
    expect(url.searchParams.get('page')).toBe('2')
    expect(url.searchParams.get('pageSize')).toBe('50')
    expect(url.searchParams.get('keyword')).toBe('注册证 A')
  })
})
