import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { ApprovalProvider } from "../../src/client";
import { MonitorStatistics } from "../../src/features/monitor/MonitorPanels";

function show(firstFails = false) {
  const queries: URLSearchParams[] = [];
  const fetcher = vi.fn(async (input: string | URL | Request) => {
    const url = new URL(String(input), "http://example.test");
    if (url.pathname.endsWith("/admin/statistics"))
      queries.push(url.searchParams);
    if (url.pathname.endsWith("/admin/statistics") && firstFails) {
      firstFails = false;
      return new Response(
        JSON.stringify({ title: "服务暂不可用", status: 503 }),
        {
          status: 503,
          headers: { "Content-Type": "application/problem+json" },
        },
      );
    }
    return new Response(
      JSON.stringify({
        summary: {
          total: 42,
          running: 2,
          approved: 40,
          rejected: 0,
          returned: 0,
        },
        approvalRate: 95,
        overdueTasks: 0,
        flows: [],
        nodes: [],
        backlog: [],
        truncated: false,
      }),
    );
  });
  render(
    <ApprovalProvider options={{ fetcher }}>
      <MonitorStatistics />
    </ApprovalProvider>,
  );
  return queries;
}

it.each(["2026-", "2026-02-30", "2025-02-29", "0000-01-01", "10000-01-01"])(
  "invalid statistics date %s keeps the last result and never queries",
  async (value) => {
    const queries = show();
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
  const queries = show(true);
  await screen.findByText("服务暂不可用");
  fireEvent.change(screen.getByRole("textbox", { name: "统计开始日期" }), {
    target: { value: "2026-" },
  });
  await userEvent.setup().click(screen.getByRole("button", { name: "重试" }));
  await screen.findByText("42", { exact: true });
  expect(queries).toHaveLength(2);
  expect([...queries[1]!.keys()]).toEqual([]);
  expect(screen.getByRole("textbox", { name: "统计开始日期" })).toHaveValue(
    "2026-",
  );
  expect(
    screen.getByText("筛选修改尚未生效，请点击刷新统计。"),
  ).toBeInTheDocument();
});

it("reversed statistics dates show field feedback without applying a query", async () => {
  const queries = show();
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
  const queries = show();
  await screen.findByText("42", { exact: true });
  fireEvent.change(screen.getByRole("textbox", { name: "统计开始日期" }), {
    target: { value: "2024-02-29" },
  });
  fireEvent.change(screen.getByRole("textbox", { name: "统计结束日期" }), {
    target: { value: "2024-02-29" },
  });
  fireEvent.change(screen.getByRole("textbox", { name: "统计流程 Key" }), {
    target: { value: " expense " },
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
  expect(queries[1]!.get("definitionKey")).toBe("expense");
  await user.click(screen.getByRole("button", { name: "重置筛选" }));
  await waitFor(() => expect(queries).toHaveLength(3));
  expect([...queries[2]!.keys()]).toEqual([]);
  expect(screen.getByRole("textbox", { name: "统计开始日期" })).toHaveValue("");
  expect(screen.getByRole("textbox", { name: "统计结束日期" })).toHaveValue("");
  expect(screen.getByRole("textbox", { name: "统计流程 Key" })).toHaveValue("");
});
