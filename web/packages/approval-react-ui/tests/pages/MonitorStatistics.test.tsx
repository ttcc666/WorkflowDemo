import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ApprovalStatistics } from "@approval/shared";
import { ApprovalProvider, createApprovalClient } from "../../src/client";
import { MonitorStatistics } from "../../src/features/monitor/MonitorPanels";

type StatisticsFixture = Omit<ApprovalStatistics, "nodes"> & {
  nodes: (Omit<ApprovalStatistics["nodes"][number], "averageMinutes"> & {
    averageMinutes: number | null;
  })[];
};

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 9, 0, 30));
});
afterEach(() => vi.useRealTimers());
const summary = {
  definitionKey: "",
  definitionName: "全部",
  total: 42,
  running: 2,
  approved: 25,
  rejected: 4,
  returned: 3,
  withdrawn: 5,
  terminated: 3,
  averageMinutes: 90,
  p90Minutes: 2880,
};
function statistics(
  averageMinutes: number | null = 90,
  p90Minutes: number | null = 2880,
  total = 42,
) {
  return {
    summary: { ...summary, averageMinutes, p90Minutes, total },
    approvalRate: 78.1,
    overdueTasks: 6,
    flows: [
      {
        ...summary,
        definitionKey: "expense",
        definitionName: "费用审批",
        total: 12,
      },
    ],
    nodes: [],
    backlog: [],
    truncated: false,
  };
}
function show(
  firstFails = false,
  durations: [number | null, number | null] = [90, 2880],
  allowed = true,
  flowState: "success" | "failure" | "empty" = "success",
  overrides: Partial<StatisticsFixture> = {},
) {
  const queries: URLSearchParams[] = [];
  let failNext = firstFails;
  let hold = false;
  const pending: {
    signal: AbortSignal;
    resolve: (response: Response) => void;
  }[] = [];
  const fetcher = vi.fn(
    async (input: string | URL | Request, init?: RequestInit) => {
      const url = new URL(String(input), "http://example.test");
      if (url.pathname.endsWith("/me"))
        return new Response(
          JSON.stringify({
            userId: "admin",
            capabilities: { "approval.statistics.read": allowed },
          }),
        );
      if (url.pathname.endsWith("/flows")) {
        if (flowState === "failure") {
          flowState = "success";
          return new Response(
            JSON.stringify({ title: "流程列表暂不可用", status: 503 }),
            {
              status: 503,
              headers: { "Content-Type": "application/problem+json" },
            },
          );
        }
        return new Response(
          JSON.stringify(
            flowState === "empty"
              ? []
              : [
                  { key: "expense", name: "费用审批" },
                  { key: "leave", name: "请假审批" },
                ],
          ),
        );
      }
      if (url.pathname.endsWith("/admin/statistics"))
        queries.push(url.searchParams);
      if (url.pathname.endsWith("/admin/statistics") && hold)
        return new Promise<Response>((resolve) =>
          pending.push({ resolve, signal: init!.signal as AbortSignal }),
        );
      if (url.pathname.endsWith("/admin/statistics") && failNext) {
        failNext = false;
        return new Response(
          JSON.stringify({ title: "服务暂不可用", status: 503 }),
          {
            status: 503,
            headers: { "Content-Type": "application/problem+json" },
          },
        );
      }
      return new Response(JSON.stringify({ ...statistics(...durations), ...overrides }));
    },
  );
  const client = createApprovalClient({ fetcher });
  render(
    <ApprovalProvider client={client}>
      <MonitorStatistics />
    </ApprovalProvider>,
  );
  return {
    client,
    queries,
    fetcher,
    pending,
    failNext: () => {
      failNext = true;
    },
    hold: () => {
      hold = true;
    },
  };
}

function distribution(total: number, average: number | null, count: number) {
  return {
    flows: [{ ...summary, definitionKey: "expense", definitionName: "费用审批", total }],
    nodes: [{
      definitionKey: "expense", definitionName: "费用审批",
      nodeId: "review", nodeName: "审核", handled: 4, overdue: 1,
      averageMinutes: average,
    }],
    backlog: [{
      assigneeKind: "User" as const, assigneeValue: "dave",
      count, overdue: 1, oldestCreatedAt: "2026-11-01T00:00:00Z",
    }],
  };
}

function fillOf(name: string) {
  return screen.getByRole("img", { name }).querySelector("span[style]");
}

