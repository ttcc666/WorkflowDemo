import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ApprovalProvider } from "../../src/client";
import type { UserPickerProps } from "../../src/components/UserPicker";
import { HandoverPanel } from "../../src/features/monitor/MonitorPanels";

afterEach(() => sessionStorage.clear());
function show(allowed = true, fails = false, slow = false) {
  const posts: { path: string; body: unknown; key: string | null }[] = [];
  let finish: ((response: Response) => void) | undefined;
  const fetcher = vi.fn(
    async (input: string | URL | Request, init?: RequestInit) => {
      const path = new URL(String(input), "http://test.local").pathname;
      if (init?.method === "POST") {
        posts.push({
          path,
          body: JSON.parse(String(init.body)),
          key: new Headers(init.headers).get("Idempotency-Key"),
        });
        if (slow)
          return new Promise<Response>((resolve) => {
            finish = resolve;
          });
        if (fails) {
          fails = false;
          return new Response(
            JSON.stringify({ title: "交接失败", status: 503 }),
            {
              status: 503,
              headers: { "Content-Type": "application/problem+json" },
            },
          );
        }
        return result();
      }
      return new Response(
        JSON.stringify(
          path.endsWith("/me")
            ? {
                userId: "admin",
                capabilities: { "approval.task.reassign-bulk": allowed },
              }
            : {
                items: [
                  {
                    taskId: "task",
                    instanceId: "instance",
                    title: "待交接申请",
                    nodeName: "审核",
                  },
                ],
                total: 1,
              },
        ),
      );
    },
  );
  // A host-provided picker need not honor exclusions: the panel must validate again.
  function Picker({ value, onChange, label, disabled }: UserPickerProps) {
    return (
      <>
        <span>
          {label}当前 {value[0] || "空"}
        </span>
        {["alice", "bob"].map((id) => (
          <button key={id} disabled={disabled} onClick={() => onChange([id])}>
            选择 {id}
          </button>
        ))}
      </>
    );
  }
  const view = render(
    <ApprovalProvider options={{ fetcher, userPicker: Picker }}>
      <HandoverPanel />
    </ApprovalProvider>,
  );
  return {
    ...view,
    posts,
    fetcher,
    complete: () => act(async () => finish?.(result())),
  };
}
function result() {
  return new Response(
    JSON.stringify({
      count: 2,
      skipped: [
        {
          taskId: "skipped",
          instanceId: "skipped-instance",
          title: "跳过的申请",
          nodeName: "复核",
          reason: "已处理",
        },
      ],
    }),
  );
}
async function choose(from: string, to: string) {
  await waitFor(() =>
    expect(
      within(screen.getByRole("group", { name: "交出人员" })).getByRole(
        "button",
        { name: "选择 " + from },
      ),
    ).toBeEnabled(),
  );
  fireEvent.click(
    within(screen.getByRole("group", { name: "交出人员" })).getByRole(
      "button",
      { name: "选择 " + from },
    ),
  );
  fireEvent.click(
    within(screen.getByRole("group", { name: "接收人员" })).getByRole(
      "button",
      { name: "选择 " + to },
    ),
  );
  fireEvent.change(screen.getByRole("textbox", { name: "交接原因" }), {
    target: { value: "交接验收" },
  });
}
it("different handover users submit once with an idempotency key and display skipped task results", async () => {
  const view = show();
  await choose("alice", "bob");
  fireEvent.click(screen.getByRole("button", { name: "批量改派" }));
  expect(view.posts).toHaveLength(0);
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  await screen.findByRole("heading", { name: "交接结果" });
  expect(view.posts).toHaveLength(1);
  expect(view.posts[0]).toMatchObject({
    path: "/api/approval/admin/users/alice/reassign",
    body: { toUserId: "bob", reason: "交接验收" },
  });
  expect(view.posts[0]!.key).toBeTruthy();
  expect(screen.getByText("跳过的申请")).toBeInTheDocument();
  expect(screen.getByText("已处理")).toBeInTheDocument();
});
it("the same handover user disables submission and cannot issue a POST even from an open confirmation", async () => {
  const view = show();
  await choose("alice", "bob");
  const invalidReceiver = within(
    screen.getByRole("group", { name: "接收人员" }),
  ).getByRole("button", { name: "选择 alice" });
  const submit = screen.getByRole("button", { name: "批量改派" });
  fireEvent.click(screen.getByRole("button", { name: "批量改派" }));
  // Mutate the host picker while confirmation is open to exercise the handler guard.
  fireEvent.click(invalidReceiver);
  expect(submit).toBeDisabled();
  expect(screen.getByText("交出人员和接收人员不能相同")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  expect(view.posts).toHaveLength(0);
});
it("changing the handing-over user clears a conflicting receiver and resets the result", async () => {
  show();
  await choose("alice", "bob");
  fireEvent.click(screen.getByRole("button", { name: "批量改派" }));
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  await screen.findByRole("heading", { name: "交接结果" });
  fireEvent.click(
    within(screen.getByRole("group", { name: "交出人员" })).getByRole(
      "button",
      { name: "选择 bob" },
    ),
  );
  expect(screen.getByText("搜索接收人员当前 空")).toBeInTheDocument();
  expect(
    screen.queryByRole("heading", { name: "交接结果" }),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "批量改派" })).toBeDisabled();
});
it("failed handover keeps the confirmation and retries with the original key and body", async () => {
  const view = show(true, true);
  await choose("alice", "bob");
  fireEvent.click(screen.getByRole("button", { name: "批量改派" }));
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  await screen.findByText("交接失败");
  expect(
    screen.getByRole("alertdialog", { name: "批量交接" }),
  ).toBeInTheDocument();
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "确认" })).toBeEnabled(),
  );
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  await screen.findByRole("heading", { name: "交接结果" });
  expect(view.posts).toHaveLength(2);
  expect(view.posts[1]).toEqual(view.posts[0]);
});
it("a pending handover disables repeated confirmation and user edits", async () => {
  const view = show(true, false, true);
  await choose("alice", "bob");
  fireEvent.click(screen.getByRole("button", { name: "批量改派" }));
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  await waitFor(() => expect(view.posts).toHaveLength(1));
  expect(screen.getByRole("button", { name: "确认" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  expect(view.posts).toHaveLength(1);
  expect(
    screen.getByRole("textbox", { name: "交接原因", hidden: true }),
  ).toBeDisabled();
  await view.complete();
  await screen.findByRole("heading", { name: "交接结果" });
});
it("users without bulk-reassign permission cannot preview or submit handover tasks", async () => {
  const view = show(false);
  await waitFor(() => expect(view.fetcher).toHaveBeenCalled());
  expect(screen.getByRole("button", { name: "批量改派" })).toBeDisabled();
  expect(
    view.fetcher.mock.calls.some(([url]) =>
      String(url).includes("/admin/users/"),
    ),
  ).toBe(false);
  expect(view.posts).toHaveLength(0);
});
