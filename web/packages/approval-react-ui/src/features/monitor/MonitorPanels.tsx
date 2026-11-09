import { DatePicker } from "../../components/ui/extended";
import { useEffect, useRef, useState } from "react";
import { useIsMobile } from "../../hooks/use-mobile";
import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";
import { localDayStart } from "@approval/shared/features/lists/listModel";
import {
  durationText,
  formatDateTime,
  instanceStatusLabels,
  type ApprovalStatistics,
} from "@approval/shared";
import {
  useApprovalClient,
  useApprovalIdentity,
  useApprovalUser,
} from "../../client";
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
  Loading,
  Pagination,
  Select,
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
        loading={list.loading}
      >
        <Field label="发起人">
          <UserPicker
            label="搜索发起人"
            value={list.filters.initiatorId ? [list.filters.initiatorId] : []}
            disabled={list.loading}
            onChange={(ids) => {
              if (list.loading) return;
              const filters = { ...list.filters, initiatorId: ids[0] ?? "" };
              list.setFilters(filters);
              const { fromError, toError } = validateDateFilters(
                filters.from,
                filters.to,
              );
              if (!fromError && !toError) list.search(filters);
            }}
          />
        </Field>
      </ListFilterBar>
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
function defaultStatisticsFilters() {
  const end = new Date();
  const start = new Date(end);
  start.setDate(start.getDate() - 29);
  const date = (value: Date) =>
    `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, "0")}-${String(value.getDate()).padStart(2, "0")}`;
  return { from: date(start), to: date(end), key: "" };
}

