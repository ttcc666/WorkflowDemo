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
  const previews: { from: string; page: string | null; pageSize: string | null }[] = [];
  const heldPreviews: { signal: AbortSignal; resolve: (response: Response) => void }[] = [];
  let pausePreviews = false;
  let total = 21;
  let pausePost = slow;
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
        if (pausePost)
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
      if (path.endsWith("/me"))
        return new Response(JSON.stringify({
          userId: "admin",
          capabilities: { "approval.task.reassign-bulk": allowed },
        }));
      const match = /\/admin\/users\/([^/]+)\/tasks$/.exec(path);
      if (!match) return new Response(null, { status: 404 });
      const page = searchParams.get("page");
      previews.push({ from: match[1]!, page, pageSize: searchParams.get("pageSize") });
      if (pausePreviews) {
        return new Promise<Response>((resolve) => {
          heldPreviews.push({ signal: init!.signal!, resolve });
        });
      }
      return taskPreview(total, page === "2" ? "" : "待交接申请");
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
    previews,
    heldPreviews,
    holdPreviews: () => { pausePreviews = true; },
    holdPost: () => { pausePost = true; },
    setTotal: (next: number) => { total = next; },
    fetcher,
    client,
    complete: () => act(async () => finish?.(result())),
  };
}
function taskPreview(total: number, title = "待交接申请") {
  return new Response(JSON.stringify({
    items: total && title ? [{
      taskId: "task",
      instanceId: "instance",
      title,
      definitionName: "费用审批",
      nodeName: "审核",
      status: "Suspended",
      initiatorId: "dave",
      createdAt,
    }] : [],
    total,
  }));
}
function previewError() {
  return new Response(JSON.stringify({ title: "预览失败", status: 503 }), {
    status: 503,
    headers: { "Content-Type": "application/problem+json" },
  });
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

function submitButton() {
  return screen.getByRole("button", { name: "批量改派" });
}

async function openConfirmation() {
  await waitFor(() => expect(submitButton()).toBeEnabled());
  fireEvent.click(submitButton());
  return screen.getByRole("alertdialog", { name: "批量交接" });
}

async function submitHandover() {
  const dialog = await openConfirmation();
  fireEvent.click(within(dialog).getByRole("button", { name: "确认" }));
}

it("handover preview renders task details and requests the next page for the same user", async () => {
  const { fetcher } = show();
  await choose("alice", "bob");
  const row = within(await screen.findByRole("row", { name: /待交接申请/ }));
  for (const value of ["费用审批", "审核", "等待加签", "dave", formatDateTime(createdAt)])
    expect(row.getByRole("cell", { name: value })).toBeInTheDocument();
  expect(row.getByText("等待加签")).toHaveAttribute("data-status", "Suspended");
  expect(submitButton()).toBeEnabled();
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
  expect(submitButton()).toBeDisabled();
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
  await submitHandover();
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
  await submitHandover();
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
  const dialog = await openConfirmation();
  expect(view.posts).toHaveLength(0);
  fireEvent.click(within(dialog).getByRole("button", { name: "确认" }));
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
  const submit = submitButton();
  await openConfirmation();
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
  await submitHandover();
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
it("failed handover recovers the original key and body through the recovery control even with an empty preview", async () => {
  const view = show(true, true);
  await choose("alice", "bob", "  交接验收  ");
  const dialog = await openConfirmation();
  view.setTotal(0);
  fireEvent.click(within(dialog).getByRole("button", { name: "确认" }));
  await screen.findByText("交接失败");
  await screen.findByText("此人员没有待交接任务");
  expect(screen.queryByRole("alertdialog", { name: "批量交接" })).not.toBeInTheDocument();
  expect(submitButton()).toBeDisabled();
  const reason = screen.getByRole("textbox", { name: "交接原因" });
  expect(reason).toHaveValue("  交接验收  ");
  fireEvent.change(reason, { target: { value: "另一个理由" } });
  view.holdPost();
  const retry = screen.getByRole("button", { name: "按原内容重试上次操作" });
  fireEvent.click(retry);
  fireEvent.click(retry);
  await waitFor(() => expect(view.posts).toHaveLength(2));
  expect(retry).toBeDisabled();
  expect(reason).toBeDisabled();
  expect(screen.getByRole("combobox", { name: "每页条数" })).toBeDisabled();
  expect(view.posts[0]?.body).toEqual({ toUserId: "bob", reason: "交接验收" });
  expect(view.posts[0]?.key).toBeTruthy();
  expect(view.posts[1]).toEqual(view.posts[0]);
  const before = view.previews.length;
  await view.complete();
  await screen.findByRole("heading", { name: "交接结果" });
  const metrics = screen.getAllByRole("listitem");
  expect(within(metrics.find((item) => item.textContent?.includes("已改派任务"))!).getByText("2", { exact: true })).toBeVisible();
  expect(within(metrics.find((item) => item.textContent?.includes("跳过任务"))!).getByText("1", { exact: true })).toBeVisible();
  expect(screen.getByRole("row", { name: /跳过的申请.*已处理/ })).toBeVisible();
  expect(screen.queryByText("交接失败")).not.toBeInTheDocument();
  await waitFor(() => expect(view.previews).toHaveLength(before + 1));
  expect(view.posts).toHaveLength(2);
});
it("a pending handover blocks repeated confirmation and its refresh blocks stale preview submission", async () => {
  const view = show(true, false, true);
  await choose("alice", "bob");
  await submitHandover();
  await waitFor(() => expect(view.posts).toHaveLength(1));
  expect(screen.getByRole("button", { name: "确认" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "确认" }));
  expect(view.posts).toHaveLength(1);
  expect(
    screen.getByRole("textbox", { name: "交接原因", hidden: true }),
  ).toBeDisabled();
  view.holdPreviews();
  await view.complete();
  await screen.findByRole("heading", { name: "交接结果" });
  await waitFor(() => expect(view.heldPreviews).toHaveLength(1));
  expect(submitButton()).toBeDisabled();
  expect(screen.queryByText("待交接申请")).not.toBeInTheDocument();
  await act(async () => view.heldPreviews[0]!.resolve(taskPreview(0)));
  await screen.findByText("此人员没有待交接任务");
  expect(submitButton()).toBeDisabled();
  expect(view.posts).toHaveLength(1);
});
it("users without bulk-reassign permission cannot preview or submit handover tasks", async () => {
  const view = show(false);
  await waitFor(() => expect(view.fetcher).toHaveBeenCalled());
  const from = within(screen.getByRole("group", { name: "交出人员" })).getByRole("button", { name: "选择 alice" });
  expect(from).toBeDisabled();
  expect(submitButton()).toBeDisabled();
  fireEvent.click(from);
  fireEvent.click(submitButton());
  expect(
    view.fetcher.mock.calls.some(([url]) =>
      String(url).includes("/admin/users/"),
    ),
  ).toBe(false);
  expect(view.posts).toHaveLength(0);
});

it("new handover stays blocked during preview loading, failure, retry and a successful empty response", async () => {
  const view = show();
  view.holdPreviews();
  await choose("alice", "bob");
  await waitFor(() => expect(view.heldPreviews).toHaveLength(1));
  expect(submitButton()).toBeDisabled();
  expect(screen.getByRole("button", { name: "下一页" })).toBeDisabled();
  await act(async () => view.heldPreviews[0]!.resolve(previewError()));
  await screen.findByText("预览失败");
  expect(submitButton()).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "重试" }));
  await waitFor(() => expect(view.heldPreviews).toHaveLength(2));
  expect(submitButton()).toBeDisabled();
  await act(async () => view.heldPreviews[1]!.resolve(taskPreview(0)));
  await screen.findByText("此人员没有待交接任务");
  expect(screen.queryByText("预览失败")).not.toBeInTheDocument();
  expect(submitButton()).toBeDisabled();
  fireEvent.click(submitButton());
  expect(view.posts).toHaveLength(0);
});

