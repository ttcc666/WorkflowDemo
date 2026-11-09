import { useRef, useState } from 'react'
import type { useCommandIntent } from '../hooks/useCommandIntent'
import { useApprovalIdentity } from '../client'
import { Button, Card, ConfirmDialog, ErrorState } from './ui/primitives'
type RecoveryProps = {
  intent: ReturnType<typeof useCommandIntent>
  scope?: string | null
  busy?: boolean
  recovered: (value: unknown, scope: string) => void
  onRecoveringChange?: (recovering: boolean) => void
  onReset?: (scope: string) => void
  description?: string
}
export function CommandRecovery(props: RecoveryProps) {
  const identity = useApprovalIdentity()
  return <CommandRecoveryList {...props} key={identity} />
}
function CommandRecoveryList(props: RecoveryProps) {
  const scopes = props.scope === undefined ? props.intent.pendingScopes() : props.scope === null ? [] : [props.scope]
  const recoveringScopes = useRef(new Set<string>())
  return <>{scopes.map(scope => <CommandRecoveryItem {...props} key={scope} scope={scope}
    onRecoveringChange={recovering => {
      if (recovering) recoveringScopes.current.add(scope); else recoveringScopes.current.delete(scope)
      if (props.intent.isCurrent()) props.onRecoveringChange?.(recoveringScopes.current.size > 0)
    }} />)}</>
}
function CommandRecoveryItem({
  intent,
  scope,
  busy,
  recovered,
  onRecoveringChange,
  onReset,
  description,
}: RecoveryProps & { scope: string }) {
  const [recovering, setRecovering] = useState(false)
  const retrying = useRef(false)
  const [confirmScope, setConfirmScope] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  async function retry() {
    if (busy || retrying.current || !intent.isCurrent() || !intent.canRetry(scope))
      return
    retrying.current = true
    setRecovering(true)
    onRecoveringChange?.(true)
    setError(null)
    try {
      const result = await intent.retry(scope)
      if (intent.isCurrent()) recovered(result, scope)
    } catch (cause) {
      if (intent.isCurrent()) setError(cause)
    } finally {
      retrying.current = false
      setRecovering(false)
      onRecoveringChange?.(false)
    }
  }
  return (
    <Card>
      <strong>上次提交结果尚未确认</strong>
      <p>操作：{scope}</p>
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
          if (busy || retrying.current || confirmScope !== scope || !intent.isCurrent()) return
          intent.reset(scope)
          setConfirmScope(null)
          setError(null)
          onReset?.(scope)
        }}
      >
        确认已核对原操作结果，且当前操作是新的业务意图。新键可能产生另一条业务记录。
      </ConfirmDialog>
    </Card>
  )
}
