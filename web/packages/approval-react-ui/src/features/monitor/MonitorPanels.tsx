import { DatePicker } from "../../components/ui/extended";
import { useEffect, useState } from "react";
import { useIsMobile } from "../../hooks/use-mobile";
import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";
import { localDayStart } from "@approval/shared/features/lists/listModel";
import { formatDateTime, instanceStatusLabels } from "@approval/shared";
import { useApprovalClient, useApprovalUser } from "../../client";
import { useAsyncData } from "../../hooks/useAsyncData";
import { usePagedList } from "../../hooks/usePagedList";
import { useCommandIntent } from "../../hooks/useCommandIntent";
import { CommandRecovery } from "../../components/CommandRecovery";
import { UserPicker } from "../../components/UserPicker";
import {
  Button,
  Badge,
  Card,
  ConfirmDialog,
  ErrorState,
  Field,
  Input,
  Pagination,
  Textarea,
} from "../../components/ui/primitives";
import { FieldGroup } from "../../components/shadcn/field";
import { DataTable } from "../../components/ui/DataTable";
import { Metrics } from "../../components/ui/Metrics";
import { ListFilterBar } from "../workbench/ListFilterBar";
import { Link } from "react-router";
import { validateDateFilters } from "../workbench/dateFilters";

export function MonitorInstances() {
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const list = usePagedList(
    (query, signal) => api.adminInstances(query, signal),
    "monitor_",
    user.can("approval.instance.read-all"),
  );
  const flows = useAsyncData((signal) => api.flows(signal));
  const intent = useCommandIntent("monitor-terminate");
  const [selected, setSelected] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  async function terminate() {
    if (!selected || !reason.trim() || busy) return;
    setBusy(true);
    try {
      await intent.run(selected, { reason }, (body, id) =>
        api.terminate(selected, body.reason, id),
      );
      setSelected(null);
      list.reload();
    } catch (cause) {
      setError(cause);
      list.reload();
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="ar-stack">
      <CommandRecovery intent={intent} busy={busy} recovered={list.reload} />
      <ListFilterBar
        filters={list.filters}
        onChange={list.setFilters}
        search={() => list.search()}
        reset={list.reset}
        flows={flows.data ?? []}
        overdue
      />
      <ErrorState error={error || list.error} retry={list.reload} />
      <DataTable
        rows={list.items}
        rowKey={(row) => row.id}
        loading={list.loading}
        columns={[
          {
            key: "title",
            label: "审批",
            render: (row) => (
              <Link className="ar-text-link" to={`../approvals/${row.id}`}>
                {row.title || row.businessKey}
              </Link>
            ),
          },
          { key: "flow", label: "流程", render: (row) => row.definitionName },
          {
            key: "status",
            label: "状态",
            render: (row) => (
              <Badge status={row.status}>
                {instanceStatusLabels[row.status]?.text}
              </Badge>
            ),
          },
          {
            key: "time",
            label: "创建时间",
            render: (row) => formatDateTime(row.createdAt),
          },
          {
            key: "action",
            label: "操作",
            render: (row) =>
              user.can("approval.instance.terminate") &&
              ["Running", "Returned"].includes(row.status) ? (
                <Button
                  variant="danger"
                  onClick={() => {
                    setReason("");
                    setSelected(row.id);
                  }}
                >
                  终止流程
                </Button>
              ) : null,
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
      <ConfirmDialog
        open={!!selected}
        onOpenChange={(open) => {
          if (!open) setSelected(null);
        }}
        title="终止流程"
        busy={busy}
        confirm={() => void terminate()}
      >
        <Field label="终止原因" required>
          <Textarea
            aria-label="终止原因"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        </Field>
      </ConfirmDialog>
    </div>
  );
}
export function MonitorStatistics() {
  const { api } = useApprovalClient();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [key, setKey] = useState("");
  const [applied, setApplied] = useState({ from: "", to: "", key: "" });
  const [checked, setChecked] = useState(false);
  const mobile = useIsMobile(701);
  const [sections, setSections] = useState<string[]>([
    "flows",
    "nodes",
    "backlog",
  ]);
  useEffect(
    () => setSections(mobile ? [] : ["flows", "nodes", "backlog"]),
    [mobile],
  );
  const next = { from: from.trim(), to: to.trim(), key: key.trim() };
  const { fromError, toError } = validateDateFilters(next.from, next.to);
  const data = useAsyncData(
    (signal) =>
      api.statistics(
        {
          from: applied.from ? localDayStart(applied.from) : undefined,
          to: applied.to ? localDayStart(applied.to, 1) : undefined,
          definitionKey: applied.key || undefined,
        },
        signal,
      ),
    [applied],
  );
  function apply() {
    setChecked(true);
    if (fromError || toError) return;
    if (JSON.stringify(applied) === JSON.stringify(next)) data.reload();
    else setApplied(next);
  }
  function reset() {
    setFrom("");
    setTo("");
    setKey("");
    setChecked(false);
    if (!applied.from && !applied.to && !applied.key) data.reload();
    else setApplied({ from: "", to: "", key: "" });
  }
  return (
    <div className="ar-stack">
      <form
        className="ar-page-filter-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          apply();
        }}
      >
        <Field
          label="开始日期"
          error={checked ? (fromError ?? undefined) : undefined}
        >
          <DatePicker
            label="统计开始日期"
            showInputError={false}
            value={from}
            onChange={(nextValue) => setFrom(nextValue)}
          />
        </Field>
        <Field
          label="结束日期"
          error={checked ? (toError ?? undefined) : undefined}
        >
          <DatePicker
            label="统计结束日期"
            showInputError={false}
            value={to}
            onChange={(nextValue) => setTo(nextValue)}
          />
        </Field>
        <Field label="流程 Key">
          <Input
            aria-label="统计流程 Key"
            value={key}
            placeholder="按流程 Key 筛选"
            onChange={(event) => setKey(event.target.value)}
          />
        </Field>
        <Button type="submit">刷新统计</Button>
        <Button onClick={reset}>重置筛选</Button>
      </form>
      {JSON.stringify(next) !== JSON.stringify(applied) && (
        <p role="status">筛选修改尚未生效，请点击刷新统计。</p>
      )}
      <ErrorState error={data.error} retry={data.reload} />
      {data.data && (
        <>
          <Card title="统计摘要">
            <Metrics
              items={[
                { label: "申请总数", value: data.data.summary.total },
                { label: "运行中", value: data.data.summary.running },
                { label: "已通过", value: data.data.summary.approved },
                { label: "已驳回", value: data.data.summary.rejected },
                { label: "已退回", value: data.data.summary.returned },
                { label: "超期待办", value: data.data.overdueTasks },
              ]}
            />
            <p>
              通过率{" "}
              {data.data.approvalRate == null
                ? "—"
                : data.data.approvalRate.toFixed(1) + "%"}
              {data.data.truncated ? " · 部分统计已截断" : ""}
            </p>
          </Card>
          <Accordion
            type="multiple"
            value={sections}
            onValueChange={setSections}
          >
            <Card title="按流程统计">
              <AccordionItem value="flows">
                <AccordionTrigger>查看各流程明细</AccordionTrigger>
                <AccordionContent>
                  <DataTable
                    rows={data.data.flows}
                    rowKey={(row) => row.definitionKey}
                    columns={[
                      {
                        key: "flow",
                        label: "流程",
                        render: (row) => row.definitionName,
                      },
                      {
                        key: "total",
                        label: "总数",
                        render: (row) => row.total,
                      },
                      {
                        key: "running",
                        label: "运行中",
                        render: (row) => row.running,
                      },
                      {
                        key: "approved",
                        label: "通过",
                        render: (row) => row.approved,
                      },
                      {
                        key: "rejected",
                        label: "驳回",
                        render: (row) => row.rejected,
                      },
                      {
                        key: "average",
                        label: "平均耗时（分钟）",
                        render: (row) => row.averageMinutes ?? "—",
                      },
                      {
                        key: "p90",
                        label: "P90（分钟）",
                        render: (row) => row.p90Minutes ?? "—",
                      },
                    ]}
                    loading={data.loading}
                    error={data.error}
                  />
                </AccordionContent>
              </AccordionItem>
            </Card>
            <Card
              title="节点处理统计"
              description="按流程节点查看已处理数量、超期数量和平均处理时长。"
            >
              <AccordionItem value="nodes">
                <AccordionTrigger>查看各节点明细</AccordionTrigger>
                <AccordionContent>
                  <DataTable
                    rows={data.data.nodes}
                    rowKey={(row) => row.definitionKey + ":" + row.nodeId}
                    columns={[
                      {
                        key: "flow",
                        label: "流程",
                        render: (row) => row.definitionName,
                      },
                      {
                        key: "node",
                        label: "节点",
                        render: (row) => row.nodeName,
                      },
                      {
                        key: "handled",
                        label: "已处理",
                        render: (row) => row.handled,
                      },
                      {
                        key: "overdue",
                        label: "超期",
                        render: (row) => row.overdue,
                      },
                      {
                        key: "average",
                        label: "平均耗时（分钟）",
                        render: (row) => row.averageMinutes.toFixed(1),
                      },
                    ]}
                    loading={data.loading}
                    error={data.error}
                  />
                </AccordionContent>
              </AccordionItem>
            </Card>
            <Card
              title="待办积压"
              description="查看处理人或角色的待办数量，定位最早等待的任务。"
            >
              <AccordionItem value="backlog">
                <AccordionTrigger>查看待办积压明细</AccordionTrigger>
                <AccordionContent>
                  <DataTable
                    rows={data.data.backlog}
                    rowKey={(row) => row.assigneeKind + ":" + row.assigneeValue}
                    columns={[
                      {
                        key: "assignee",
                        label: "处理人 / 角色",
                        render: (row) =>
                          row.assigneeKind === "Role"
                            ? "角色：" + row.assigneeValue
                            : row.assigneeValue,
                      },
                      {
                        key: "count",
                        label: "待办数量",
                        render: (row) => row.count,
                      },
                      {
                        key: "overdue",
                        label: "超期数量",
                        render: (row) => row.overdue,
                      },
                      {
                        key: "time",
                        label: "最早创建时间",
                        render: (row) => formatDateTime(row.oldestCreatedAt),
                      },
                    ]}
                    loading={data.loading}
                    error={data.error}
                  />
                </AccordionContent>
              </AccordionItem>
            </Card>
          </Accordion>
        </>
      )}
    </div>
  );
}
export function HandoverPanel() {
  const { api } = useApprovalClient();
  const [from, setFrom] = useState<string[]>([]);
  const [to, setTo] = useState<string[]>([]);
  const [reason, setReason] = useState("");
  const [page, setPage] = useState(1);
  const preview = useAsyncData(
    (signal) => api.userTasks(from[0]!, { page, pageSize: 20 }, signal),
    [from[0], page],
    !!from[0],
  );
  const intent = useCommandIntent("user-reassign", { retainOnError: true });
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<Awaited<
    ReturnType<typeof api.reassignUser>
  > | null>(null);
  const [error, setError] = useState<unknown>(null);
  async function handover() {
    if (!from[0] || !to[0] || !reason.trim() || busy) return;
    setBusy(true);
    setError(null);
    try {
      const result = await intent.run(
        from[0],
        { from: from[0], to: to[0], reason },
        (body, id) => api.reassignUser(body.from, body.to, body.reason, id),
      );
      setResult(result);
      setConfirm(false);
      preview.reload();
    } catch (cause) {
      setError(cause);
      preview.reload();
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="ar-stack">
      <CommandRecovery intent={intent} busy={busy} recovered={preview.reload} />
      <FieldGroup className="ar-handover-form">
        <Field label="交出人员">
          <UserPicker
            value={from}
            onChange={(ids) => {
              setFrom(ids);
              setPage(1);
              setResult(null);
            }}
          />
        </Field>
        <Field label="接收人员">
          <UserPicker value={to} onChange={setTo} excludedUsers={from} />
        </Field>
      </FieldGroup>
      <ErrorState error={error || preview.error} retry={preview.reload} />
      <DataTable
        rows={preview.data?.items ?? []}
        loading={preview.loading}
        empty={
          from.length ? "此人员没有待交接任务" : "选择交出人员后显示待办预览"
        }
        rowKey={(row) => row.taskId}
        columns={[
          {
            key: "title",
            label: "待办",
            render: (row) => row.title || row.businessKey,
          },
          { key: "node", label: "节点", render: (row) => row.nodeName },
        ]}
        error={preview.error}
      />
      {preview.data && (
        <Pagination
          page={page}
          pageSize={20}
          total={preview.data?.total}
          onChange={setPage}
          loading={preview.loading}
        />
      )}
      <Field label="交接原因" required>
        <Textarea
          aria-label="交接原因"
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
      </Field>
      <Button
        disabled={!from[0] || !to[0] || !reason.trim() || busy}
        variant="primary"
        onClick={() => setConfirm(true)}
      >
        批量改派
      </Button>
      {result && (
        <Card title="交接结果">
          <Metrics
            items={[
              { label: "已改派任务", value: result.count },
              { label: "跳过任务", value: result.skipped.length },
            ]}
          />
          <DataTable
            rows={result.skipped}
            rowKey={(row) => row.taskId}
            empty="所有可交接任务已处理，无跳过记录"
            columns={[
              {
                key: "title",
                label: "申请",
                render: (row) => row.title || row.instanceId,
              },
              { key: "node", label: "节点", render: (row) => row.nodeName },
              { key: "reason", label: "跳过原因", render: (row) => row.reason },
            ]}
          />
        </Card>
      )}
      <ConfirmDialog
        open={confirm}
        onOpenChange={setConfirm}
        title="批量交接"
        busy={busy}
        confirm={() => void handover()}
      >
        每个实例独立提交；部分完成后按原键和原内容恢复。
      </ConfirmDialog>
    </div>
  );
}
