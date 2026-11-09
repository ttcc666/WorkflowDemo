import { useRef, useState } from 'react'
import type { useCommandIntent } from '../hooks/useCommandIntent'
import { Button, Card, ConfirmDialog, ErrorState } from './ui/primitives'
export function CommandRecovery({
  intent,
  scope = intent.pending,
  busy,
  recovered,
  onRecoveringChange,
  onReset,
  description,
}: {
  intent: ReturnType<typeof useCommandIntent>
  scope?: string | null
  busy?: boolean
  recovered: (value: unknown, scope: string) => void
  onRecoveringChange?: (recovering: boolean) => void
  onReset?: () => void
  description?: string
}) {
  const [recovering, setRecovering] = useState(false)
  const retrying = useRef(false)
  const [confirmScope, setConfirmScope] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  if (scope === null) return null
  async function retry() {
    if (scope === null || busy || retrying.current || !intent.canRetry(scope))
      return
    retrying.current = true
    setRecovering(true)
    onRecoveringChange?.(true)
    setError(null)
    try {
      const result = await intent.retry(scope)
      if (intent.isCurrent()) recovered(result, scope)
    } catch (cause) {
      setError(cause)
    } finally {
      retrying.current = false
      setRecovering(false)
      onRecoveringChange?.(false)
    }
  }
  return (
    <Card>
      <strong>上次提交结果尚未确认</strong>
      {description && <p>{description}</p>}
      <p role="status">恢复状态：{recovering ? '正在按原内容确认结果' : intent.canRetry(scope) ? '原请求仍在本页，可恢复' : '需要人工核对，禁止自动重试'}</p>
      <p>
        {intent.hasConflictingPending(scope)
          ? '检测到多笔未确认操作，请人工核对全部结果，禁止自动重试。'
          : intent.canRetry(scope)
          ? '可按原内容和原键重试。'
          : '当前页面已无原请求正文，请核对已提交记录。'}
      </p>
      <div className="ar-row">
        {intent.canRetry(scope) && (
          <Button
            busy={recovering}
            disabled={busy}
            onClick={() => void retry()}
          >
            按原内容重试上次操作
          </Button>
        )}
        <Button
          disabled={busy || recovering}
          onClick={() => setConfirmScope(scope)}
        >
          核对结果后开始新操作
        </Button>
      </div>
      <ErrorState error={error} />
      <ConfirmDialog
        open={confirmScope === scope}
        onOpenChange={(open) => {
          if (!open) setConfirmScope(null)
        }}
        title="开始新操作"
        busy={busy || recovering}
        confirm={() => {
          if (busy || retrying.current || confirmScope !== scope) return
          intent.reset(scope)
          setConfirmScope(null)
          setError(null)
          onReset?.()
        }}
      >
        确认已核对原操作结果，且当前操作是新的业务意图。新键可能产生另一条业务记录。
      </ConfirmDialog>
    </Card>
  )
}
