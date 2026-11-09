import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router";
import { expect, it, vi } from "vitest";
import { ApprovalProvider } from "../../src/client";
import MonitorPage from "../../src/pages/MonitorPage";

const all = {
  "approval.instance.read-all": true,
  "approval.statistics.read": true,
  "approval.task.reassign-bulk": true,
};
function show(entry = "/monitor", capabilities: Record<string, boolean> = all) {
  const requests: string[] = [];
  const fetcher = vi.fn(async (input: string | URL | Request) => {
    const path = new URL(String(input), "http://test.local").pathname;
    requests.push(path);
    return new Response(
      JSON.stringify(
        path.endsWith("/me")
          ? { userId: "admin", capabilities }
          : path.endsWith("/flows")
            ? []
            : path.endsWith("/statistics")
              ? {
                  summary: {
                    total: 1,
                    running: 1,
                    approved: 0,
                    rejected: 0,
                    returned: 0,
                    withdrawn: 0,
                    terminated: 0,
                    averageMinutes: null,
                    p90Minutes: null,
                  },
                  approvalRate: null,
                  overdueTasks: 0,
                  flows: [],
                  nodes: [],
                  backlog: [],
                }
              : { items: [], total: 0 },
      ),
    );
  });
  const router = createMemoryRouter(
    [
      { path: "/monitor", element: <MonitorPage /> },
      { path: "/before", element: <p>之前页面</p> },
    ],
    { initialEntries: ["/before", entry] },
  );
  const view = render(
    <ApprovalProvider options={{ fetcher }}>
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  return { ...view, router, requests };
}

it("switching monitor tabs replaces the URL and preserves existing filters without adding history", async () => {
  const view = show("/monitor?keep=1&monitor_keyword=budget");
  await screen.findByRole("tab", { name: "统计" });
  await userEvent.setup().click(screen.getByRole("tab", { name: "统计" }));
  await waitFor(() =>
    expect(
      new URLSearchParams(view.router.state.location.search).get("tab"),
    ).toBe("statistics"),
  );
  expect(
    new URLSearchParams(view.router.state.location.search).get(
      "monitor_keyword",
    ),
  ).toBe("budget");
  expect(
    new URLSearchParams(view.router.state.location.search).get("keep"),
  ).toBe("1");
  expect(view.router.state.historyAction).toBe("REPLACE");
  await act(() => view.router.navigate(-1));
  expect(screen.getByText("之前页面")).toBeInTheDocument();
});
it.each([
  { value: "statistics", label: "统计", content: "统计摘要" },
  { value: "handover", label: "离职交接", content: "待办交接" },
  { value: "instances", label: "全部流程", content: "实例列表" },
])(
  "direct URL access to $value selects only the authorized requested panel",
  async ({ value, label, content }) => {
    const view = show("/monitor?tab=" + value + "&keep=1");
    expect(await screen.findByRole("tab", { name: label })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    await screen.findByRole("heading", { name: content });
    expect(view.router.state.location.search).toBe("?tab=" + value + "&keep=1");
    expect(
      view.requests.filter((path) => path.endsWith("/admin/instances")),
    ).toHaveLength(value === "instances" ? 1 : 0);
  },
);
it("reloading a monitor URL restores the previously selected tab", async () => {
  const first = show();
  await screen.findByRole("tab", { name: "离职交接" });
  await userEvent.setup().click(screen.getByRole("tab", { name: "离职交接" }));
  const entry =
    first.router.state.location.pathname + first.router.state.location.search;
  first.unmount();
  const reloaded = show(entry);
  expect(await screen.findByRole("tab", { name: "离职交接" })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  expect(
    reloaded.requests.some((path) => path.endsWith("/admin/instances")),
  ).toBe(false);
});
it.each(["handover", "invalid", "instances"])(
  "unauthorized or invalid URL tab %s falls back to the first permitted tab and preserves parameters",
  async (value) => {
    const view = show("/monitor?tab=" + value + "&keep=1", {
      "approval.statistics.read": true,
    });
    await waitFor(() =>
      expect(
        new URLSearchParams(view.router.state.location.search).get("tab"),
      ).toBe("statistics"),
    );
    expect(screen.getByRole("tab", { name: "统计" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(
      screen.queryByRole("tab", { name: "离职交接" }),
    ).not.toBeInTheDocument();
    expect(
      new URLSearchParams(view.router.state.location.search).get("keep"),
    ).toBe("1");
    expect(
      view.requests.filter((path) => path.endsWith("/admin/instances")),
    ).toHaveLength(0);
  },
);
it("no monitor permissions keeps the denial page and sends no administrative requests", async () => {
  const view = show("/monitor?tab=handover", {});
  await waitFor(() => expect(view.requests).toContain("/api/approval/me"));
  expect(screen.getByText("没有流程监控权限")).toBeInTheDocument();
  expect(screen.queryByRole("tab")).not.toBeInTheDocument();
  expect(view.requests.filter((path) => path.includes("/admin/"))).toHaveLength(
    0,
  );
});
