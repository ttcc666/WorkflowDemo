import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { expect, it, vi } from "vitest";
import { StrictMode } from "react";
import { ApprovalProvider, createApprovalClient } from "../../src/client";
import { usePagedList } from "../../src/hooks/usePagedList";
import type { ListQuery } from "@approval/shared";
import { DataTable } from "../../src/components/ui/DataTable";
import { Pagination } from "../../src/components/ui/primitives";

it("preserves an initial URL page through owned Provider StrictMode setup and cleanup", async () => {
  const loader = vi.fn(async (query: ListQuery) => ({
    items: [query.page],
    total: 100,
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20,
  }));
  function List() {
    const list = usePagedList(loader, "todo_");
    return (
      <output>
        {list.page}:{list.pageSize}
      </output>
    );
  }
  const router = createMemoryRouter(
    [{ path: "/workbench", element: <List /> }],
    { initialEntries: ["/workbench?todo_page=3&todo_pageSize=50"] },
  );
  render(
    <StrictMode>
      <ApprovalProvider
        options={{
          userKey: () => "alice",
          fetcher: async () => new Response("{}"),
        }}
      >
        <RouterProvider router={router} />
      </ApprovalProvider>
    </StrictMode>,
  );
  await waitFor(() =>
    expect(loader.mock.calls.length).toBeGreaterThanOrEqual(2),
  );
  expect(screen.getByText("3:50")).toBeInTheDocument();
  expect(
    loader.mock.calls.every(
      ([query]) => query.page === 3 && query.pageSize === 50,
    ),
  ).toBe(true);
  expect(
    new URLSearchParams(router.state.location.search).get("todo_page"),
  ).toBe("3");
});

it("restores prefixed URL pagination and filters on load and history navigation and refreshes an unchanged search", async () => {
  const loader = vi.fn(async (query: ListQuery) => ({
    items: [String(query.page)],
    total: 100,
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20,
  }));
  function List() {
    const list = usePagedList(loader, "todo_");
    return (
      <>
        <output>
          {list.page}:{list.pageSize}:{list.filters.keyword}
        </output>
        <button onClick={() => list.paginate(4, 50)}>page</button>
        <button onClick={() => list.search()}>search</button>
        <button onClick={() => list.reset()}>reset</button>
      </>
    );
  }
  const router = createMemoryRouter(
    [{ path: "/workbench", element: <List /> }],
    {
      initialEntries: [
        "/workbench?todo_page=3&todo_pageSize=50&todo_keyword=part&done_page=7",
      ],
    },
  );
  const client = createApprovalClient({
    userKey: () => "alice",
    fetcher: async () => new Response("{}"),
  });
  render(
    <ApprovalProvider client={client}>
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  await waitFor(() =>
    expect(loader.mock.calls.at(-1)?.[0]).toMatchObject({
      page: 3,
      pageSize: 50,
      keyword: "part",
    }),
  );
  expect(screen.getByText("3:50:part")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "page" }));
  await waitFor(() =>
    expect(loader.mock.calls.at(-1)?.[0]).toMatchObject({
      page: 4,
      pageSize: 50,
    }),
  );
  expect(
    new URLSearchParams(router.state.location.search).get("done_page"),
  ).toBe("7");
  await act(async () => {
    await router.navigate(
      "/workbench?todo_page=2&todo_keyword=back&done_page=7",
    );
  });
  await waitFor(() =>
    expect(loader.mock.calls.at(-1)?.[0]).toMatchObject({
      page: 2,
      pageSize: 20,
      keyword: "back",
    }),
  );
  await act(async () => {
    await router.navigate(-1);
  });
  await waitFor(() =>
    expect(loader.mock.calls.at(-1)?.[0]).toMatchObject({
      page: 4,
      pageSize: 50,
      keyword: "part",
    }),
  );
  fireEvent.click(screen.getByRole("button", { name: "search" }));
  await waitFor(() =>
    expect(loader.mock.calls.at(-1)?.[0]).toMatchObject({
      page: 1,
      keyword: "part",
    }),
  );
  const before = loader.mock.calls.length;
  fireEvent.click(screen.getByRole("button", { name: "search" }));
  await waitFor(() => expect(loader).toHaveBeenCalledTimes(before + 1));
  await act(async () => {
    client.updateOptions({ userKey: () => "bob" });
  });
  fireEvent.click(screen.getByRole("button", { name: "page" }));
  await waitFor(() => expect(loader.mock.calls.at(-1)?.[0].page).toBe(4));
  await act(async () => {
    client.updateOptions({ userKey: () => "carol" });
  });
  await waitFor(() =>
    expect(loader.mock.calls.at(-1)?.[0]).toMatchObject({
      page: 1,
      pageSize: 50,
      keyword: "part",
    }),
  );
  fireEvent.click(screen.getByRole("button", { name: "reset" }));
  await waitFor(() =>
    expect(loader.mock.calls.at(-1)?.[0].keyword).toBeUndefined(),
  );
});

