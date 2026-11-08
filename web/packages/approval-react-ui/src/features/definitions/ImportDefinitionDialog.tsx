import { useEffect, useRef, useState } from 'react'
import { readDefinitionFile } from '@approval/shared'
import {
  useApprovalClient,
  useApprovalIdentity,
  useApprovalUser,
} from '../../client'
import { useCommandIntent } from '../../hooks/useCommandIntent'
import { CommandRecovery } from '../../components/CommandRecovery'
import {
  Button,
  Dialog,
  ErrorState,
  Field,
  Input,
} from '../../components/ui/primitives'

export function ImportDefinitionDialog({
  close,
  imported,
}: {
  close: () => void
  imported: (key: string) => void
}) {
  const client = useApprovalClient()
  const identity = useApprovalIdentity()
  const user = useApprovalUser()
  const intent = useCommandIntent('definition-import')
  const [file, setFile] = useState<Awaited<
    ReturnType<typeof readDefinitionFile>
  > | null>(null)
  const [key, setKey] = useState('')
  const [valid, setValid] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const revision = useRef(0)
  useEffect(() => {
    revision.current++
    setValid(false)
    setError(null)
    setBusy(false)
  }, [key, identity])
  useEffect(
    () => () => {
      revision.current++
    },
    [],
  )
  async function choose(selected?: File) {
    const current = ++revision.current
    setFile(null)
    setValid(false)
    setError(null)
    if (!selected) return
    setBusy(true)
    try {
      const parsed = await readDefinitionFile(selected)
      if (revision.current === current) setFile(parsed)
    } catch (cause) {
      if (revision.current === current) setError(cause)
    } finally {
      if (revision.current === current) setBusy(false)
    }
  }
  async function validate() {
    if (!file || !key || busy || !user.can('approval.definition.validate'))
      return
    const current = revision.current
    setBusy(true)
    setValid(false)
    setError(null)
    try {
      await client.api.validateDefinitionImport(key, file.packageBase64)
      if (revision.current === current) setValid(true)
    } catch (cause) {
      if (revision.current === current) setError(cause)
    } finally {
      if (revision.current === current) setBusy(false)
    }
  }
  async function submit() {
    if (
      !file ||
      busy ||
      !key ||
      !user.can('approval.definition.design') ||
      (!valid && !intent.hasPending(key))
    )
      return
    const current = revision.current
    setBusy(true)
    setError(null)
    try {
      const result = await intent.run(
        key,
        { key, packageBase64: file.packageBase64 },
        (body, id) =>
          client.api.importDefinitionDraft(body.key, body.packageBase64, id),
      )
      if (revision.current === current) imported(result.key)
    } catch (cause) {
      if (revision.current === current) setError(cause)
    } finally {
      if (revision.current === current) setBusy(false)
    }
  }
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busy) close()
      }}
      title="导入流程定义"
      footer={
        <>
          <Button
            disabled={
              busy || !file || !key || !user.can('approval.definition.validate')
            }
            onClick={() => void validate()}
          >
            导入预检查
          </Button>
          <Button
            variant="primary"
            busy={busy}
            disabled={!valid && !intent.hasPending(key)}
            onClick={() => void submit()}
          >
            确认导入
          </Button>
        </>
      }
    >
      <CommandRecovery
        intent={intent}
        busy={busy}
        recovered={() => {
          if (key) imported(key)
        }}
      />
      <Field label="流程定义文件">
        <Input
          type="file"
          aria-label="流程定义文件"
          accept=".json,application/json"
          disabled={busy}
          onChange={(event) => {
            void choose(event.target.files?.[0])
            event.target.value = ''
          }}
        />
      </Field>
      {file && (
        <p>
          来源：{file.source.name}（{file.source.key} · v{file.source.version}）
        </p>
      )}
      <Field label="目标流程标识">
        <Input
          aria-label="目标流程标识"
          value={key}
          maxLength={100}
          disabled={busy}
          onChange={(event) => setKey(event.target.value.trim())}
        />
      </Field>
      {valid && <p role="status">导入预检查通过</p>}
      <ErrorState error={error} />
    </Dialog>
  )
}