export function MonitorStatistics() {
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const identity = useApprovalIdentity();
  const [applied, setApplied] = useState(defaultStatisticsFilters);
  const [from, setFrom] = useState(applied.from);
  const [to, setTo] = useState(applied.to);
  const [key, setKey] = useState("");
  const [checked, setChecked] = useState(false);
  const allowed = user.can("approval.statistics.read");
  const flows = useAsyncData((signal) => api.flows(signal), [], allowed);
  const [lastSuccessful, setLastSuccessful] = useState<{
    result: ApprovalStatistics;
    filters: typeof applied;
    owner: string;
  } | null>(null);
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
  const dates = validateDateFilters(next.from, next.to);
  const fromError =
    dates.fromError || (!next.from ? "请选择开始日期" : undefined);
  const toError = dates.toError || (!next.to ? "请选择结束日期" : undefined);
  const data = useAsyncData(
    async (signal) => ({
      result: await api.statistics(
        {
          from: applied.from ? localDayStart(applied.from) : undefined,
          to: applied.to ? localDayStart(applied.to, 1) : undefined,
          definitionKey: applied.key || undefined,
        },
        signal,
      ),
      filters: applied,
    }),
    [applied],
    allowed,
  );
  useEffect(() => {
    if (data.data) setLastSuccessful({ ...data.data, owner: identity });
  }, [data.data, identity]);
  const successful =
    data.data ??
    (allowed && lastSuccessful?.owner === identity ? lastSuccessful : null);
  const result = successful?.result;
  function apply(filters = next) {
    setChecked(true);
    const errors = validateDateFilters(filters.from, filters.to);
    if (
      !allowed ||
      !filters.from ||
      !filters.to ||
      errors.fromError ||
      errors.toError
    )
      return;
    if (JSON.stringify(applied) === JSON.stringify(filters)) {
      if (!data.loading) data.reload();
    } else setApplied(filters);
  }
  function reset() {
    if (data.loading || !allowed) return;
    const defaults = defaultStatisticsFilters();
    setFrom(defaults.from);
    setTo(defaults.to);
    setKey("");
    setChecked(false);
    if (JSON.stringify(applied) === JSON.stringify(defaults)) data.reload();
    else setApplied(defaults);
  }
  return (
    <div className="ar-stack">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          apply();
        }}
      >
        <FieldGroup className="ar-page-filter-toolbar approval:flex-row">
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
          <Field label="审批流程">
            <Select
              aria-label="统计流程"
              value={key}
              placeholder="全部流程"
              options={(flows.data ?? []).map((flow) => ({
                value: flow.key,
                label: flow.name,
              }))}
              disabled={flows.loading || !allowed}
              onValueChange={(value) => {
                setKey(value);
                apply({ ...next, key: value });
              }}
            />
          </Field>
          <Button type="submit" disabled={data.loading || !allowed}>
            刷新统计
          </Button>
          <Button onClick={reset} disabled={data.loading || !allowed}>
            重置筛选
          </Button>
        </FieldGroup>
      </form>
      {JSON.stringify(next) !== JSON.stringify(applied) && (
        <p role="status">筛选修改尚未生效，请点击刷新统计。</p>
      )}
      {successful && (
        <p>
          当前统计：{successful.filters.from} 至 {successful.filters.to} ·{" "}
          {successful.filters.key
            ? (flows.data?.find((flow) => flow.key === successful.filters.key)
                ?.name ?? successful.filters.key)
            : "全部流程"}
          。按发起日期统计，耗时只计已结束的流程。
        </p>
      )}
      {data.loading && <Loading />}
      <ErrorState error={flows.error} retry={flows.reload} />
      <ErrorState error={data.error} retry={data.reload} />
      {result && (
        <>
          <Card title="统计摘要">
            <Metrics
              items={[
                { label: "申请总数", value: result.summary.total },
                { label: "运行中", value: result.summary.running },
                { label: "已通过", value: result.summary.approved },
                { label: "已驳回", value: result.summary.rejected },
                { label: "已退回", value: result.summary.returned ?? "—" },
                { label: "已撤回", value: result.summary.withdrawn },
                { label: "已终止", value: result.summary.terminated },
                { label: "超期待办", value: result.overdueTasks },
                {
                  label: "通过率",
                  value:
                    result.approvalRate == null
                      ? "—"
                      : result.approvalRate.toFixed(1) + "%",
                },
                {
                  label: "平均审批耗时",
                  value: durationText(result.summary.averageMinutes),
                },
                {
                  label: "P90 耗时",
                  value: durationText(result.summary.p90Minutes),
                },
              ]}
            />
            <p>
              90% 的流程在 {durationText(result.summary.p90Minutes)} 内完成。
              {result.truncated ? " · 部分统计已截断" : ""}
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
                    rows={result.flows}
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
                        key: "returned",
                        label: "已退回",
                        render: (row) => row.returned ?? "—",
                      },
                      {
                        key: "approved",
                        label: "已通过",
                        render: (row) => row.approved,
                      },
                      {
                        key: "rejected",
                        label: "已驳回",
                        render: (row) => row.rejected,
                      },
                      {
                        key: "withdrawn",
                        label: "已撤回",
                        render: (row) => row.withdrawn,
                      },
                      {
                        key: "terminated",
                        label: "已终止",
                        render: (row) => row.terminated,
                      },
                      {
                        key: "average",
                        label: "平均耗时",
                        render: (row) => durationText(row.averageMinutes),
                      },
                      {
                        key: "p90",
                        label: "P90 耗时",
                        render: (row) => durationText(row.p90Minutes),
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
                    rows={result.nodes}
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
                        label: "平均耗时",
                        render: (row) => durationText(row.averageMinutes),
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
                    rows={result.backlog}
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
  const client = useApprovalClient();
  const { api } = client;
  const user = useApprovalUser();
  const identity = useApprovalIdentity();
  const [from, setFrom] = useState<string[]>([]);
  const [to, setTo] = useState<string[]>([]);
  const [reason, setReason] = useState("");
  const [page, setPage] = useState(1);
  const preview = useAsyncData(
    (signal) => api.userTasks(from[0]!, { page, pageSize: 20 }, signal),
    [from[0], page],
    !!from[0] && user.can("approval.task.reassign-bulk"),
  );
  const intent = useCommandIntent("user-reassign", { retainOnError: true });
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<Awaited<
    ReturnType<typeof api.reassignUser>
  > | null>(null);
  const [error, setError] = useState<unknown>(null);
  const submitting = useRef(false);
  const active = useRef(true);
  useEffect(() => {
    setFrom([]);
    setTo([]);
    setReason("");
    setPage(1);
    setResult(null);
    setError(null);
    setConfirm(false);
    setBusy(false);
    submitting.current = false;
  }, [identity]);
  useEffect(() => {
    active.current = true;
    return () => {
      active.current = false;
    };
  }, []);
  const canSubmit =
    !!from[0] &&
    !!to[0] &&
    from[0] !== to[0] &&
    !!reason.trim() &&
    user.can("approval.task.reassign-bulk");
  async function handover() {
    if (!canSubmit || submitting.current) return;
    const owner = client.generation;
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      const result = await intent.run(
        from[0],
        { from: from[0], to: to[0], reason },
        (body, id) => api.reassignUser(body.from, body.to, body.reason, id),
      );
      if (!active.current || client.generation !== owner) return;
      setResult(result);
      setConfirm(false);
      preview.reload();
    } catch (cause) {
      if (!active.current || client.generation !== owner) return;
      setError(cause);
      preview.reload();
    } finally {
      if (active.current && client.generation === owner) {
        submitting.current = false;
        setBusy(false);
      }
    }
  }
  return (
    <div className="ar-stack">
      <CommandRecovery intent={intent} busy={busy} recovered={preview.reload} />
      <FieldGroup className="ar-handover-form">
        <Field label="交出人员">
          <UserPicker
            label="搜索交出人员"
            value={from}
            disabled={busy || !user.can("approval.task.reassign-bulk")}
            onChange={(ids) => {
              if (busy) return;
              setFrom(ids);
              if (ids[0] && ids[0] === to[0]) setTo([]);
              setPage(1);
              setResult(null);
            }}
          />
        </Field>
        <Field
          label="接收人员"
          error={
            from[0] && from[0] === to[0]
              ? "交出人员和接收人员不能相同"
              : undefined
          }
        >
          <UserPicker
            label="搜索接收人员"
            value={to}
            onChange={setTo}
            excludedUsers={from}
            disabled={busy || !user.can("approval.task.reassign-bulk")}
          />
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
          disabled={busy}
          onChange={(event) => setReason(event.target.value)}
        />
      </Field>
      <Button
        disabled={!canSubmit || busy}
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