it("changing page or sender blocks stale rows and discards an out-of-order preview response", async () => {
  const view = show();
  await choose("alice", "bob");
  await waitFor(() => expect(submitButton()).toBeEnabled());
  view.holdPreviews();
  const next = screen.getByRole("button", { name: "下一页" });
  fireEvent.click(next);
  fireEvent.click(next);
  await waitFor(() => expect(view.heldPreviews).toHaveLength(1));
  expect(next).toBeDisabled();
  expect(submitButton()).toBeDisabled();
  expect(screen.queryByText("待交接申请")).not.toBeInTheDocument();
  await choose("bob", "alice");
  await waitFor(() => expect(view.heldPreviews).toHaveLength(2));
  expect(view.heldPreviews[0]!.signal.aborted).toBe(true);
  expect(view.previews).toEqual([
    { from: "alice", page: "1", pageSize: "20" },
    { from: "alice", page: "2", pageSize: "20" },
    { from: "bob", page: "1", pageSize: "20" },
  ]);
  await act(async () => view.heldPreviews[1]!.resolve(taskPreview(4, "当前交出人员待办")));
  await screen.findByText("当前交出人员待办");
  await act(async () => view.heldPreviews[0]!.resolve(taskPreview(99, "已过期的待办")));
  expect(screen.queryByText("已过期的待办")).not.toBeInTheDocument();
  expect(screen.getByText("共 4 条")).toBeVisible();
  expect(submitButton()).toBeEnabled();
  expect(view.posts).toHaveLength(0);
});

