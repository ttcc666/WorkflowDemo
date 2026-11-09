import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { formatDateTime } from "@approval/shared";
import { ApprovalProvider, createApprovalClient } from "../../src/client";
import type { UserPickerProps } from "../../src/components/UserPicker";
import { HandoverPanel } from "../../src/features/monitor/MonitorPanels";

afterEach(() => sessionStorage.clear());
const createdAt = "2026-11-01T08:30:00Z";

function show(allowed = true, fails = false, slow = false) {
  const posts: { path: string; body: unknown; key: string | null }[] = [];
  let finish: ((response: Response) => void) | undefined;
  const fetcher = vi.fn(
    async (input: string | URL | Request, init?: RequestInit) => {
      const { pathname: path, searchParams } = new URL(
        String(input),
        "http://test.local",
      );
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
                items: searchParams.get("page") === "2" ? [] : [
                  {
                    taskId: "task",
                    instanceId: "instance",
                    title: "待交接申请",
                    definitionName: "费用审批",
                    nodeName: "审核",
                    status: "Suspended",
                    initiatorId: "dave",
                    createdAt,
                  },
                ],
                total: 21,
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
  const client = createApprovalClient({ fetcher, userPicker: Picker });
  const view = render(
    <ApprovalProvider client={client}>
      <HandoverPanel />
    </ApprovalProvider>,
  );
  return {
    ...view,
    posts,
    fetcher,
    client,
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
async function choose(from: string, to: string, reason = "交接验收") {
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
  if (reason) {
    fireEvent.change(screen.getByRole("textbox", { name: "交接原因" }), {
      target: { value: reason },
    });
  }
}

it("handover preview renders task details and requests the next page for the same user", async () => {
  const { fetcher } = show();
  await choose("alice", "bob");
  const row = within(await screen.findByRole("row", { name: /待交接申请/ }));
  for (const value of ["费用审批", "审核", "等待加签", "dave", formatDateTime(createdAt)])
    expect(row.getByRole("cell", { name: value })).toBeInTheDocument();
  expect(row.getByText("等待加签")).toHaveAttribute("data-status", "Suspended");
  fireEvent.click(screen.getByRole("button", { name: "下一页" }));
  expect(screen.getByRole("table").parentElement?.parentElement).toHaveAttribute("aria-busy", "true");
  await screen.findByText("此人员没有待交接任务");
  const requests = fetcher.mock.calls
    .map(([url]) => new URL(String(url), "http://test.local"))
    .filter((url) => url.pathname.endsWith("/admin/users/alice/tasks"));
  expect(requests.map((url) => Object.fromEntries(url.searchParams))).toEqual([
    { page: "1", pageSize: "20" },
    { page: "2", pageSize: "20" },
  ]);
  expect(screen.queryByText("待交接申请")).not.toBeInTheDocument();
});

it("handover submits the default reason and explains the scope and recovery rules", async () => {
  const { posts } = show();
  await choose("alice", "bob", "");
  expect(screen.getByRole("textbox", { name: "交接原因" })).toHaveValue("离职交接");
  const rules = screen.getByText(/只改派指派给本人的待办/).parentElement!;
  for (const rule of [
    "只改派指派给本人的待办",
    "依次审批中尚未轮到的任务",
    "等待加签的任务",
    "角色任务不在交接范围内",
    "新处理人是申请发起人，或已是当前节点审批人时会跳过",
    "处理时限不会重置",
    "部分完成或结果未知时，必须使用原来的交接内容和幂等键恢复",
  ]) expect(rules).toHaveTextContent(rule);
  fireEvent.click(screen.getByRole("button", { name: "批量改派" }));
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  await screen.findByRole("heading", { name: "交接结果" });
  expect(posts).toHaveLength(1);
  expect(posts[0]?.body).toEqual({ toUserId: "bob", reason: "离职交接" });
});

it("the reason caps user input at 200 characters and identity changes clear the handover", async () => {
  const { client, posts } = show();
  await choose("alice", "bob");
  await screen.findByText("待交接申请");
  const field = screen.getByRole("textbox", { name: "交接原因" });
  const user = userEvent.setup();
  fireEvent.change(field, { target: { value: "交".repeat(199) } });
  expect(field).toHaveValue("交".repeat(199));
  await user.type(field, "接替");
  expect(field).toHaveValue("交".repeat(199) + "接");
  fireEvent.click(screen.getByRole("button", { name: "批量改派" }));
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  await screen.findByRole("heading", { name: "交接结果" });
  expect(posts[0]?.body).toEqual({ toUserId: "bob", reason: "交".repeat(199) + "接" });
  await act(async () => {
    client.updateOptions({ userKey: () => "other-admin" });
    await client.reloadUser();
  });
  expect(screen.getByRole("textbox", { name: "交接原因" })).toHaveValue("离职交接");
  expect(screen.getByText("搜索交出人员当前 空")).toBeInTheDocument();
  expect(screen.getByText("搜索接收人员当前 空")).toBeInTheDocument();
  expect(screen.getByText("选择交出人员后显示待办预览")).toBeInTheDocument();
  expect(screen.queryByRole("heading", { name: "交接结果" })).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "批量改派" })).toBeDisabled();
});
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
