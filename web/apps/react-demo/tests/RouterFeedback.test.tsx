// @vitest-environment jsdom
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router";
import { expect, it } from "vitest";
import { NotFoundPage, RouterErrorPage } from "../src/features/RouterFeedback";

it("not found feedback provides a working route back to the workbench", async () => {
  const router = createMemoryRouter(
    [
      { path: "*", element: <NotFoundPage /> },
      { path: "/workbench", element: <h1>工作台</h1> },
    ],
    { initialEntries: ["/missing"] },
  );
  render(<RouterProvider router={router} />);
  expect(
    screen.getByRole("heading", { name: "页面不存在", level: 1 }),
  ).toBeInTheDocument();
  await userEvent
    .setup()
    .click(screen.getByRole("link", { name: "返回审批工作台" }));
  expect(
    await screen.findByRole("heading", { name: "工作台" }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("heading", { name: "页面不存在" }),
  ).not.toBeInTheDocument();
});
it("route failures show recoverable Chinese feedback without exposing internal error text", async () => {
  const router = createMemoryRouter(
    [
      {
        path: "/error",
        loader: () => {
          throw new Error("internal private stack");
        },
        element: <p>成功</p>,
        errorElement: <RouterErrorPage />,
      },
    ],
    { initialEntries: ["/error"] },
  );
  render(<RouterProvider router={router} />);
  expect(
    await screen.findByRole("heading", { name: "页面加载失败", level: 1 }),
  ).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "重新加载" })).toBeEnabled();
  expect(screen.getByRole("link", { name: "返回审批工作台" })).toHaveAttribute(
    "href",
    "/workbench",
  );
  expect(screen.queryByText("internal private stack")).not.toBeInTheDocument();
});
