import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { expect, it, vi } from "vitest";
import { ApprovalProvider } from "../../src/client";
import { MonitorInstances } from "../../src/features/monitor/MonitorPanels";
import type { UserPickerProps } from "../../src/components/UserPicker";
import { localDayStart } from "@approval/shared/features/lists/listModel";

function show(entry = "/monitor", allowed = true) {
  const queries: URLSearchParams[] = [];
  const fetcher = vi.fn(async (input: string | URL | Request) => {
    const url = new URL(String(input), "http://test.local");
    if (url.pathname.endsWith("/admin/instances"))
      queries.push(url.searchParams);
    const body = url.pathname.endsWith("/me")
      ? {
          userId: "admin",
          capabilities: { "approval.instance.read-all": allowed },
        }
      : url.pathname.endsWith("/flows")
        ? []
        : { items: [], total: 100 };
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