it("page sizes 50 and 100 request page one and keep the server total and selected size", async () => {
  const view = show();
  view.setTotal(121);
  await choose("alice", "bob");
  await screen.findByText("共 121 条");
  const next = screen.getByRole("button", { name: "下一页" });
  const sizes = screen.getByRole("combobox", { name: "每页条数" });
  const user = userEvent.setup();
  for (const size of ["50", "100"]) {
    fireEvent.click(next);
    await waitFor(() => expect(sizes).toBeEnabled());
    await user.click(sizes);
    await user.click(screen.getByRole("option", { name: size + " / 页" }));
    await screen.findByText("待交接申请");
    expect(sizes).toHaveAttribute("data-value", size);
    expect(screen.getByText("共 121 条")).toBeVisible();
  }
  expect(view.previews).toEqual([
    { from: "alice", page: "1", pageSize: "20" },
    { from: "alice", page: "2", pageSize: "20" },
    { from: "alice", page: "1", pageSize: "50" },
    { from: "alice", page: "2", pageSize: "50" },
    { from: "alice", page: "1", pageSize: "100" },
  ]);
});

it("a stored unresolved command without its body blocks a conflicting handover until explicit review", async () => {
  const first = show(true, true);
  await choose("alice", "bob");
  await submitHandover();
  await screen.findByText("交接失败");
  first.unmount();
  const second = show();
  await choose("alice", "bob", "另一条交接");
  await screen.findByText("待交接申请");
  expect(screen.getByText(/当前页面已无原请求正文，请核对/)).toBeVisible();
  expect(screen.queryByRole("button", { name: "按原内容重试上次操作" })).not.toBeInTheDocument();
  expect(submitButton()).toBeDisabled();
  fireEvent.click(submitButton());
  expect(second.posts).toHaveLength(0);
  fireEvent.click(screen.getByRole("button", { name: "核对结果后开始新操作" }));
  const review = within(screen.getByRole("alertdialog", { name: "开始新操作" }));
  fireEvent.click(review.getByRole("button", { name: "确认" }));
  await submitHandover();
  await screen.findByRole("heading", { name: "交接结果" });
  expect(second.posts).toHaveLength(1);
  expect(second.posts[0]?.key).toBeTruthy();
  expect(second.posts[0]?.key).not.toBe(first.posts[0]?.key);
  expect(second.posts[0]?.body).toEqual({ toUserId: "bob", reason: "另一条交接" });
});

it("confirmation shows the normalized operation and cancellation or a blank reason never submits", async () => {
  const view = show();
  await choose("alice", "bob", " \t ");
  await screen.findByText("待交接申请");
  expect(submitButton()).toBeDisabled();
  const reason = screen.getByRole("textbox", { name: "交接原因" });
  fireEvent.change(reason, { target: { value: "  交接验收  " } });
  const dialog = await openConfirmation();
  expect(dialog).toHaveTextContent("交出人员：alice");
  expect(dialog).toHaveTextContent("接收人员：bob");
  expect(dialog).toHaveTextContent("待交接任务：21 条");
  expect(dialog).toHaveTextContent("交接原因：交接验收");
  expect(dialog).toHaveTextContent("角色任务不在范围内");
  expect(dialog).toHaveTextContent("按原内容和原幂等键恢复");
  fireEvent.click(within(dialog).getByRole("button", { name: "取消" }));
  expect(reason).toHaveValue("  交接验收  ");
  expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  expect(view.posts).toHaveLength(0);
});

it("identity changes abort pending previews and discard a late handover response", async () => {
  const view = show(true, false, true);
  await choose("alice", "bob");
  await submitHandover();
  await waitFor(() => expect(view.posts).toHaveLength(1));
  await act(async () => {
    view.client.updateOptions({ userKey: () => "other-admin" });
    await view.client.reloadUser();
  });
  expect(submitButton()).toBeDisabled();
  await view.complete();
  expect(screen.queryByText("已改派任务")).not.toBeInTheDocument();
  expect(screen.queryByText("上次提交结果尚未确认")).not.toBeInTheDocument();
  view.holdPreviews();
  await choose("alice", "bob");
  await waitFor(() => expect(view.heldPreviews).toHaveLength(1));
  await act(async () => {
    view.client.updateOptions({ userKey: () => "third-admin" });
    await view.client.reloadUser();
  });
  expect(view.heldPreviews[0]!.signal.aborted).toBe(true);
  await act(async () => view.heldPreviews[0]!.resolve(taskPreview(9, "旧身份待办")));
  expect(screen.queryByText("旧身份待办")).not.toBeInTheDocument();
  expect(submitButton()).toBeDisabled();
  expect(view.posts).toHaveLength(1);
});