it("statistics bars scale each series against its own maximum and retain API values", async () => {
  const first = distribution(8, 15, 3);
  const second = distribution(16, 60, 12);
  show(false, [90, 2880], true, "success", {
    flows: [...first.flows, { ...second.flows[0]!, definitionKey: "leave", definitionName: "请假审批" }],
    nodes: [...first.nodes, { ...second.nodes[0]!, nodeId: "final", nodeName: "复核" }],
    backlog: [...first.backlog, { ...second.backlog[0]!, assigneeKind: "Role", assigneeValue: "审计" }],
  });
  await screen.findByRole("img", { name: "费用审批：总数 8 条" });
  expect(fillOf("费用审批：总数 8 条")).toHaveStyle({ width: "50%" });
  expect(fillOf("请假审批：总数 16 条")).toHaveStyle({ width: "100%" });
  expect(fillOf("费用审批 · 审核：平均耗时 15 分钟")).toHaveStyle({ width: "25%" });
  expect(fillOf("费用审批 · 复核：平均耗时 60 分钟")).toHaveStyle({ width: "100%" });
  expect(fillOf("dave：待办 3 条")).toHaveStyle({ width: "25%" });
  expect(fillOf("角色：审计：待办 12 条")).toHaveStyle({ width: "100%" });
  expect(screen.getByRole("img", { name: "费用审批 · 复核：平均耗时 60 分钟" })).toHaveTextContent("1 小时");
});

it("empty statistics series report no rows and render no bars", async () => {
  const { queries } = show(false, [null, null], true, "success", { flows: [] });
  await screen.findByText("该时间范围内没有流程");
  expect(screen.getByText("暂无已处理的任务")).toBeVisible();
  expect(screen.getByText("当前没有待办")).toBeVisible();
  expect(screen.queryByRole("img", { name: /总数|平均耗时|待办/ })).not.toBeInTheDocument();
  expect(queries).toHaveLength(1);
});

it("zero statistics values have zero-width fills while a null node duration is unknown", async () => {
  const zero = distribution(0, 0, 0);
  show(false, [null, null], true, "success", {
    ...zero,
    nodes: [...zero.nodes, {
      ...zero.nodes[0]!, nodeId: "unknown", nodeName: "未知节点", averageMinutes: null,
    }],
  });
  await screen.findByRole("img", { name: "费用审批：总数 0 条" });
  expect(fillOf("费用审批：总数 0 条")).toHaveStyle({ width: "0%" });
  expect(fillOf("费用审批 · 审核：平均耗时 0 分钟")).toHaveStyle({ width: "0%" });
  expect(fillOf("dave：待办 0 条")).toHaveStyle({ width: "0%" });
  const unknown = within(screen.getByRole("row", { name: /未知节点/ }));
  expect(unknown.getByText("-", { exact: true })).toBeVisible();
  expect(unknown.getByText("费用审批 · 未知节点：平均耗时 无数据")).toBeInTheDocument();
  expect(unknown.queryByRole("img")).not.toBeInTheDocument();
  expect(screen.queryByText(/90% 的流程在/)).not.toBeInTheDocument();
});

it("truncated statistics explain the limit and metric definitions without recomputing the API rate", async () => {
  show(false, [90, 2880], true, "success", { truncated: true, approvalRate: 12.3 });
  const alert = await screen.findByRole("alert");
  expect(alert).toHaveTextContent("统计数据已截断，只统计最早的 10 万条记录。建议缩小时间范围。");
  expect(screen.getByText("12.3%")).toBeVisible();
  expect(screen.getByText("通过率 = 已通过 /（已通过 + 已驳回 + 已终止），撤回不计入分母。")).toBeVisible();
  expect(screen.getByText("超时任务指超过时限才处理，或者当前已经超时的待办。")).toBeVisible();
});
async function chooseFlow(name: string) {
  const user = userEvent.setup();
  await user.click(screen.getByRole("combobox", { name: "统计流程" }));
  await user.click(screen.getByRole("option", { name }));
}

it.each(["2026-", "2026-02-30", "2025-02-29", "0000-01-01", "10000-01-01"])(
  "invalid statistics date %s keeps the last result and never queries",
  async (value) => {
    const { queries } = show();
    await screen.findByText("42", { exact: true });
    expect(queries).toHaveLength(1);
    fireEvent.change(screen.getByRole("textbox", { name: "统计开始日期" }), {
      target: { value },
    });
    expect(queries).toHaveLength(1);
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "刷新统计" }));
    expect(queries).toHaveLength(1);
    expect(screen.getByText("42", { exact: true })).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", { name: "统计开始日期" }),
    ).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByText("Invalid time value")).not.toBeInTheDocument();
  },
);

