import { ItemMedia } from "../components/shadcn/item";
import { useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import {
  ArrowUpRight,
  ChevronRight,
  Clock3,
  FileText,
  Inbox,
  Mail,
  RefreshCw,
  Send,
} from "lucide-react";
import {
  dueText,
  formatDateTime,
  instanceStatusLabels,
  taskKindLabels,
  taskStatusLabels,
  type CarbonCopyItem,
  type InstanceListItem,
  type TodoItem,
} from "@approval/shared";
import { emptyFilters } from "@approval/shared/features/lists/listModel";
import {
  useApprovalClient,
  useApprovalIdentity,
  useApprovalUser,
} from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import { usePagedList } from "../hooks/usePagedList";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "../components/CommandRecovery";
import {
  Badge,
  Checkbox,
  ErrorState,
  Pagination,
} from "../components/ui/primitives";
import { Button } from "../components/shadcn/button";
import {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardAction,
} from "../components/shadcn/card";
import { Tabs, TabsList, TabsTrigger } from "../components/shadcn/tabs";
import { revealTab } from "../components/ui/tabFocus";
import {
  Table,
  TableHeader,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
} from "../components/shadcn/table";
import { Avatar, AvatarFallback } from "../components/shadcn/avatar";
import {
  Empty,
  EmptyHeader,
  EmptyTitle,
  EmptyDescription,
  EmptyMedia,
} from "../components/shadcn/empty";
import { Skeleton } from "../components/shadcn/skeleton";
import { ListFilterBar } from "../features/workbench/ListFilterBar";

type Row = TodoItem | InstanceListItem | CarbonCopyItem;
type WorkbenchTab = "todo" | "done" | "started" | "cc";
const categories = {
  todo: {
    label: "我的待办",
    description: "查看申请内容，处理当前需要你审批的任务。",
    empty: "暂无待办审批",
    hint: "有新的审批任务时，会出现在这里。",
    icon: Inbox,
  },
  done: {
    label: "我已处理",
    description: "回顾你已处理的申请与当前流程状态。",
    empty: "暂无已处理审批",
    hint: "你处理过的审批会保留在这里，方便随时回顾。",
    icon: FileText,
  },
  started: {
    label: "我发起的",
    description: "跟踪申请进度，查看当前步骤与审批结果。",
    empty: "暂无发起记录",
    hint: "发起申请后，可以在这里跟踪审批进度。",
    icon: Send,
  },
  cc: {
    label: "抄送我的",
    description: "查看与你相关的抄送记录，及时了解审批进展。",
    empty: "暂无抄送记录",
    hint: "抄送给你或你所在角色的申请会显示在这里。",
    icon: Mail,
  },
};
const titleOf = (row: Row) =>
  row.title || row.businessKey || row.definitionName;
const instanceIdOf = (row: Row) =>
  "instanceId" in row ? row.instanceId : row.id;

function WorkbenchList({
  tab,
  refreshCount,
}: {
  tab: WorkbenchTab;
  refreshCount: () => void;
}) {
  const client = useApprovalClient();
  const { api } = client;
  const navigate = useNavigate();
  const user = useApprovalUser();
  const intent = useCommandIntent("cc-read");
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const opening = useRef(false);
  const list = usePagedList<Row>(
    (query, signal) =>
      tab === "todo"
        ? api.todo(query, signal)
        : tab === "done"
          ? api.done(query, signal)
          : tab === "started"
            ? api.started(query, signal)
            : api.carbonCopies(query, signal),
    `${tab}_`,
    !!user.me,
  );
  const flows = useAsyncData((signal) => api.flows(signal), [], !!user.me);
  const initiators = [
    ...new Set(list.items.map((row) => row.initiatorId)),
  ].sort();
  const people = useAsyncData(
    (signal) => api.usersByIds(initiators, signal),
    [initiators],
    user.can("approval.directory.read") && initiators.length > 0,
  );
  const nameOf = (id: string) =>
    people.data?.find((person) => person.id === id)?.name ?? id;
  async function open(row: Row) {
    if (opening.current) return;
    const owner = client.generation;
    opening.current = true;
    setBusy(true);
    setError(null);
    try {
      if (tab === "cc" && !(row as CarbonCopyItem).readAt) {
        const copy = row as CarbonCopyItem;
        await intent.run(copy.id, {}, (_, key) =>
          api.readCarbonCopy(copy.id, key),
        );
        if (owner !== client.generation) return;
        list.reload();
        refreshCount();
      }
      navigate("../approvals/" + instanceIdOf(row));
    } finally {
      opening.current = false;
      if (owner === client.generation) setBusy(false);
    }
  }
  const info = categories[tab];
  function status(row: Row) {
    if (tab === "cc")
      return (
        <Badge status={(row as CarbonCopyItem).readAt ? undefined : "Pending"}>
          {(row as CarbonCopyItem).readAt ? "已读" : "未读"}
        </Badge>
      );
    return (
      <Badge status={row.status}>
        {"taskId" in row
          ? taskStatusLabels[row.status]?.text
          : instanceStatusLabels[row.status]?.text}
      </Badge>
    );
  }
  return (
    <div className="ar-workbench-list">
      <div className="ar-list-heading">
        <div>
          <h2>{info.label}</h2>
          <p>{info.description}</p>
        </div>
        <Button
          variant="outline"
          onClick={() => {
            list.reload();
            refreshCount();
          }}
          disabled={list.loading}
        >
          <RefreshCw data-icon="inline-start" />
          刷新列表
        </Button>
      </div>
      <CommandRecovery
        intent={intent}
        busy={busy}
        recovered={() => {
          setError(null);
          list.reload();
          refreshCount();
        }}
      />
      <ListFilterBar
        filters={list.filters}
        onChange={list.setFilters}
        search={() => list.search()}
        reset={list.reset}
        flows={flows.data ?? []}
        overdue={tab === "todo"}
        dates={tab !== "todo"}
        status={tab !== "todo"}
      />
      {tab === "cc" && (
        <div className="ar-list-quick-filter">
          <Checkbox
            checked={list.filters.unreadOnly === true}
            onChange={(unreadOnly) =>
              list.search({ ...list.filters, unreadOnly })
            }
          >
            只看未读
          </Checkbox>
        </div>
      )}
      <ErrorState
        error={error || list.error || flows.error}
        retry={() => {
          setError(null);
          list.reload();
          flows.reload();
        }}
      />
      <div className="ar-workbench-table" aria-busy={list.loading}>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>审批事项</TableHead>
              <TableHead>发起人</TableHead>
              <TableHead>{tab === "todo" ? "待办步骤" : "当前步骤"}</TableHead>
              <TableHead>{tab === "cc" ? "阅读状态" : "状态"}</TableHead>
              <TableHead>{tab === "cc" ? "抄送时间" : "创建时间"}</TableHead>
              <TableHead>
                {tab === "todo"
                  ? "截止时间"
                  : tab === "cc"
                    ? "阅读时间"
                    : "完成时间"}
              </TableHead>
              <TableHead>操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {list.loading && !list.items.length ? (
              Array.from({ length: 4 }, (_, index) => (
                <TableRow key={index}>
                  <TableCell colSpan={7}>
                    <Skeleton className="approval:h-12 approval:w-full" />
                  </TableCell>
                </TableRow>
              ))
            ) : list.items.length ? (
              list.items.map((row) => (
                <TableRow
                  key={"taskId" in row ? row.taskId : row.id}
                  data-overdue={
                    "dueAt" in row &&
                    !!row.dueAt &&
                    new Date(row.dueAt).getTime() < Date.now()
                  }
                >
                  <TableCell data-label="审批事项">
                    <div className="ar-task-title">
                      <ItemMedia variant="icon" className="ar-task-icon">
                        <FileText aria-hidden="true" />
                      </ItemMedia>
                      <div>
                        <Button
                          variant="link"
                          disabled={busy}
                          onClick={() => void open(row).catch(setError)}
                        >
                          {titleOf(row)}
                        </Button>
                        <small>
                          {row.definitionName}
                          {"definitionVersion" in row
                            ? ` · v${row.definitionVersion}`
                            : ""}{" "}
                          · {row.businessKey}
                        </small>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell data-label="发起人">
                    <div className="ar-task-person">
                      <Avatar>
                        <AvatarFallback>
                          {nameOf(row.initiatorId).charAt(0).toUpperCase()}
                        </AvatarFallback>
                      </Avatar>
                      <span>{nameOf(row.initiatorId)}</span>
                    </div>
                  </TableCell>
                  <TableCell
                    data-label={tab === "todo" ? "待办步骤" : "当前步骤"}
                  >
                    <div className="ar-task-step">
                      <span>
                        {"taskId" in row
                          ? row.nodeName
                          : "currentNodeId" in row
                            ? row.currentNodeId || "—"
                            : row.nodeId}
                      </span>
                      {"taskId" in row && (
                        <small>
                          {taskKindLabels[row.kind] || "待审批"} ·{" "}
                          {row.assigneeKind === "Role"
                            ? `角色：${row.assigneeValue}`
                            : nameOf(row.assigneeValue)}
                        </small>
                      )}
                    </div>
                  </TableCell>
                  <TableCell data-label={tab === "cc" ? "阅读状态" : "状态"}>
                    {status(row)}
                  </TableCell>
                  <TableCell
                    data-label={tab === "cc" ? "抄送时间" : "创建时间"}
                  >
                    <time className="ar-task-time">
                      {formatDateTime(row.createdAt)}
                    </time>
                  </TableCell>
                  <TableCell
                    data-label={
                      tab === "todo"
                        ? "截止时间"
                        : tab === "cc"
                          ? "阅读时间"
                          : "完成时间"
                    }
                  >
                    {"dueAt" in row ? (
                      row.dueAt ? (
                        <div
                          className="ar-task-deadline"
                          data-overdue={dueText(row.dueAt).overdue}
                        >
                          <span>
                            <Clock3 aria-hidden="true" />
                            {dueText(row.dueAt).text}
                          </span>
                          <small>{formatDateTime(row.dueAt)}</small>
                        </div>
                      ) : (
                        "—"
                      )
                    ) : "completedAt" in row ? (
                      formatDateTime(row.completedAt)
                    ) : (row as CarbonCopyItem).readAt ? (
                      formatDateTime((row as CarbonCopyItem).readAt)
                    ) : (
                      "—"
                    )}
                  </TableCell>
                  <TableCell data-label="操作">
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label={`查看审批：${titleOf(row)}`}
                      disabled={busy}
                      onClick={() => void open(row).catch(setError)}
                    >
                      {tab === "todo" ? "处理" : "查看"}
                      <ChevronRight data-icon="inline-end" />
                    </Button>
                  </TableCell>
                </TableRow>
              ))
            ) : list.error ? (
              <TableRow>
                <TableCell colSpan={7}>
                  <Empty>
                    <EmptyHeader>
                      <EmptyTitle>列表加载失败，请重试</EmptyTitle>
                    </EmptyHeader>
                  </Empty>
                </TableCell>
              </TableRow>
            ) : (
              <TableRow>
                <TableCell colSpan={7}>
                  <Empty>
                    <EmptyHeader>
                      <EmptyMedia variant="icon">
                        <info.icon />
                      </EmptyMedia>
                      <EmptyTitle>{info.empty}</EmptyTitle>
                      <EmptyDescription>
                        {list.filters.keyword ||
                        list.filters.definitionKey ||
                        list.filters.status ||
                        list.filters.from ||
                        list.filters.to ||
                        list.filters.overdueOnly ||
                        list.filters.unreadOnly
                          ? "当前筛选条件下没有记录，请调整或重置筛选。"
                          : info.hint}
                      </EmptyDescription>
                    </EmptyHeader>
                  </Empty>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
      <Pagination
        page={list.page}
        pageSize={list.pageSize}
        total={list.total}
        loading={list.loading}
        onChange={list.paginate}
      />
    </div>
  );
}

export default function WorkbenchPage() {
  const identity = useApprovalIdentity();
  const [params, setParams] = useSearchParams();
  const tab = (
    Object.hasOwn(categories, params.get("tab") ?? "")
      ? params.get("tab")
      : "todo"
  ) as WorkbenchTab;
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const count = useAsyncData((signal) => api.todoCount(signal), [], !!user.me);
  const overdue = useAsyncData(
    (signal) => api.todo({ page: 1, pageSize: 1, overdueOnly: true }, signal),
    [],
    !!user.me,
  );
  const started = useAsyncData(
    (signal) =>
      api.started({ page: 1, pageSize: 1, status: "Running" }, signal),
    [],
    !!user.me,
  );
  const unread = useAsyncData(
    (signal) =>
      api.carbonCopies({ page: 1, pageSize: 1, unreadOnly: true }, signal),
    [],
    !!user.me,
  );
  const summaries = [
    {
      key: "todo",
      title: "待我审批",
      description: "需要你处理的审批任务",
      value: count.data?.count,
      error: count.error,
      icon: Inbox,
      tab: "todo" as const,
      filter: undefined,
    },
    {
      key: "overdue",
      title: "超期待办",
      description: "已超过截止时间的任务",
      value: overdue.data?.total,
      error: overdue.error,
      icon: Clock3,
      tab: "todo" as const,
      filter: ["overdueOnly", "1"],
    },
    {
      key: "started",
      title: "申请进行中",
      description: "你发起且正在审批的申请",
      value: started.data?.total,
      error: started.error,
      icon: Send,
      tab: "started" as const,
      filter: ["status", "Running"],
    },
    {
      key: "unread",
      title: "未读抄送",
      description: "等待你查看的抄送记录",
      value: unread.data?.total,
      error: unread.error,
      icon: Mail,
      tab: "cc" as const,
      filter: ["unreadOnly", "1"],
    },
  ];
  function changeTab(value: string) {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set("tab", value);
        return next;
      },
      { replace: true },
    );
  }
  function refreshCount() {
    count.reload();
    overdue.reload();
    started.reload();
    unread.reload();
  }
  function selectSummary(item: (typeof summaries)[number]) {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set("tab", item.tab);
        for (const key of Object.keys(emptyFilters()))
          next.delete(`${item.tab}_${key}`);
        next.delete(`${item.tab}_page`);
        if (item.filter)
          next.set(`${item.tab}_${item.filter[0]}`, item.filter[1]!);
        return next;
      },
      { replace: true },
    );
  }
  return (
    <div className="ar-workbench-page">
      <header className="ar-workbench-page-header">
        <div>
          <span className="ar-product-eyebrow">我的工作空间</span>
          <h1>审批工作台</h1>
          <p>集中处理审批，随时掌握与你相关的申请进展。</p>
        </div>
        {user.formsEnabled &&
          (user.can("approval.instance.start") ||
            user.can("approval.draft.read")) && (
            <Button asChild size="lg">
              <Link to="../start">
                <Send data-icon="inline-start" />
                发起申请
              </Link>
            </Button>
          )}
      </header>
      <div className="ar-workbench-metrics">
        {summaries.map((item) => (
          <Card
            key={item.key}
            className="ar-workbench-metric"
            data-kind={item.key}
          >
            <CardHeader>
              <CardTitle>{item.title}</CardTitle>
              <CardAction>
                <ItemMedia variant="icon" className="ar-metric-icon">
                  <item.icon aria-hidden="true" />
                </ItemMedia>
              </CardAction>
              <CardDescription>{item.description}</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="ar-metric-bottom">
                <strong aria-label={`${item.title}数量`}>
                  {item.value ?? "—"}
                  <small>项</small>
                </strong>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`查看${item.title}`}
                  onClick={() => selectSummary(item)}
                >
                  <ArrowUpRight />
                </Button>
              </div>
              {!!item.error && (
                <span className="ar-metric-error">获取失败</span>
              )}
            </CardContent>
          </Card>
        ))}
      </div>
      {summaries.some((item) => !!item.error) && (
        <ErrorState
          error={summaries.find((item) => item.error)?.error}
          retry={refreshCount}
        />
      )}
      <Card className="ar-workbench-panel">
        <CardHeader className="ar-workbench-panel-header">
          <CardTitle className="approval:sr-only">审批列表</CardTitle>
          <Tabs
            value={tab}
            onValueChange={changeTab}
            className="approval:min-w-0"
          >
            <div className="ar-workbench-tabs" data-tab-scroll>
              <TabsList variant="line" aria-label="审批分类">
                {Object.entries(categories).map(([value, item]) => (
                  <TabsTrigger
                    key={value}
                    value={value}
                    onFocus={(event) => revealTab(event.currentTarget)}
                  >
                    <item.icon />
                    {item.label}
                    {value === "todo" && (
                      <Badge>{count.data?.count ?? "—"}</Badge>
                    )}
                  </TabsTrigger>
                ))}
              </TabsList>
            </div>
          </Tabs>
        </CardHeader>
        <CardContent>
          <WorkbenchList
            key={`${tab}:${identity}`}
            tab={tab}
            refreshCount={refreshCount}
          />
        </CardContent>
      </Card>
    </div>
  );
}
