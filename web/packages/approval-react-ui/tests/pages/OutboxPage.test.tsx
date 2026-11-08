import { act, render } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { createMemoryRouter, RouterProvider } from "react-router";
import { ApprovalProvider } from "../../src/client";
import OutboxPage from "../../src/pages/OutboxPage";
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});
function show() {
  const jobs: { resolve: (response: Response) => void; signal: AbortSignal }[] =
    [];
  const fetcher = vi.fn(
    async (url: string | URL | Request, init?: RequestInit) => {
      const path = String(url);
      if (path.includes("/outbox"))
        return new Promise<Response>((resolve) =>
          jobs.push({ resolve, signal: init!.signal as AbortSignal }),
        );
      return new Response(
        JSON.stringify(
          path.endsWith("/me")
            ? {
                userId: "admin",
                roles: ["Admin"],
                capabilities: { "approval.outbox.read": true },
              }
            : {},
        ),
      );
    },
  );
  const router = createMemoryRouter([{ path: "/", element: <OutboxPage /> }]);
  const view = render(
    <ApprovalProvider options={{ fetcher }}>
      <RouterProvider router={router} />
    </ApprovalProvider>,
  );
  const complete = (index: number) =>
    act(async () => {
      jobs[index]!.resolve(
        new Response(
          JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }),
        ),
      );
      await vi.advanceTimersByTimeAsync(0);
    });
  return { ...view, jobs, complete };
}
it("outbox polling waits for a slow query to finish before scheduling the next read", async () => {
  vi.useFakeTimers();
  vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
  vi.spyOn(navigator, "onLine", "get").mockReturnValue(true);
  const view = show();
  await act(async () => vi.advanceTimersByTimeAsync(0));
  expect(view.jobs).toHaveLength(1);
  await act(async () => vi.advanceTimersByTimeAsync(10000));
  expect(view.jobs).toHaveLength(1);
  expect(view.jobs[0]!.signal.aborted).toBe(false);
  await view.complete(0);
  await act(async () => vi.advanceTimersByTimeAsync(2999));
  expect(view.jobs).toHaveLength(1);
  await act(async () => vi.advanceTimersByTimeAsync(1));
  expect(view.jobs).toHaveLength(2);
  await act(async () => vi.advanceTimersByTimeAsync(9000));
  expect(view.jobs).toHaveLength(2);
  expect(view.jobs[1]!.signal.aborted).toBe(false);
});
it("outbox polling pauses when hidden or offline resumes once and cleans up on unmount", async () => {
  vi.useFakeTimers();
  let visibility: DocumentVisibilityState = "visible";
  let online = true;
  vi.spyOn(document, "visibilityState", "get").mockImplementation(
    () => visibility,
  );
  vi.spyOn(navigator, "onLine", "get").mockImplementation(() => online);
  const view = show();
  await act(async () => vi.advanceTimersByTimeAsync(0));
  await view.complete(0);
  visibility = "hidden";
  act(() => document.dispatchEvent(new Event("visibilitychange")));
  await act(async () => vi.advanceTimersByTimeAsync(30000));
  expect(view.jobs).toHaveLength(1);
  visibility = "visible";
  act(() => {
    document.dispatchEvent(new Event("visibilitychange"));
    document.dispatchEvent(new Event("visibilitychange"));
  });
  await act(async () => vi.advanceTimersByTimeAsync(0));
  expect(view.jobs).toHaveLength(2);
  await view.complete(1);
  online = false;
  act(() => window.dispatchEvent(new Event("offline")));
  await act(async () => vi.advanceTimersByTimeAsync(30000));
  expect(view.jobs).toHaveLength(2);
  online = true;
  act(() => window.dispatchEvent(new Event("online")));
  await act(async () => vi.advanceTimersByTimeAsync(0));
  expect(view.jobs).toHaveLength(3);
  view.unmount();
  await act(async () => {
    vi.advanceTimersByTime(30000);
    document.dispatchEvent(new Event("visibilitychange"));
    window.dispatchEvent(new Event("online"));
  });
  expect(view.jobs).toHaveLength(3);
  expect(view.jobs[2]!.signal.aborted).toBe(true);
});