it("statistics retry uses the applied query even when the editable date is incomplete", async () => {
  const { queries } = show(true);
  await screen.findByText("服务暂不可用");
  fireEvent.change(screen.getByRole("textbox", { name: "统计开始日期" }), {
    target: { value: "2026-" },
  });
  await userEvent.setup().click(screen.getByRole("button", { name: "重试" }));
  await screen.findByText("42", { exact: true });
  expect(queries).toHaveLength(2);
  expect(Object.fromEntries(queries[1]!)).toEqual(
    Object.fromEntries(queries[0]!),
  );
  expect(queries[1]!.get("from")).toBe(new Date(2026, 9, 11).toISOString());
  expect(queries[1]!.get("to")).toBe(new Date(2026, 10, 10).toISOString());
  expect(screen.getByRole("textbox", { name: "统计开始日期" })).toHaveValue(
    "2026-",
  );
  expect(
    screen.getByText("筛选修改尚未生效，请点击刷新统计。"),
  ).toBeInTheDocument();
});

it("reversed statistics dates show field feedback without applying a query", async () => {
  const { queries } = show();
  await screen.findByText("42", { exact: true });
  fireEvent.change(screen.getByRole("textbox", { name: "统计开始日期" }), {
    target: { value: "2026-11-10" },
  });
  fireEvent.change(screen.getByRole("textbox", { name: "统计结束日期" }), {
    target: { value: "2026-11-08" },
  });
  await userEvent
    .setup()
    .click(screen.getByRole("button", { name: "刷新统计" }));
  expect(queries).toHaveLength(1);
  expect(screen.getByText("结束日期不能早于开始日期")).toBeInTheDocument();
});

it("valid leap day statistics apply local inclusive bounds and reset both the query and inputs", async () => {
  const { queries } = show();
  await screen.findByText("42", { exact: true });
  fireEvent.change(screen.getByRole("textbox", { name: "统计开始日期" }), {
    target: { value: "2024-02-29" },
  });
  fireEvent.change(screen.getByRole("textbox", { name: "统计结束日期" }), {
    target: { value: "2024-02-29" },
  });
  expect(queries).toHaveLength(1);
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "刷新统计" }));
  await waitFor(() => expect(queries).toHaveLength(2));
  const from = new Date(queries[1]!.get("from")!);
  const to = new Date(queries[1]!.get("to")!);
  expect([
    from.getFullYear(),
    from.getMonth(),
    from.getDate(),
    from.getHours(),
    from.getMinutes(),
    from.getSeconds(),
    from.getMilliseconds(),
  ]).toEqual([2024, 1, 29, 0, 0, 0, 0]);
  expect([
    to.getFullYear(),
    to.getMonth(),
    to.getDate(),
    to.getHours(),
    to.getMinutes(),
    to.getSeconds(),
    to.getMilliseconds(),
  ]).toEqual([2024, 2, 1, 0, 0, 0, 0]);
  await waitFor(() =>
    expect(screen.getByRole("combobox", { name: "统计流程" })).toBeEnabled(),
  );
  await chooseFlow("费用审批");
  await waitFor(() => expect(queries).toHaveLength(3));
  expect(queries[2]!.get("definitionKey")).toBe("expense");
  expect(queries[2]!.get("from")).toBe(queries[1]!.get("from"));
  expect(queries[2]!.get("to")).toBe(queries[1]!.get("to"));
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "重置筛选" })).toBeEnabled(),
  );
  await user.click(screen.getByRole("button", { name: "重置筛选" }));
  await waitFor(() => expect(queries).toHaveLength(4));
  expect(Object.fromEntries(queries[3]!)).toEqual({
    from: new Date(2026, 9, 11).toISOString(),
    to: new Date(2026, 10, 10).toISOString(),
  });
  expect(screen.getByRole("textbox", { name: "统计开始日期" })).toHaveValue(
    "2026-10-11",
  );
  expect(screen.getByRole("textbox", { name: "统计结束日期" })).toHaveValue(
    "2026-11-09",
  );
  expect(screen.getByRole("combobox", { name: "统计流程" })).toHaveAttribute(
    "data-value",
    "",
  );
});

