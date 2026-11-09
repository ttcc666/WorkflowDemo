import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { createMemoryRouter, RouterProvider, Outlet, MemoryRouter, Routes, Route, Link } from "react-router";
import { expect, it, vi } from "vitest";
import { ApprovalProvider, createApprovalClient } from "../../src/client";
import { MessageBell } from "../../src/components/MessageBell";

function delayedBell(otherLoader?: () => Promise<null>) {
  const writes: RequestInit[] = []
  const replies: ((value: Response) => void)[] = []
  let read = false
  const client = createApprovalClient({ userKey: () => 'alice', fetcher: async (url, init) => {
    const actor = client.config().userKey?.() ?? 'alice'
    if (url.endsWith('/me')) return Response.json({ userId: actor, capabilities: { 'approval.message.read': true, 'approval.message.write': true } })
    if (url.endsWith('/messages/unread-count')) return Response.json({ count: actor === 'alice' ? (read ? 0 : 1) : 9 })
    if (url.endsWith('/messages/m1/read')) { writes.push(init); return new Promise<Response>(resolve => replies.push(resolve)) }
    return Response.json([{ id: 'm1', title: '延迟站内信', content: '需要处理', isRead: read, instanceId: 'i1', readAt: null, createdAt: '2026-11-09T00:00:00Z' }])
  } })
  const router = createMemoryRouter([{ path: '/', element: <><MessageBell /><Outlet /></>, children: [
    { path: 'workbench', element: <p>用户工作台</p> }, { path: 'other', element: <p>用户选择的页面</p>, loader: otherLoader },
    { path: 'approvals/:id', element: <p>目标审批详情</p> },
  ] }], { initialEntries: ['/workbench'] })
  render(<ApprovalProvider client={client}><RouterProvider router={router} /></ApprovalProvider>)
  let trigger!: HTMLElement
  return { client, router, writes, get trigger() { return trigger },
    click: async () => { trigger = await screen.findByRole('button', { name: '消息通知' }); fireEvent.click(trigger); fireEvent.click(await screen.findByRole('button', { name: '延迟站内信' })); await waitFor(() => expect(writes).toHaveLength(1)) },
    complete: (index = 0, status = 204) => act(async () => { read = status === 204; replies[index]!(status === 204 ? new Response(null, { status }) : Response.json({ title: '已读失败' }, { status })) }),
  }
}
it('a deferred unread message navigates normally when no navigation happened', async () => {
  const view = delayedBell(); await view.click()
  expect(view.router.state.location.pathname).toBe('/workbench')
  await view.complete()
  await waitFor(() => expect(view.router.state.location.pathname).toBe('/approvals/i1'))
  expect(view.writes).toHaveLength(1)
})
it.each(['other', 'same-path', 'back'])('a deferred unread message preserves the user navigation %s without cancelling its write', async scenario => {
  const view = delayedBell(); await view.click()
  const originalKey = view.router.state.location.key
  await act(async () => {
    await view.router.navigate(scenario === 'same-path' ? '/workbench?view=other' : '/other')
    if (scenario === 'back') await view.router.navigate(-1)
  })
  const chosen = view.router.state.location
  if (scenario === 'back') expect(chosen.key).toBe(originalKey)
  else expect(chosen.key).not.toBe(originalKey)
  expect(view.writes[0]!.signal?.aborted).toBe(false)
  await view.complete()
  expect(view.router.state.location).toEqual(chosen)
  expect(view.writes).toHaveLength(1)
  await waitFor(() => expect(view.trigger).toHaveTextContent('0'))
})
it('a deferred old identity message cannot navigate or update the new identity count', async () => {
  const view = delayedBell(); await view.click()
  await act(async () => { view.client.updateOptions({ userKey: () => 'bob' }); await view.client.reloadUser() })
  expect(view.writes[0]!.signal?.aborted).toBe(true)
  await waitFor(() => expect(screen.getByRole('button', { name: '消息通知' })).toHaveTextContent('9'))
  await view.complete()
  expect(view.router.state.location.pathname).toBe('/workbench')
  expect(screen.getByRole('button', { name: '消息通知' })).toHaveTextContent('9')
  expect(screen.queryByText('延迟站内信')).not.toBeInTheDocument()
})
it('preserves a user navigation whose destination loader has not completed yet', async () => {
  let finishLoader!: (value: null) => void
  const loader = vi.fn(() => new Promise<null>(resolve => { finishLoader = resolve }))
  const view = delayedBell(loader); await view.click()
  let navigation!: Promise<void>
  await act(async () => { navigation = view.router.navigate('/other') })
  expect(loader).toHaveBeenCalledTimes(1)
  expect(view.router.state.navigation.location?.pathname).toBe('/other')
  await view.complete()
  expect(view.router.state.navigation.location?.pathname).toBe('/other')
  expect(view.router.state.location.pathname).toBe('/workbench')
  await act(async () => { finishLoader(null); await navigation })
  expect(view.router.state.location.pathname).toBe('/other')
  expect(view.writes).toHaveLength(1)
})
it('a failed deferred read stays on the page and a new click reuses the same key before normal navigation', async () => {
  const view = delayedBell(); await view.click(); await view.complete(0, 503)
  await screen.findByText('已读失败')
  expect(view.router.state.location.pathname).toBe('/workbench')
  fireEvent.click(screen.getByRole('button', { name: '延迟站内信' }))
  await waitFor(() => expect(view.writes).toHaveLength(2))
  expect(new Headers(view.writes[1]!.headers).get('Idempotency-Key')).toBe(new Headers(view.writes[0]!.headers).get('Idempotency-Key'))
  await view.complete(1)
  await waitFor(() => expect(view.router.state.location.pathname).toBe('/approvals/i1'))
})

