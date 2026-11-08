import { Card } from "./ui/primitives";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router";
import { formatDateTime, type ApprovalMessage } from "@approval/shared";
import {
  useApprovalClient,
  useApprovalIdentity,
  useApprovalUser,
} from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "./CommandRecovery";
import {
  Badge,
  Button,
  Dialog,
  EmptyState,
  ErrorState,
  Loading,
} from "./ui/primitives";
export function MessageBell({
  detailPath = (id) => "approvals/" + id,
}: { detailPath?: (instanceId: string) => string } = {}) {
  const client = useApprovalClient();
  const user = useApprovalUser();
  const identity = useApprovalIdentity();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const count = useAsyncData(
    (signal) => client.api.unreadCount(signal),
    [],
    user.can("approval.message.read"),
  );
  const messages = useAsyncData(
    (signal) => client.api.messages(false, signal),
    [open],
    open && user.can("approval.message.read"),
  );
  const intent = useCommandIntent("message-read");
  useEffect(() => {
    setOpen(false);
    setError(null);
    setBusy(false);
  }, [identity]);
  useEffect(() => {
    const timer = window.setInterval(count.reload, 10000);
    return () => window.clearInterval(timer);
  }, [client]);
  function refresh() {
    count.reload();
    messages.reload();
  }
  async function read(message?: ApprovalMessage) {
    if (busy) return;
    const owner = client.generation;
    setBusy(true);
    setError(null);
    try {
      if (!message || !message.isRead) {
        if (!user.can("approval.message.write")) return;
        await intent.run(message?.id ?? "read-all", {}, (_, id) =>
          message
            ? client.api.markRead(message.id, id)
            : client.api.markAllRead(id),
        );
      }
      if (owner !== client.generation) return;
      refresh();
      if (message?.instanceId) {
        setOpen(false);
        navigate(detailPath(message.instanceId));
      }
    } catch (cause) {
      if (owner === client.generation) setError(cause);
    } finally {
      if (owner === client.generation) setBusy(false);
    }
  }
  if (!user.can("approval.message.read")) return null;
  return (
    <>
      <Button
        aria-label="消息通知"
        onClick={() => {
          setOpen(true);
          refresh();
        }}
      >
        消息 <Badge>{count.data?.count ?? "—"}</Badge>
      </Button>
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title="消息通知"
        footer={
          user.can("approval.message.write") && (
            <Button disabled={busy} onClick={() => void read()}>
              全部标记已读
            </Button>
          )
        }
      >
        <CommandRecovery intent={intent} busy={busy} recovered={refresh} />
        <ErrorState
          error={error || messages.error || count.error}
          retry={refresh}
        />
        {messages.loading ? (
          <Loading />
        ) : !messages.error && !messages.data?.length ? (
          <EmptyState text="暂无消息" />
        ) : (
          messages.data?.map((message) => (
            <Card
              key={message.id}
              className="ar-message-card"
              action={!message.isRead && <Badge status="Pending">未读</Badge>}
              title={
                <Button
                  className="ar-message-title"
                  variant="ghost"
                  disabled={
                    busy ||
                    (!message.isRead && !user.can("approval.message.write"))
                  }
                  onClick={() => void read(message)}
                >
                  {message.title}
                </Button>
              }
              description={formatDateTime(message.createdAt)}
            >
              <p className="ar-message-body">{message.content}</p>
            </Card>
          ))
        )}
      </Dialog>
    </>
  );
}
