import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router";
import { expect, it, vi } from "vitest";
import { ApprovalProvider, createApprovalClient } from "../../src/client";
import { MonitorInstances } from "../../src/features/monitor/MonitorPanels";
import type { UserPickerProps } from "../../src/components/UserPicker";
import { localDayStart } from "@approval/shared/features/lists/listModel";
import { formatDateTime } from "@approval/shared";

const completedAt = "2026-11-02T08:30:00Z";

function show(entry = "/monitor", allowed = true) {
  const queries: URLSearchParams[] = [];
  const fetcher = vi.fn(async (input: string | URL | Request) => {
    const url = new URL(String(input), "http://test.local");
    if (url.pathname.endsWith("/admin/instances"))
      queries.push(url.searchParams);
    const body = url.pathname.endsWith("/me")
      ? {
          userId: "admin",
          capabilities: {
            "approval.instance.read-all": allowed,
            "approval.instance.terminate": allowed,
          },
        }
      : url.pathname.endsWith("/flows")
        ? []
        : {
            items: [
              { id: "open", title: "申请中", status: "Running", initiatorId: "alice", completedAt: null },
              { id: "done", title: "申请完成", status: "Approved", initiatorId: "bob", completedAt },
            ].map((item) => ({
              ...item,
              definitionName: "费用审批",
              createdAt: "2026-11-01T08:30:00Z",
            })),
            total: 100,
          };
    return new Response(JSON.stringify(body));
  });
  function Picker({ value, onChange, disabled }: UserPickerProps) {
    return (
      <>
        <span>已选发起人 {value[0] || "全部"}</span>
        <button
          type="button"
          disabled={disabled}
          onClick={() => onChange(["alice"])}
        >
          选择 Alice
        </button>
        <button type="button" disabled={disabled} onClick={() => onChange([])}>
          清空发起人
        </button>
      </>
    );
  }
  const router = createMemoryRouter(
    [{ path: "/monitor", element: <MonitorInstances /> }],
    { initialEntries: [entry] },
  );
  render(
    <ApprovalProvider options={{ fetcher, userPicker: Picker }}>
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  return { queries, router, fetcher };
}

it("initiator selection composes every applied filter resets pagination and preserves unrelated URL parameters", async () => {
  const view = show(
    "/monitor?tab=instances&keep=1&monitor_page=3&monitor_keyword=budget&monitor_status=Running&monitor_definitionKey=expense&monitor_from=2026-11-01&monitor_to=2026-11-09&monitor_overdueOnly=1",
  );
  await waitFor(() => expect(view.queries).toHaveLength(1));
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "选择 Alice" })).toBeEnabled(),
  );
  fireEvent.click(screen.getByRole("button", { name: "选择 Alice" }));
  await waitFor(() => expect(view.queries).toHaveLength(2));
  expect(Object.fromEntries(view.queries[1]!)).toMatchObject({
    initiatorId: "alice",
    page: "1",
    pageSize: "20",
    keyword: "budget",
    status: "Running",
    definitionKey: "expense",
    from: localDayStart("2026-11-01"),
    to: localDayStart("2026-11-09", 1),
    overdueOnly: "true",
  });
  const params = new URLSearchParams(view.router.state.location.search);
  expect(params.get("monitor_initiatorId")).toBe("alice");
  expect(params.has("monitor_page")).toBe(false);
  expect(params.get("keep")).toBe("1");
  expect(params.get("tab")).toBe("instances");
});

it("instance rows show API initiators and completion times while retaining eligible termination", async () => {
  show();
  const open = within(await screen.findByRole("row", { name: /申请中/ }));
  const done = within(screen.getByRole("row", { name: /申请完成/ }));
  expect(open.getByRole("cell", { name: "alice" })).toBeInTheDocument();
  expect(open.getByRole("cell", { name: "-" })).toBeInTheDocument();
  expect(done.getByRole("cell", { name: "bob" })).toBeInTheDocument();
  expect(done.getByRole("cell", { name: formatDateTime(completedAt) })).toBeInTheDocument();
  expect(done.queryByRole("button", { name: "终止流程" })).not.toBeInTheDocument();
  fireEvent.click(open.getByRole("button", { name: "终止流程" }));
  expect(screen.getByRole("alertdialog", { name: "终止流程" })).toBeVisible();
});

