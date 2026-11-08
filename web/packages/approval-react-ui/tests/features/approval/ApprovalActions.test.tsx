import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import type { ApprovalInstance, ApprovalTask } from "@approval/shared";
import { ApprovalProvider, createApprovalClient } from "../../../src/client";
import { ApprovalActions, type ApprovalActionInput } from "../../../src/features/approval/ApprovalActions";

const task = {
  id: "task",
  nodeId: "review",
  kind: "Approval",
  status: "Pending",
  capabilities: {},
} as ApprovalTask;
const instance = {
  id: "instance",
  initiatorId: "alice",
  status: "Running",
  tasks: [task],
  canWithdraw: false,
  capabilities: {},
} as ApprovalInstance;
const client = () =>
  createApprovalClient({ fetcher: async () => new Response("{}") });
const base = {
  instance,
  task,
  node: { id: "review", name: "审批", type: "Approval" as const },
  data: {},
  changes: [],
  userId: "dave",
  busy: false,
};
it('matches Vue reassign exclusions and allows an optional reason without excluding the administrator', async () => {
  const reassigned = { ...task, assigneeKind: 'User' as const, assigneeValue: 'dave', capabilities: { 'approval.task.reassign': true } }
  const onAction = vi.fn(async (_input: ApprovalActionInput) => true)
  const picker = vi.fn(({ excludedUsers, onChange }: { excludedUsers?: string[]; onChange: (ids: string[]) => void }) =>
    <button onClick={() => onChange(['admin'])} disabled={excludedUsers?.includes('admin')}>选择管理员</button>)
  const supplied = createApprovalClient({ fetcher: async () => new Response('{}'), userPicker: picker })
  render(<ApprovalProvider client={supplied}><ApprovalActions {...base}
    instance={{ ...instance, tasks: [reassigned] }} task={null} userId="admin" capabilities={{}} onAction={onAction}
  /></ApprovalProvider>)
  fireEvent.click(screen.getByRole('button', { name: '改派 dave' }))
  expect(picker.mock.calls.at(-1)?.[0].excludedUsers).toEqual(['alice', 'dave'])
  expect(screen.getByRole('button', { name: '确认改派' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: '选择管理员' }))
  fireEvent.change(screen.getByRole('textbox', { name: '审批意见' }), { target: { value: '   ' } })
  expect(screen.getByRole('button', { name: '确认改派' })).toBeEnabled()
  fireEvent.click(screen.getByRole('button', { name: '确认改派' }))
  await waitFor(() => expect(onAction).toHaveBeenCalledOnce())
  expect(onAction.mock.calls[0]?.[0]).toMatchObject({ kind: 'reassign', taskId: 'task', comment: '', users: ['admin'] })
})
it("uses the my-task resource capability map rather than task list administration capabilities", () => {
  render(
    <ApprovalProvider client={client()}>
      <ApprovalActions
        {...base}
        capabilities={{
          "approval.task.approve": true,
          "approval.task.reject": false,
        }}
        onAction={vi.fn()}
      />
    </ApprovalProvider>,
  );
  expect(screen.getByRole("button", { name: "同意" })).toBeEnabled();
  expect(
    screen.queryByRole("button", { name: "驳回" }),
  ).not.toBeInTheDocument();
});
it("denies missing permissions even when a task is assigned and displayed", () => {
  render(
    <ApprovalProvider client={client()}>
      <ApprovalActions {...base} capabilities={{}} onAction={vi.fn()} />
    </ApprovalProvider>,
  );
  expect(
    screen.queryByRole("button", { name: "同意" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "撤回" }),
  ).not.toBeInTheDocument();
});
it("disables an open approval dialog when its resource permission is revoked", () => {
  const callback = vi.fn();
  const supplied = client();
  const view = render(
    <ApprovalProvider client={supplied}>
      <ApprovalActions
        {...base}
        capabilities={{ "approval.task.approve": true }}
        onAction={callback}
      />
    </ApprovalProvider>,
  );
  fireEvent.click(screen.getByRole("button", { name: "同意" }));
  view.rerender(
    <ApprovalProvider client={supplied}>
      <ApprovalActions
        {...base}
        capabilities={{ "approval.task.approve": false }}
        onAction={callback}
      />
    </ApprovalProvider>,
  );
  expect(screen.getByRole("button", { name: "确认同意" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "确认同意" }));
  expect(callback).not.toHaveBeenCalled();
});
it("requires the configured approval comment and disables duplicate submission while busy", () => {
  const callback = vi.fn();
  const supplied = client();
  const view = render(
    <ApprovalProvider client={supplied}>
      <ApprovalActions
        {...base}
        node={{ ...base.node, requireComment: true }}
        capabilities={{ "approval.task.approve": true }}
        onAction={callback}
      />
    </ApprovalProvider>,
  );
  fireEvent.click(screen.getByRole("button", { name: "同意" }));
  expect(screen.getByRole("button", { name: "确认同意" })).toBeDisabled();
  fireEvent.change(screen.getByRole("textbox", { name: "审批意见" }), {
    target: { value: "同意" },
  });
  expect(screen.getByRole("button", { name: "确认同意" })).toBeEnabled();
  view.rerender(
    <ApprovalProvider client={supplied}>
      <ApprovalActions
        {...base}
        busy
        node={{ ...base.node, requireComment: true }}
        capabilities={{ "approval.task.approve": true }}
        onAction={callback}
      />
    </ApprovalProvider>,
  );
  expect(screen.getByRole("button", { name: "确认同意" })).toBeDisabled();
});
