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

function show(
  panel = false,
  form = true,
  canApprove = false,
  editableFields = canApprove ? ["name"] : [],
) {
  let status = "Running";
  const fetcher = vi.fn(async (input: string | URL | Request, _init?: RequestInit) => {
    const path = new URL(String(input), "http://test.local").pathname;
    const id = path.split("/")[4];
    let body: unknown = {};
    if (path.endsWith("/me")) body = { userId: "dave", capabilities: {} };
    else if (path.endsWith("/my-task"))
      body = {
        task: canApprove || editableFields.length
          ? { id: "task", nodeId: "start", kind: "Approval", status: "Pending" }
          : null,
        capabilities: {
          "approval.task.approve": canApprove,
          "approval.task.reject": canApprove,
        },
        editableFields,
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
                  fields: [
                    { key: "name", label: "动态名称", type: "Text", required: true },
                    { key: "locked", label: "只读字段", type: "Text" },
                  ],
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
        formDataJson: JSON.stringify(
          form ? { name: "数据 " + id, locked: "保留" } : { name: "数据 " + id },
        ),
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
    approvals: () =>
      fetcher.mock.calls.filter(
        ([url, init]) => String(url).endsWith("/approve") && init?.method === "POST",
      ),
    setStatus: (next: string) => {
      status = next;
    },
  };
}

it.each([
  { scenario: "with a dynamic form", form: true },
  { scenario: "without a dynamic form", form: false },
])("a registered business panel $scenario approves once without form changes", async ({ form }) => {
  const { approvals } = show(true, form, true);
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
  expect(approvals()).toHaveLength(1);
  const [url, request] = approvals()[0]!;
  expect(url).toMatch(/\/tasks\/task\/approve$/);
  expect(JSON.parse(String(request?.body)).formData).toBeNull();
  expect(new Headers(request?.headers).get("Idempotency-Key")).toBeTruthy();
});

it("the dynamic form fallback validates and submits only server-editable values", async () => {
  const { approvals } = show(false, true, true);
  const field = await screen.findByRole("textbox", { name: /动态名称/ });
  const approve = screen.getByRole("button", { name: "同意" });
  await waitFor(() => expect(approve).toBeEnabled());
  expect(field).toHaveValue("数据 one");
  expect(screen.getByText("保留")).toBeInTheDocument();
  fireEvent.change(field, { target: { value: "" } });
  fireEvent.click(approve);
  fireEvent.click(screen.getByRole("button", { name: "确认同意" }));
  expect(approvals()).toHaveLength(0);
  fireEvent.click(screen.getByRole("button", { name: "取消" }));
  fireEvent.change(field, { target: { value: "已修改" } });
  fireEvent.click(approve);
  fireEvent.click(screen.getByRole("button", { name: "确认同意" }));
  await screen.findByText("操作已完成");
  expect(approvals()).toHaveLength(1);
  expect(JSON.parse(String(approvals()[0]![1]?.body)).formData).toEqual({
    name: "已修改",
  });
});

it("a missing required editable form blocks approval but leaves rejection enabled", async () => {
  const { approvals } = show(false, false, true);
  const approve = await screen.findByRole("button", { name: "同意" });
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "驳回" })).toBeEnabled(),
  );
  expect(approve).toBeDisabled();
  fireEvent.click(approve);
  expect(screen.queryByRole("button", { name: "确认同意" })).not.toBeInTheDocument();
  expect(approvals()).toHaveLength(0);
});

it("approval without a form or editable fields sends one request", async () => {
  const { approvals } = show(false, false, true, []);
  const approve = await screen.findByRole("button", { name: "同意" });
  await waitFor(() => expect(approve).toBeEnabled());
  fireEvent.click(approve);
  fireEvent.click(screen.getByRole("button", { name: "确认同意" }));
  await screen.findByText("操作已完成");
  expect(approvals()).toHaveLength(1);
});

it("a business panel cannot grant approval even with editable fields", async () => {
  const { approvals, fetcher } = show(true, false, false, ["name"]);
  await screen.findByText("业务面板 business-one:Running");
  await waitFor(() =>
    expect(fetcher.mock.calls.some(([url]) => String(url).endsWith("/my-task"))).toBe(true),
  );
  expect(screen.queryByRole("button", { name: "同意" })).not.toBeInTheDocument();
  expect(approvals()).toHaveLength(0);
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