it("monitor page size offers four choices and resets the API and URL to page one", async () => {
  const { queries, router } = show("/monitor?keep=1&monitor_page=3");
  const control = screen.getByRole("combobox", { name: "每页条数" });
  await waitFor(() => expect(control).toBeEnabled());
  const user = userEvent.setup();
  await user.click(control);
  expect(screen.getAllByRole("option", { name: /\/ 页$/ }).map((option) => option.textContent)).toEqual([
    "10 / 页", "20 / 页", "50 / 页", "100 / 页",
  ]);
  await user.click(screen.getByRole("option", { name: "10 / 页" }));
  await waitFor(() => expect(queries).toHaveLength(2));
  expect(Object.fromEntries(queries[0]!)).toEqual({ page: "3", pageSize: "20" });
  expect(Object.fromEntries(queries[1]!)).toEqual({ page: "1", pageSize: "10" });
  expect(router.state.location.search).toBe("?keep=1&monitor_pageSize=10");
  await waitFor(() => expect(control).toBeEnabled());
  await user.click(control);
  await user.click(screen.getByRole("option", { name: "20 / 页" }));
  await waitFor(() => expect(queries).toHaveLength(3));
  expect(Object.fromEntries(queries[2]!)).toEqual({ page: "1", pageSize: "20" });
  expect(router.state.location.search).toBe("?keep=1");
});

it("clearing the initiator removes its API and URL parameters while preserving other filters", async () => {
  const view = show(
    "/monitor?monitor_initiatorId=alice&monitor_keyword=budget",
  );
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "清空发起人" })).toBeEnabled(),
  );
  expect(view.queries[0]!.get("initiatorId")).toBe("alice");
  fireEvent.click(screen.getByRole("button", { name: "清空发起人" }));
  await waitFor(() => expect(view.queries).toHaveLength(2));
  expect(view.queries[1]!.has("initiatorId")).toBe(false);
  expect(view.queries[1]!.get("keyword")).toBe("budget");
  expect(
    new URLSearchParams(view.router.state.location.search).has(
      "monitor_initiatorId",
    ),
  ).toBe(false);
});

it("reset clears the initiator and all list filters and restores the first page", async () => {
  const view = show(
    "/monitor?keep=1&monitor_initiatorId=alice&monitor_page=4&monitor_keyword=budget&monitor_status=Running",
  );
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "重置" })).toBeEnabled(),
  );
  fireEvent.click(screen.getByRole("button", { name: "重置" }));
  await waitFor(() => expect(view.queries).toHaveLength(2));
  expect(Object.fromEntries(view.queries[1]!)).toEqual({
    page: "1",
    pageSize: "20",
  });
  expect(screen.getByRole("textbox", { name: "关键词" })).toHaveValue("");
  expect(
    within(screen.getByRole("group", { name: "发起人" })).getByText(
      "已选发起人 全部",
    ),
  ).toBeInTheDocument();
  expect(view.router.state.location.search).toBe("?keep=1");
});

it("invalid draft dates prevent initiator changes from issuing an invalid combined request", async () => {
  const view = show();
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "选择 Alice" })).toBeEnabled(),
  );
  fireEvent.change(screen.getByRole("textbox", { name: "开始日期" }), {
    target: { value: "2026-02-30" },
  });
  fireEvent.click(screen.getByRole("button", { name: "选择 Alice" }));
  expect(view.queries).toHaveLength(1);
  expect(screen.getByText("已选发起人 alice")).toBeInTheDocument();
});

it("users without read-all permission never request the administrative instance list", async () => {
  const view = show("/monitor?monitor_initiatorId=alice", false);
  await waitFor(() => expect(view.fetcher).toHaveBeenCalled());
  expect(view.queries).toHaveLength(0);
});

it('revoking terminate permission in an open monitor confirmation prevents a write', async () => {
  const posts: unknown[] = []
  const client = createApprovalClient({ fetcher: vi.fn(async (url, init) => {
    if (init.method === 'POST') { posts.push(JSON.parse(String(init.body))); return new Response(null, { status: 204 }) }
    if (url.endsWith('/me')) return Response.json({ userId: 'admin', capabilities: { 'approval.instance.read-all': true, 'approval.instance.terminate': true } })
    if (url.endsWith('/flows')) return Response.json([])
    return Response.json({ items: [{ id: 'instance', title: '真实管理对象', status: 'Running', definitionName: '审批', createdAt: '' }], total: 1 })
  }) })
  const router = createMemoryRouter([{ path: '/monitor', Component: MonitorInstances }], { initialEntries: ['/monitor'] })
  render(<ApprovalProvider client={client}><RouterProvider router={router} /></ApprovalProvider>)
  await screen.findByText('真实管理对象')
  fireEvent.click(screen.getByRole('button', { name: '终止流程' }))
  const dialog = screen.getByRole('alertdialog', { name: '终止流程' })
  fireEvent.change(screen.getByRole('textbox', { name: '终止原因' }), { target: { value: '  核对后终止  ' } })
  await waitFor(() => expect(within(dialog).getByRole('button', { name: '确认' })).toBeEnabled())
  act(() => { client.me.value = { ...client.me.value!, capabilities: { 'approval.instance.read-all': true } } })
  expect(within(dialog).getByRole('button', { name: '确认' })).toBeDisabled()
  fireEvent.click(within(dialog).getByRole('button', { name: '确认' }))
  expect(posts).toEqual([])
})
