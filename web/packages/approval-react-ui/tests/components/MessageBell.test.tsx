import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { createMemoryRouter, RouterProvider, Outlet } from "react-router";
import { expect, it, vi } from "vitest";
import { ApprovalProvider, createApprovalClient } from "../../src/client";
import { MessageBell } from "../../src/components/MessageBell";

it("marks only unread messages once and navigates already-read messages without another write", async () => {
  let isRead = false;
  const writes: RequestInit[] = [];
  const client = createApprovalClient({
    fetcher: async (url, init) => {
      if (url.endsWith("/me"))
        return Response.json({
          userId: "alice",
          capabilities: {
            "approval.message.read": true,
            "approval.message.write": true,
          },
        });
      if (url.endsWith("/messages/unread-count"))
        return Response.json({ count: isRead ? 0 : 1 });
      if (url.includes("/messages/m1/read")) {
        isRead = true;
        writes.push(init!);
        return new Response(null, { status: 204 });
      }
      if (url.includes("/messages"))
        return Response.json([
          {
            id: "m1",
            title: "审批通知",
            content: "需要处理",
            isRead,
            readAt: null,
            instanceId: "i1",
            createdAt: "2026-11-06T00:00:00Z",
          },
        ]);
      return Response.json({});
    },
  });
  const router = createMemoryRouter([{ path: "/", element: <><MessageBell /><Outlet /></>, children: [
    { path: 'workbench', element: <p>工作台</p> },
    { path: 'approvals/:id', element: <p>详情</p> },
  ] }], {
    initialEntries: ["/workbench"],
  });
  render(
    <ApprovalProvider client={client}>
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  fireEvent.click(await screen.findByRole("button", { name: "消息通知" }));
  fireEvent.click(await screen.findByRole("button", { name: "审批通知" }));
  await waitFor(() =>
    expect(router.state.location.pathname).toBe("/approvals/i1"),
  );
  expect(writes).toHaveLength(1);
  expect(new Headers(writes[0]!.headers).get("Idempotency-Key")).toMatch(
    /^[a-f0-9-]{36}$/i,
  );
  fireEvent.click(screen.getByRole("button", { name: "消息通知" }));
  fireEvent.click(await screen.findByRole("button", { name: "审批通知" }));
  await waitFor(() =>
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
  );
  expect(writes).toHaveLength(1);
});

it('keeps message detail navigation inside a nested host route', async () => {
  const client = createApprovalClient({ fetcher: async (url) => {
    if (url.endsWith('/me')) return Response.json({ userId: 'alice', capabilities: { 'approval.message.read': true } })
    if (url.endsWith('/messages/unread-count')) return Response.json({ count: 0 })
    return Response.json([{ id: 'm1', title: '宿主审批通知', content: '', isRead: true, readAt: '2026-11-06T00:00:00Z', instanceId: 'i1', createdAt: '2026-11-06T00:00:00Z' }])
  } })
  const router = createMemoryRouter([{ path: '/host/workflow', element: <><MessageBell /><Outlet /></>, children: [
    { path: 'workbench', element: <p>工作台</p> },
    { path: 'approvals/:id', element: <p>宿主详情</p> },
  ] }], { initialEntries: ['/host/workflow/workbench'] })
  render(<ApprovalProvider client={client}><RouterProvider router={router} /></ApprovalProvider>)
  fireEvent.click(await screen.findByRole('button', { name: '消息通知' }))
  fireEvent.click(await screen.findByRole('button', { name: '宿主审批通知' }))
  await waitFor(() => expect(router.state.location.pathname).toBe('/host/workflow/approvals/i1'))
  expect(screen.getByText('宿主详情')).toBeInTheDocument()
})

it("defaults message write permissions to denied and clears notifications when their owner changes", async () => {
  const fetcher = vi.fn(async (url: string) => {
    if (url.endsWith("/me"))
      return Response.json({
        userId: "alice",
        capabilities: { "approval.message.read": true },
      });
    if (url.endsWith("/messages/unread-count"))
      return Response.json({ count: 1 });
    return Response.json([
      {
        id: "m1",
        title: "旧身份消息",
        content: "旧数据",
        isRead: false,
        readAt: null,
        instanceId: "i1",
        createdAt: "2026-11-06T00:00:00Z",
      },
    ]);
  });
  const client = createApprovalClient({ userKey: () => "alice", fetcher });
  const router = createMemoryRouter([{ path: "*", element: <MessageBell /> }]);
  render(
    <ApprovalProvider client={client}>
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  fireEvent.click(await screen.findByRole("button", { name: "消息通知" }));
  expect(
    await screen.findByRole("button", { name: "旧身份消息" }),
  ).toBeDisabled();
  expect(
    screen.queryByRole("button", { name: "全部标记已读" }),
  ).not.toBeInTheDocument();
  await act(async () => {
    client.updateOptions({ userKey: () => "bob" });
  });
  expect(screen.queryByText("旧身份消息")).not.toBeInTheDocument();
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(fetcher.mock.calls.some(([url]) => url.endsWith("/read"))).toBe(false);
});