it("paged queries distinguish pending failure empty data refresh and recovery without a false zero total", async () => {
  function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (error: unknown) => void;
    const promise = new Promise<T>((yes, no) => {
      resolve = yes;
      reject = no;
    });
    return { resolve, reject, promise };
  }
  type Result = {
    items: { id: string; name: string }[];
    total: number;
    page: number;
    pageSize: number;
  };
  const jobs = Array.from({ length: 5 }, () => deferred<Result>());
  let call = 0;
  function List() {
    const list = usePagedList(() => jobs[call++]!.promise);
    return (
      <>
        <DataTable
          rows={list.items}
          rowKey={(row) => row.id}
          columns={[{ key: "name", label: "审批", render: (row) => row.name }]}
          loading={list.loading}
          error={list.error}
        />
        <Pagination
          page={list.page}
          pageSize={list.pageSize}
          total={list.total}
          loading={list.loading}
          onChange={list.paginate}
        />
        <button onClick={list.reload}>刷新</button>
      </>
    );
  }
  const router = createMemoryRouter([{ path: "/", element: <List /> }]);
  render(
    <ApprovalProvider
      options={{
        userKey: () => "alice",
        fetcher: async () => new Response("{}"),
      }}
    >
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  expect(screen.getByRole("status")).toHaveTextContent("总数加载中");
  expect(screen.queryByText("共 0 条")).not.toBeInTheDocument();
  await act(async () => jobs[0]!.reject(new Error("查询失败")));
  expect(screen.getByText("列表加载失败，请重试")).toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("总数未知");
  expect(screen.getByRole("button", { name: "下一页" })).toBeDisabled();
  expect(screen.queryByText("暂无数据")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "刷新" }));
  await act(async () =>
    jobs[1]!.resolve({ items: [], total: 0, page: 1, pageSize: 20 }),
  );
  expect(screen.getByText("暂无数据")).toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("共 0 条");
  fireEvent.click(screen.getByRole("button", { name: "刷新" }));
  await act(async () =>
    jobs[2]!.resolve({
      items: [{ id: "a", name: "真实审批" }],
      total: 41,
      page: 1,
      pageSize: 20,
    }),
  );
  expect(screen.getByText("真实审批")).toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("共 41 条");
  expect(screen.getByRole("button", { name: "下一页" })).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "刷新" }));
  expect(screen.getByText("真实审批")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "下一页" })).toBeDisabled();
  await act(async () => jobs[3]!.reject(new Error("刷新失败")));
  expect(screen.queryByText("真实审批")).not.toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("总数未知");
  fireEvent.click(screen.getByRole("button", { name: "刷新" }));
  await act(async () =>
    jobs[4]!.resolve({
      items: [{ id: "b", name: "恢复审批" }],
      total: 1,
      page: 1,
      pageSize: 20,
    }),
  );
  expect(screen.getByText("恢复审批")).toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("共 1 条");
});
