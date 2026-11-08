import { act, fireEvent, render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
import { ApprovalProvider, createApprovalClient } from "../../src/client";
import { useAsyncData } from "../../src/hooks/useAsyncData";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}
const client = () =>
  createApprovalClient({ fetcher: async () => new Response("{}") });
it("ignores an old response and error after route inputs change even when the loader ignores cancellation", async () => {
  const first = deferred<string>();
  const second = deferred<string>();
  const supplied = client();
  function Panel({ id }: { id: string }) {
    const data = useAsyncData(
      () => (id === "first" ? first.promise : second.promise),
      [id],
    );
    return (
      <div>
        {data.data ?? "empty"}
        {data.error ? "error" : ""}
      </div>
    );
  }
  const view = render(
    <ApprovalProvider client={supplied}>
      <Panel id="first" />
    </ApprovalProvider>,
  );
  await act(async () => {
    await Promise.resolve();
  });
  view.rerender(
    <ApprovalProvider client={supplied}>
      <Panel id="second" />
    </ApprovalProvider>,
  );
  await act(async () => {
    second.resolve("new-data");
  });
  expect(screen.getByText("new-data")).toBeInTheDocument();
  await act(async () => {
    first.reject(new Error("old-error"));
  });
  expect(screen.getByText("new-data")).toBeInTheDocument();
  expect(screen.queryByText("error")).not.toBeInTheDocument();
});
it("retains the same identity snapshot while refreshing but clears it for a new identity", async () => {
  const first = deferred<string>();
  const refresh = deferred<string>();
  const next = deferred<string>();
  let call = 0;
  const supplied = createApprovalClient({
    userKey: () => "alice",
    fetcher: async () => new Response("{}"),
  });
  function Panel() {
    const data = useAsyncData(() => [first, refresh, next][call++]!.promise);
    return (
      <>
        <span>{data.data ?? "empty"}</span>
        <button onClick={data.reload}>refresh</button>
      </>
    );
  }
  render(
    <ApprovalProvider client={supplied}>
      <Panel />
    </ApprovalProvider>,
  );
  await act(async () => {
    first.resolve("alice-data");
  });
  fireEvent.click(screen.getByText("refresh"));
  expect(screen.getByText("alice-data")).toBeInTheDocument();
  await act(async () => {
    supplied.updateOptions({ userKey: () => "bob" });
  });
  expect(screen.queryByText("alice-data")).not.toBeInTheDocument();
  await act(async () => {
    refresh.resolve("late-alice");
    next.resolve("bob-data");
  });
  expect(screen.getByText("bob-data")).toBeInTheDocument();
  expect(screen.queryByText("late-alice")).not.toBeInTheDocument();
});
it("turns a synchronous loader failure into an error state rather than an unhandled effect error", async () => {
  function Panel() {
    const data = useAsyncData<string>(() => {
      throw new Error("invalid query");
    });
    return (
      <span>
        {data.error instanceof Error ? data.error.message : "waiting"}
      </span>
    );
  }
  render(
    <ApprovalProvider client={client()}>
      <Panel />
    </ApprovalProvider>,
  );
  expect(await screen.findByText("invalid query")).toBeInTheDocument();
});
