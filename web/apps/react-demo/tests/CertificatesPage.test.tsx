// @vitest-environment jsdom
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, expect, it, vi } from "vitest";
import { ApprovalProvider } from "@approval/react-ui";
import CertificatesPage from "../src/pages/CertificatesPage";

const api = vi.hoisted(() => ({ list: vi.fn(), submitRevoke: vi.fn() }));
vi.mock("../src/api", () => ({ useCertificateApi: () => api }));
vi.mock("../src/features/CertificateDiagnosticsPanel", () => ({
  CertificateDiagnosticsPanel: () => null,
}));
vi.mock("../src/features/CertificateRequestPanel", () => ({
  CertificateRequestPanel: ({ businessKey }: { businessKey: string }) => (
    <p>申请详情 {businessKey}</p>
  ),
}));
beforeEach(() => {
  vi.clearAllMocks();
  api.list.mockResolvedValue({
    items: [
      {
        id: "cert-1",
        certNo: "C-001",
        name: "设备证书",
        holder: "Alice",
        certType: "Normal",
        issueDate: "2026-01-01",
        expireDate: "2027-12-31",
        status: "Active",
        effectiveStatus: "Valid",
        canManage: true,
        pendingRequestId: null,
      },
    ],
    total: 1,
    page: 1,
    pageSize: 20,
  });
  api.submitRevoke.mockResolvedValue({ requestId: "request-1" });
});
async function showRevoke() {
  const router = createMemoryRouter(
    [{ path: "/certificates", element: <CertificatesPage /> }],
    { initialEntries: ["/certificates"] },
  );
  render(
    <ApprovalProvider
      options={{
        fetcher: async () =>
          new Response(
            JSON.stringify({
              userId: "admin",
              roles: ["Admin"],
              capabilities: { "approval.instance.start": true },
            }),
          ),
      }}
    >
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  const user = userEvent.setup();
  await user.click(await screen.findByRole("button", { name: "申请注销" }));
  return {
    user,
    dialog: screen.getByRole("alertdialog", { name: "申请注销注册证" }),
  };
}
it("blank revoke reason shows a field error focuses the input and sends no command", async () => {
  const { user, dialog } = await showRevoke();
  await user.click(within(dialog).getByRole("button", { name: "确认" }));
  const reason = within(dialog).getByRole("textbox", { name: "注销原因" });
  expect(reason).toHaveFocus();
  expect(reason).toHaveAttribute("aria-invalid", "true");
  expect(within(dialog).getByText("请填写注销原因")).toBeInTheDocument();
  expect(api.submitRevoke).not.toHaveBeenCalled();
});
it("valid revoke reason submits once with an operation key and opens the resulting request", async () => {
  const { user, dialog } = await showRevoke();
  await user.type(
    within(dialog).getByRole("textbox", { name: "注销原因" }),
    "需要注销",
  );
  await user.click(within(dialog).getByRole("button", { name: "确认" }));
  await screen.findByText("申请详情 request-1");
  expect(api.submitRevoke).toHaveBeenCalledExactlyOnceWith(
    "cert-1",
    "需要注销",
    expect.stringMatching(/^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i),
    undefined,
  );
  expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  await waitFor(() => expect(api.list).toHaveBeenCalledTimes(2));
});
