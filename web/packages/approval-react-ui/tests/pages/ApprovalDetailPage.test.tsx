import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { useState } from "react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { expect, it, vi } from "vitest";
import { ApprovalProvider } from "../../src/client";
import ApprovalDetailPage from "../../src/pages/ApprovalDetailPage";

function show(panel = false, form = true, canApprove = false) {
  let status = "Running";
  const fetcher = vi.fn(async (input: string | URL | Request) => {
    const path = new URL(String(input), "http://test.local").pathname;
    const id = path.split("/")[4];
    let body: unknown = {};
    if (path.endsWith("/me")) body = { userId: "dave", capabilities: {} };
    else if (path.endsWith("/my-task"))
      body = {
        task: canApprove
          ? { id: "task", nodeId: "start", kind: "Approval", status: "Pending" }
          : null,
        capabilities: { "approval.task.approve": canApprove },
        editableFields: canApprove ? ["name"] : [],
      };
    else if (path.endsWith("/definition"))
      body = {
        name: "测试流程",
        definitionJson: JSON.stringify({
          schemaVersion: 1,
          startNodeId: "start",
          nodes: { start: { id: "start", name: "审批", type: "Approval" } },
          ...(form
            ? {
                form: {
                  fields: [{ key: "name", label: "动态名称", type: "Text" }],
                },
              }
            : {}),
        }),
      };
    else if (/\/instances\/[^/]+$/.test(path))
      body = {
        id,
        businessType: "Test",
        businessKey: "business-" + id,
        status,
        initiatorId: "alice",
        definitionId: "definition",
        definitionKey: "test",
        definitionVersion: 1,
        title: "申请 " + id,
        createdAt: "2026-11-01T00:00:00Z",
        formDataJson: JSON.stringify({ name: "数据 " + id }),
        tasks: [],
        logs: [],
        capabilities: {},
      };
    else if (path.endsWith("/approve"))
      return new Response(null, { status: 204 });
    else if (path.endsWith("/carbon-copies")) body = [];
    return new Response(JSON.stringify(body));
  });
  function BusinessPanel({ businessKey }: { businessKey: string }) {
    const [initial] = useState(businessKey + ":" + status);
    return <p>业务面板 {initial}</p>;
  }
  const router = createMemoryRouter(
    [{ path: "/approvals/:id", element: <ApprovalDetailPage /> }],
    { initialEntries: ["/approvals/one"] },
  );
  const view = render(
    <ApprovalProvider
      options={{
        fetcher,
        businessPanels: panel ? { Test: BusinessPanel } : {},
      }}
    >
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  return {
    ...view,
    router,
    fetcher,
    setStatus: (next: string) => {
      status = next;
    },
  };
}

it("registered business panel takes priority over a loaded dynamic form and still allows approval", async () => {
  const { fetcher } = show(true, true, true);
  await screen.findByText("业务面板 business-one:Running");
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "同意" })).toBeEnabled(),
  );
  expect(
    screen.queryByText("动态名称", { exact: true }),
  ).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "同意" }));
  fireEvent.click(screen.getByRole("button", { name: "确认同意" }));
  await screen.findByText("操作已完成");
  expect(
    fetcher.mock.calls.filter(([url]) => String(url).endsWith("/approve")),
  ).toHaveLength(1);
});

it("without a business panel the dynamic form displays the instance data and denied actions stay hidden", async () => {
  show();
  await screen.findByText("动态名称", { exact: true });
  expect(screen.getByText("数据 one", { exact: true })).toBeInTheDocument();
  expect(screen.queryByText('{"name":"数据 one"}')).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "同意" }),
  ).not.toBeInTheDocument();
});

it("without a business panel or form the default content displays raw business data", async () => {
  show(false, false);
  await screen.findByRole("heading", { name: "业务内容" });
  expect(screen.getByText('{"name":"数据 one"}')).toBeInTheDocument();
});

it("changing instance or status remounts the panel with the current business key", async () => {
  const view = show(true);
  await screen.findByText("业务面板 business-one:Running");
  await act(() => view.router.navigate("/approvals/two"));
  await screen.findByText("业务面板 business-two:Running");
  expect(
    screen.queryByText("业务面板 business-one:Running"),
  ).not.toBeInTheDocument();
  view.setStatus("Approved");
  fireEvent.click(screen.getByRole("button", { name: "刷新详情" }));
  await screen.findByText("业务面板 business-two:Approved");
  expect(
    view.fetcher.mock.calls.filter(([url]) =>
      /\/instances\/two$/.test(String(url)),
    ),
  ).toHaveLength(2);
});
