import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../components/shadcn/accordion";
import { useEffect, useState } from "react";
import { Link } from "react-router";
import { RefreshCw, Search } from "lucide-react";
import { PageHeader } from "../components/ui/PageHeader";
import {
  formatDateTime,
  outboxStatusLabels,
  type OutboxMessage,
} from "@approval/shared";
import { useApprovalClient, useApprovalUser } from "../client";
import { usePagedList } from "../hooks/usePagedList";
import { useAsyncData } from "../hooks/useAsyncData";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "../components/CommandRecovery";
import {
  Badge,
  Button,
  Card,
  Dialog,
  EmptyState,
  ErrorState,
  Field,
  Input,
  Loading,
  Pagination,
  Select,
} from "../components/ui/primitives";
import { DataTable } from "../components/ui/DataTable";
import { eventLabel, targetLabel } from "../features/monitor/codeLabels";

function formatPayload(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

export default function OutboxPage() {
  const client = useApprovalClient();
  const user = useApprovalUser();
  const list = usePagedList(
    (query, signal) => client.api.outbox(query, signal),
    "",
    user.can("approval.outbox.read"),
  );
  const intent = useCommandIntent("outbox-retry", { permission: "approval.outbox.retry" });
  const [selected, setSelected] = useState<OutboxMessage | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const payload = useAsyncData(
    (signal) => client.api.outboxPayload(selected!.id, signal),
    [selected?.id],
    !!selected && user.can("approval.instance.read-sensitive"),
  );
  const canRead = user.can("approval.outbox.read");
  const { loading: listLoading, reload: reloadList } = list;
  useEffect(() => {
    if (!canRead || listLoading) return;
    let timer: number | undefined;
    let refreshing = false;
    const available = () =>
      document.visibilityState === "visible" && navigator.onLine;
    function clear() {
      if (timer !== undefined) window.clearTimeout(timer);
      timer = undefined;
    }
    function refresh() {
      clear();
      if (available() && !refreshing) {
        refreshing = true;
        reloadList();
      }
    }
    function changed() {
      clear();
      if (available()) refresh();
    }
    if (available()) timer = window.setTimeout(refresh, 3000);
    document.addEventListener("visibilitychange", changed);
    window.addEventListener("online", changed);
    window.addEventListener("offline", clear);
    return () => {
      clear();
      document.removeEventListener("visibilitychange", changed);
      window.removeEventListener("online", changed);
      window.removeEventListener("offline", clear);
    };
  }, [canRead, listLoading, reloadList]);
  async function retry(message: OutboxMessage) {
    if (busy || !user.can("approval.outbox.retry") || message.status === "Succeeded") return;
    setBusy(message.id);
    try {
      await intent.run(message.id, {}, (_, id) =>
        client.api.retryOutbox(message.id, id),
      );
      list.reload();
    } catch (cause) {
      setError(cause);
    } finally {
      setBusy(null);
    }
  }
  const Panel = client.options().outboxPanel;
  if (!user.can("approval.outbox.read"))
    return (
      <Card>
        <EmptyState text="没有投递记录查看权限" />
        <Link className="ar-text-link" to="../workbench">
          返回审批工作台
        </Link>
      </Card>
    );
  return (
    <div className="ar-product-page ar-outbox-page">
      <PageHeader
        title="Outbox 投递记录"
        eyebrow="流程管理"
        description="查看事件投递状态、重试记录与关联审批。页面可见时自动刷新。"
        actions={
          <Button onClick={list.reload}>
            <RefreshCw data-icon="inline-start" />
            刷新
          </Button>
        }
      />
      <CommandRecovery
        intent={intent}
        busy={!!busy}
        recovered={() => {
          setError(null);
          list.reload();
        }}
      />
      <form
        className="ar-page-filter-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          list.search();
        }}
      >
        <Field label="投递状态">
          <Select
            aria-label="投递状态"
            value={list.filters.status}
            placeholder="全部状态"
            options={Object.entries(outboxStatusLabels).map(
              ([value, label]) => ({ value, label: label.text }),
            )}
            onValueChange={(nextValue) =>
              list.search({ ...list.filters, status: nextValue })
            }
          />
        </Field>
        <Field label="关联实例">
          <Input
            aria-label="实例 ID"
            placeholder="输入实例 ID"
            value={list.filters.instanceId ?? ""}
            onChange={(event) =>
              list.setFilters({
                ...list.filters,
                instanceId: event.target.value,
              })
            }
          />
        </Field>
        <div className="ar-row">
          <Button type="submit" variant="primary">
            <Search data-icon="inline-start" />
            搜索
          </Button>
          <Button type="button" onClick={list.reset}>
            重置
          </Button>
        </div>
      </form>
      <ErrorState error={error || list.error} retry={list.reload} />
      <Card
        title="投递队列"
        className="ar-outbox-queue"
        description="失败原因、尝试次数与下次尝试时间由服务端投递记录提供。"
      >
        <DataTable
          rows={list.items}
          rowKey={(row) => row.id}
          loading={list.loading}
          empty="暂无投递记录"
          columns={[
            {
              key: "target",
              label: "投递事项",
              render: (row) => (
                <div className="ar-cell-stack">
                  <strong>{targetLabel(row.target)}</strong>
                  <span>
                    {row.description ? eventLabel(row.description) : "—"}
                  </span>
                  <small className="ar-cell-muted ar-outbox-secondary">
                    {row.id}
                  </small>
                </div>
              ),
            },
            {
              key: "instance",
              label: "关联审批",
              render: (row) => (
                <div className="ar-cell-stack">
                  {row.instanceId ? (
                    <Link
                      className="ar-text-link"
                      to={"../approvals/" + row.instanceId}
                    >
                      审批详情
                    </Link>
                  ) : (
                    <span>—</span>
                  )}
                  <small className="ar-cell-muted">
                    节点：{row.nodeId || "—"}
                  </small>
                </div>
              ),
            },
            {
              key: "status",
              label: "状态 / 尝试",
              render: (row) => (
                <div className="ar-cell-stack">
                  <Badge status={row.status}>
                    {outboxStatusLabels[row.status]?.text ?? row.status}
                  </Badge>
                  <small className="ar-cell-muted">
                    {row.attempts} / {row.maxAttempts} 次尝试
                  </small>
                </div>
              ),
            },
            {
              key: "time",
              label: "时间安排",
              render: (row) => (
                <div className="ar-cell-stack">
                  <span>创建 {formatDateTime(row.createdAt)}</span>
                  <small className="ar-cell-muted ar-outbox-secondary">
                    下次 {formatDateTime(row.nextAttemptAt)}
                  </small>
                </div>
              ),
            },
            {
              key: "error",
              label: "最后错误",
              render: (row) =>
                row.lastError || <span data-empty="true">—</span>,
            },
            {
              key: "action",
              label: "操作",
              render: (row) => (
                <div className="ar-row">
                  {user.can("approval.outbox.retry") &&
                    row.status !== "Succeeded" && (
                      <Button
                        size="sm"
                        busy={busy === row.id}
                        onClick={() => void retry(row)}
                      >
                        重试
                      </Button>
                    )}
                  {user.can("approval.instance.read-sensitive") && (
                    <Button size="sm" onClick={() => setSelected(row)}>
                      查看 Payload
                    </Button>
                  )}
                  <Accordion
                    type="single"
                    collapsible
                    className="ar-outbox-mobile-details"
                  >
                    <AccordionItem value="details">
                      <AccordionTrigger aria-label={`投递详情 ${row.id}`}>
                        更多信息
                      </AccordionTrigger>
                      <AccordionContent>
                        <dl className="ar-cell-stack">
                          <dt>投递 ID</dt>
                          <dd>{row.id}</dd>
                          <dt>目标 / 事件代码</dt>
                          <dd>
                            {row.target} · {row.description || "—"}
                          </dd>
                          <dt>节点</dt>
                          <dd>{row.nodeId || "—"}</dd>
                          <dt>下次尝试</dt>
                          <dd>{formatDateTime(row.nextAttemptAt)}</dd>
                        </dl>
                      </AccordionContent>
                    </AccordionItem>
                  </Accordion>
                </div>
              ),
            },
          ]}
          error={list.error}
        />
        <Pagination
          page={list.page}
          pageSize={list.pageSize}
          total={list.total}
          onChange={list.paginate}
          loading={list.loading}
        />
      </Card>
      {Panel && <Panel />}
      <Dialog
        open={!!selected}
        onOpenChange={(open) => {
          if (!open) setSelected(null);
        }}
        title="投递 Payload"
        wide
      >
        <ErrorState error={payload.error} retry={payload.reload} />
        {payload.loading && !payload.data && <Loading />}
        {payload.data && (
          <>
            <pre
              aria-label="投递内容"
              className="ar-code-block"
              style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}
            >
              {formatPayload(payload.data.payloadJson)}
            </pre>
            <Accordion type="single" collapsible>
              <AccordionItem
                value="content"
                className="approval:group/disclosure"
              >
                <AccordionTrigger>原始内容</AccordionTrigger>
                <AccordionContent
                  forceMount
                  className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
                >
                  <div className="approval:flex approval:flex-col approval:gap-4">
                    <pre
                      aria-label="原始投递内容"
                      className="ar-code-block"
                      style={{
                        whiteSpace: "pre-wrap",
                        overflowWrap: "anywhere",
                      }}
                    >
                      {payload.data.payloadJson}
                    </pre>
                  </div>
                </AccordionContent>
              </AccordionItem>
            </Accordion>
          </>
        )}
        <small>敏感读取权限与实例访问策略由服务端再次检查。</small>
      </Dialog>
    </div>
  );
}
