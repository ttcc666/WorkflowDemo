/** 同一弹窗内，网络失败后重试相同内容使用同一键；修改内容或开始新申请时生成新键。 */
export function createSubmissionIdentity(generate: () => string = () => crypto.randomUUID()) {
  let payload: string | undefined
  let key: string | undefined
  return {
    forPayload(value: unknown) {
      const serialized = JSON.stringify(value)
      if (!key || payload !== serialized) { key = generate(); payload = serialized }
      return key
    },
    reset() { payload = undefined; key = undefined },
  }
}
