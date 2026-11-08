import { Card } from './ui/primitives'
import { useState } from 'react'
import type { useCommandIntent } from '../hooks/useCommandIntent'
import { Button, ConfirmDialog, ErrorState } from './ui/primitives'
export function CommandRecovery({
  intent,
  busy,
  recovered,
}: {
  intent: ReturnType<typeof useCommandIntent>
  busy?: boolean
  recovered: () => void
}) {
  const [recovering, setRecovering] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [error, setError] = useState<unknown>(null)
  if (!intent.pending) return null
  async function retry() {
    if (busy || recovering) return
    setRecovering(true)
    setError(null)
    try {
      await intent.retry()
      recovered()
    } catch (cause) {
      setError(cause)
    } finally {
      setRecovering(false)
    }
  }
  return (
    <Card>
      <strong>上次提交结果尚未确认</strong>
      <p>
        {intent.canRetry()
          ? '可按原内容和原键重试。'
          : '当前页面已无原请求正文，请核对已提交记录并恢复原内容。'}
      </p>
      <div className="ar-row">
        {intent.canRetry() && (
          <Button
            busy={recovering}
            disabled={busy}
            onClick={() => void retry()}
          >
            按原内容重试上次操作
          </Button>
        )}
        <Button disabled={busy || recovering} onClick={() => setConfirm(true)}>
          核对结果后开始新操作
        </Button>
      </div>
      <ErrorState error={error} />
      <ConfirmDialog
        open={confirm}
        onOpenChange={setConfirm}
        title="开始新操作"
        confirm={() => {
          intent.reset(intent.pending!)
          setConfirm(false)
        }}
      >
        确认已核对原操作结果，且当前操作是新的业务意图。新键可能产生另一条业务记录。
      </ConfirmDialog>
    </Card>
  )
}