it('preserves navigation in a declarative host router while the original read completes', async () => {
  let complete!: (response: Response) => void
  let isRead = false
  const client = createApprovalClient({ fetcher: async url => {
    if (url.endsWith('/me')) return Response.json({ userId: 'alice', capabilities: { 'approval.message.read': true, 'approval.message.write': true } })
    if (url.endsWith('/messages/unread-count')) return Response.json({ count: isRead ? 0 : 1 })
    if (url.endsWith('/messages/m1/read')) return new Promise<Response>(resolve => { complete = resolve })
    return Response.json([{ id: 'm1', title: '宿主延迟通知', content: '', isRead, instanceId: 'i1', readAt: null, createdAt: '2026-11-09T00:00:00Z' }])
  } })
  render(<ApprovalProvider client={client}><MemoryRouter initialEntries={['/workbench']}>
    <MessageBell /><Link to="/other">打开宿主页面</Link>
    <Routes><Route path="/workbench" element={<p>宿主工作台</p>} /><Route path="/other" element={<p>宿主选择的页面</p>} /><Route path="/approvals/:id" element={<p>目标审批详情</p>} /></Routes>
  </MemoryRouter></ApprovalProvider>)
  fireEvent.click(await screen.findByRole('button', { name: '消息通知' }))
  fireEvent.click(await screen.findByRole('button', { name: '宿主延迟通知' }))
  await waitFor(() => expect(complete).toBeTypeOf('function'))
  fireEvent.click(screen.getByRole('button', { name: '关闭' }))
  fireEvent.click(screen.getByRole('link', { name: '打开宿主页面' }))
  expect(await screen.findByText('宿主选择的页面')).toBeVisible()
  await act(async () => { isRead = true; complete(new Response(null, { status: 204 })) })
  await waitFor(() => expect(screen.getByRole('button', { name: '消息通知' })).toHaveTextContent('0'))
  expect(screen.getByText('宿主选择的页面')).toBeVisible()
  expect(screen.queryByText('目标审批详情')).not.toBeInTheDocument()
})

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
