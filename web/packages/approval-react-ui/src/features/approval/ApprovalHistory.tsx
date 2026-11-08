import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";
import { useState } from "react";
import {
  actionLabels,
  formatDateTime,
  type ApprovalInstance,
  type CarbonCopyItem,
} from "@approval/shared";
import { useApprovalClient, useApprovalUser } from "../../client";
import { useAsyncData } from "../../hooks/useAsyncData";
import {
  Button,
  Card,
  Dialog,
  ErrorState,
  Pagination,
  Select,
} from "../../components/ui/primitives";
import { Link } from "react-router";
import { DataTable } from "../../components/ui/DataTable";
import {
  Item,
  ItemContent,
  ItemHeader,
  ItemTitle,
  ItemDescription,
} from "../../components/shadcn/item";

export function ApprovalTimeline({ instance }: { instance: ApprovalInstance }) {
  return (
    <Card title="审批轨迹">
      <ol className="approval:flex approval:flex-col approval:gap-3">
        {instance.logs.map((log) => (
          <li key={log.id}>
            <Item variant="outline" size="sm">
              <ItemContent>
                <ItemHeader>
                  <ItemTitle>
                    {actionLabels[log.action] ?? log.action}
                  </ItemTitle>
                  <time className="approval:text-xs approval:text-muted-foreground">
                    {formatDateTime(log.createdAt)}
                  </time>
                </ItemHeader>
                <ItemDescription>
                  {log.operatorId} · {log.nodeName ?? log.nodeId} {log.comment}
                </ItemDescription>
                {log.details && (
                  <Accordion type="single" collapsible>
                    <AccordionItem
                      value="content"
                      className="approval:group/disclosure"
                    >
                      <AccordionTrigger>字段变更 / 操作详情</AccordionTrigger>
                      <AccordionContent
                        forceMount
                        className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
                      >
                        <div className="approval:flex approval:flex-col approval:gap-4">
                          <pre
                            style={{ whiteSpace: "pre-wrap", overflow: "auto" }}
                          >
                            {log.details}
                          </pre>
                        </div>
                      </AccordionContent>
                    </AccordionItem>
                  </Accordion>
                )}
              </ItemContent>
            </Item>
          </li>
        ))}
      </ol>
    </Card>
  );
}
export function ActionExecutionList({
  instance,
}: {
  instance: ApprovalInstance;
}) {
  const user = useApprovalUser();
  const [node, setNode] = useState("");
  const [type, setType] = useState("");
  const [status, setStatus] = useState("");
  const logs = instance.logs
    .filter((log) => log.action === "ActionExecuted" && !!log.details)
    .flatMap((log) => {
      try {
        const detail = JSON.parse(log.details ?? "{}") as {
          id: string;
          type: string;
          mode: string;
          status: string;
          executionId?: string;
          outboxId?: string;
          durationMs?: number;
          failure?: string;
        };
        return [
          {
            ...detail,
            logId: log.id,
            nodeId: log.nodeId,
            createdAt: log.createdAt,
          },
        ];
      } catch {
        return [];
      }
    });
  const rows = logs.filter(
    (log) =>
      (!node || log.nodeId === node) &&
      (!type || log.type === type) &&
      (!status || log.status === status),
  );
  return (
    <Card title="Action 执行记录">
      <div className="ar-row">
        <Select
          aria-label="按动作节点筛选"
          value={node}
          options={[
            ...new Set(logs.map((log) => log.nodeId).filter(Boolean)),
          ].map((value) => ({ value: value!, label: value! }))}
          onValueChange={(nextValue) => setNode(nextValue)}
        />
        <Select
          aria-label="按动作类型筛选"
          value={type}
          options={[...new Set(logs.map((log) => log.type))].map((value) => ({
            value,
            label: value,
          }))}
          onValueChange={(nextValue) => setType(nextValue)}
        />
        <Select
          aria-label="按动作状态筛选"
          value={status}
          options={[
            { value: "Succeeded", label: "执行成功" },
            { value: "Failed", label: "执行失败" },
            { value: "Queued", label: "已入队" },
          ]}
          onValueChange={(nextValue) => setStatus(nextValue)}
        />
      </div>
      <DataTable
        rows={rows}
        rowKey={(log) => log.logId}
        columns={[
          {
            key: "node",
            label: "节点 / 步骤",
            render: (log) => `${log.nodeId} · ${log.id}`,
          },
          { key: "type", label: "动作类型", render: (log) => log.type },
          { key: "mode", label: "模式", render: (log) => log.mode },
          { key: "status", label: "状态", render: (log) => log.status },
          {
            key: "time",
            label: "时间 / 耗时",
            render: (log) =>
              `${formatDateTime(log.createdAt)} · ${log.durationMs ?? "—"} ms`,
          },
          {
            key: "detail",
            label: "记录",
            render: (log) => (
              <div>
                <span>{log.failure}</span>
                <code>{log.outboxId ?? log.executionId}</code>
              </div>
            ),
          },
        ]}
        empty={
          instance.logs.some((log) => log.action === "ActionExecuted") &&
          !logs.length
            ? "动作详情受当前读取权限限制"
            : "暂无动作记录"
        }
      />
      {user.can("approval.outbox.read") && (
        <Link to={`../outbox?instanceId=${instance.id}`}>
          查看此实例的 Outbox
        </Link>
      )}
    </Card>
  );
}
export function CarbonCopyHistory({ instanceId }: { instanceId: string }) {
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const copies = useAsyncData(
    (signal) => api.carbonCopyHistory(instanceId, signal),
    [instanceId],
  );
  const [selected, setSelected] = useState<CarbonCopyItem | null>(null);
  return (
    <Card title="抄送历史">
      <ErrorState error={copies.error} retry={copies.reload} />
      <DataTable
        rows={copies.data ?? []}
        rowKey={(copy) => copy.id}
        loading={copies.loading}
        columns={[
          { key: "node", label: "节点", render: (copy) => copy.nodeId },
          {
            key: "round",
            label: "轮次",
            render: (copy) => Number(copy.round ?? 0) + 1,
          },
          {
            key: "assignee",
            label: "接收人 / 角色",
            render: (copy) => copy.assigneeValue,
          },
          {
            key: "time",
            label: "时间",
            render: (copy) => formatDateTime(copy.createdAt),
          },
          ...(user.can("approval.directory.read")
            ? [
                {
                  key: "people",
                  label: "人员",
                  render: (copy: CarbonCopyItem) => (
                    <Button onClick={() => setSelected(copy)}>
                      查看接收成员
                    </Button>
                  ),
                },
              ]
            : []),
        ]}
        error={copies.error}
      />
      {selected && (
        <CarbonCopyRecipientsDialog
          id={selected.id}
          close={() => setSelected(null)}
        />
      )}
    </Card>
  );
}
function CarbonCopyRecipientsDialog({
  id,
  close,
}: {
  id: string;
  close: () => void;
}) {
  const { api } = useApprovalClient();
  const [page, setPage] = useState(1);
  const data = useAsyncData(
    (signal) => api.carbonCopyRecipients(id, page, 20, signal),
    [id, page],
  );
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) close();
      }}
      title="抄送接收成员"
    >
      <ErrorState error={data.error} retry={data.reload} />
      {data.data && (
        <>
          <p>
            {data.data.assigneeValue} ·{" "}
            {data.data.isCurrentRoleMembership ? "当前角色成员" : "原接收人"}
          </p>
          <DataTable
            rows={data.data.items}
            rowKey={(row) => row.userId}
            columns={[
              {
                key: "user",
                label: "接收人",
                render: (row) => row.name ?? row.userId,
              },
              {
                key: "known",
                label: "目录状态",
                render: (row) =>
                  !row.isKnown
                    ? "未找到人员"
                    : row.isActive === false
                      ? "已停用"
                      : "有效",
              },
            ]}
            loading={data.loading}
            error={data.error}
          />
        </>
      )}
      <Pagination
        page={page}
        pageSize={20}
        total={data.data?.total}
        onChange={setPage}
        loading={data.loading}
      />
    </Dialog>
  );
}