it("default statistics explicitly query the last 30 local dates including today once", async () => {
  const { queries } = show();
  await screen.findByText("42", { exact: true });
  expect(queries).toHaveLength(1);
  expect(Object.fromEntries(queries[0]!)).toEqual({
    from: new Date(2026, 9, 11).toISOString(),
    to: new Date(2026, 10, 10).toISOString(),
  });
  expect(screen.getByRole("textbox", { name: "统计开始日期" })).toHaveValue(
    "2026-10-11",
  );
  expect(screen.getByRole("textbox", { name: "统计结束日期" })).toHaveValue(
    "2026-11-09",
  );
  expect(
    screen.getByText(/当前统计：2026-10-11 至 2026-11-09 · 全部流程/),
  ).toBeInTheDocument();
});
it("statistics show all real status metrics and shared readable duration units", async () => {
  show();
  await screen.findByText("42", { exact: true });
  for (const [label, value] of [
    ["已撤回", "5"],
    ["已终止", "3"],
    ["已退回", "3"],
    ["通过率", "78.1%"],
    ["平均审批耗时", "1.5 小时"],
    ["P90 耗时", "2 天"],
  ]) {
    const metric = screen
      .getAllByRole("listitem")
      .find((item) => within(item).queryByText(label!) !== null);
    expect(metric).toBeDefined();
    expect(
      within(metric!).getByText(value!, { exact: true }),
    ).toBeInTheDocument();
  }
  const table = screen.getAllByRole("table")[0]!;
  const headers = within(table)
    .getAllByRole("columnheader")
    .map((cell) => cell.textContent);
  expect(headers).toEqual(
    expect.arrayContaining([
      "总数",
      "运行中",
      "已退回",
      "已通过",
      "已驳回",
      "已撤回",
      "已终止",
      "平均耗时",
      "P90 耗时",
    ]),
  );
  const row = within(table).getByRole("row", { name: /费用审批/ });
  expect(
    within(row)
      .getAllByRole("cell")
      .map((cell) => cell.textContent),
  ).toEqual([
    "费用审批",
    "12",
    "2",
    "3",
    "25",
    "4",
    "5",
    "3",
    "1.5 小时",
    "2 天",
  ]);
});
it.each([
  { average: null, p90: null, averageText: "-", p90Text: "-" },
  { average: 0, p90: 45, averageText: "0 分钟", p90Text: "45 分钟" },
])(
  "duration values $average and $p90 keep unknown values distinct from zero",
  async ({ average, p90, averageText, p90Text }) => {
    show(false, [average, p90]);
    await screen.findByText("42", { exact: true });
    const metrics = screen.getAllByRole("listitem");
    expect(
      within(
        metrics.find((item) => within(item).queryByText("平均审批耗时"))!,
      ).getByText(averageText, { exact: true }),
    ).toBeInTheDocument();
    expect(
      within(
        metrics.find((item) => within(item).queryByText("P90 耗时"))!,
      ).getByText(p90Text, { exact: true }),
    ).toBeInTheDocument();
  },
);
it("flow selection applies its definition key with dates and clearing queries all flows", async () => {
  const { queries } = show();
  await screen.findByText("42", { exact: true });
  await chooseFlow("费用审批");
  await waitFor(() => expect(queries).toHaveLength(2));
  expect(Object.fromEntries(queries[1]!)).toEqual({
    ...Object.fromEntries(queries[0]!),
    definitionKey: "expense",
  });
  await waitFor(() =>
    expect(screen.getByText(/当前统计：.*费用审批/)).toBeInTheDocument(),
  );
  await chooseFlow("全部流程");
  await waitFor(() => expect(queries).toHaveLength(3));
  expect(Object.fromEntries(queries[2]!)).toEqual(
    Object.fromEntries(queries[0]!),
  );
});
it("a failed query keeps the last successful statistics and the actual successful filter range", async () => {
  const view = show();
  await screen.findByText("42", { exact: true });
  view.failNext();
  fireEvent.change(screen.getByRole("textbox", { name: "统计开始日期" }), {
    target: { value: "2026-11-01" },
  });
  expect(
    screen.getByText("筛选修改尚未生效，请点击刷新统计。"),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "刷新统计" }));
  await screen.findByText("服务暂不可用");
  expect(view.queries).toHaveLength(2);
  expect(screen.getByText("42", { exact: true })).toBeInTheDocument();
  expect(
    screen.getByText(/当前统计：2026-10-11 至 2026-11-09/),
  ).toBeInTheDocument();
});
it("new flow queries cancel stale requests and pending refresh cannot submit duplicates", async () => {
  const view = show();
  await screen.findByText("42", { exact: true });
  view.hold();
  await chooseFlow("费用审批");
  await waitFor(() => expect(view.pending).toHaveLength(1));
  expect(screen.getByRole("button", { name: "刷新统计" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "刷新统计" }));
  expect(view.queries).toHaveLength(2);
  await chooseFlow("请假审批");
  await waitFor(() => expect(view.pending).toHaveLength(2));
  expect(view.pending[0]!.signal.aborted).toBe(true);
  await act(async () =>
    view.pending[1]!.resolve(
      new Response(JSON.stringify(statistics(90, 2880, 99))),
    ),
  );
  await screen.findByText("99", { exact: true });
  await act(async () =>
    view.pending[0]!.resolve(
      new Response(JSON.stringify(statistics(90, 2880, 77))),
    ),
  );
  expect(screen.queryByText("77", { exact: true })).not.toBeInTheDocument();
  expect(screen.getByText("99", { exact: true })).toBeInTheDocument();
  expect(view.queries).toHaveLength(3);
});
it("an empty statistics date is rejected and never silently uses a backend default", async () => {
  const { queries } = show();
  await screen.findByText("42", { exact: true });
  fireEvent.change(screen.getByRole("textbox", { name: "统计结束日期" }), {
    target: { value: "" },
  });
  fireEvent.click(screen.getByRole("button", { name: "刷新统计" }));
  expect(screen.getByText("请选择结束日期")).toBeInTheDocument();
  expect(queries).toHaveLength(1);
});
it("users without statistics permission never request administrative data", async () => {
  const { queries, fetcher } = show(false, [90, 2880], false);
  await waitFor(() => expect(fetcher).toHaveBeenCalled());
  expect(queries).toHaveLength(0);
  expect(screen.getByRole("button", { name: "刷新统计" })).toBeDisabled();
  expect(screen.queryByText("42", { exact: true })).not.toBeInTheDocument();
});
it("flow list failure keeps statistics available and retry reloads only the flow options", async () => {
  const view = show(false, [90, 2880], true, "failure");
  await screen.findByText("流程列表暂不可用");
  await screen.findByText("42", { exact: true });
  fireEvent.click(screen.getByRole("button", { name: "重试" }));
  await waitFor(() =>
    expect(screen.queryByText("流程列表暂不可用")).not.toBeInTheDocument(),
  );
  await chooseFlow("费用审批");
  await waitFor(() => expect(view.queries).toHaveLength(2));
  expect(
    view.fetcher.mock.calls.filter(([url]) => String(url).endsWith("/flows")),
  ).toHaveLength(2);
  expect(view.queries[1]!.get("definitionKey")).toBe("expense");
});
it("an empty flow list still offers all flows and keeps the default statistics", async () => {
  const view = show(false, [90, 2880], true, "empty");
  await screen.findByText("42", { exact: true });
  await userEvent
    .setup()
    .click(screen.getByRole("combobox", { name: "统计流程" }));
  expect(screen.getAllByRole("option")).toHaveLength(1);
  expect(screen.getByRole("option", { name: "全部流程" })).toBeInTheDocument();
  expect(view.queries).toHaveLength(1);
});
it("identity changes clear retained statistics and late old responses cannot replace the new result", async () => {
  const view = show();
  await screen.findByText("42", { exact: true });
  view.hold();
  fireEvent.click(screen.getByRole("button", { name: "刷新统计" }));
  await waitFor(() => expect(view.pending).toHaveLength(1));
  await act(async () => {
    view.client.updateOptions({ userKey: () => "other-user" });
    await view.client.reloadUser();
  });
  expect(screen.queryByText("42", { exact: true })).not.toBeInTheDocument();
  await waitFor(() => expect(view.pending).toHaveLength(2));
  expect(view.pending[0]!.signal.aborted).toBe(true);
  await act(async () => view.pending[1]!.resolve(new Response(JSON.stringify(statistics(90, 2880, 99)))));
  await screen.findByText("99", { exact: true });
  await act(async () => view.pending[0]!.resolve(new Response(JSON.stringify(statistics(90, 2880, 77)))));
  expect(screen.queryByText("77", { exact: true })).not.toBeInTheDocument();
  expect(screen.getByText("99", { exact: true })).toBeInTheDocument();
  expect(view.queries).toHaveLength(3);
});
