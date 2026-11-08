import { useState } from "react";
import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { beforeAll, beforeEach, expect, it, vi } from "vitest";
import { EditorView } from "codemirror";
import { ActionScriptEditor } from "../../../src/features/designer/ActionScriptEditor";

const formatter = vi.hoisted(() => vi.fn());
vi.mock("prettier/standalone", () => ({ format: formatter }));
vi.mock("prettier/plugins/babel", () => ({ default: {} }));
vi.mock("prettier/plugins/estree", () => ({ default: {} }));
beforeAll(() => {
  Range.prototype.getClientRects ??= () => [] as unknown as DOMRectList;
  Range.prototype.getBoundingClientRect ??= () => new DOMRect();
});
beforeEach(() => {
  formatter.mockReset();
});
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (cause: unknown) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}
const original = "function execute(context){return {value:1}}";
const newer = "function execute(context){return {value:2}}";
const formatted = "function execute(context) {\n  return { value: 1 };\n}\n";
function show() {
  const changed = vi.fn();
  function Editor() {
    const [value, setValue] = useState(original);
    return (
      <>
        <ActionScriptEditor
          value={value}
          onChange={(next) => {
            changed(next);
            setValue(next);
          }}
          outputs={[]}
        />
        <output aria-label="模型代码">{value}</output>
      </>
    );
  }
  const rendered = render(<Editor />);
  const view = EditorView.findFromDOM(
    screen.getByRole("textbox", { name: "JavaScript" }),
  )!;
  const replace = (text: string) =>
    act(() =>
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: text },
      }),
    );
  return {
    ...rendered,
    view,
    replace,
    changed,
    button: screen.getByRole("button", { name: "格式化代码" }),
  };
}

it("formatting captures the clicked document before imports and never replaces newer input", async () => {
  const job = deferred<string>();
  formatter.mockReturnValue(job.promise);
  const editor = show();
  fireEvent.click(editor.button);
  editor.replace(newer);
  await waitFor(() =>
    expect(formatter).toHaveBeenCalledWith(
      original,
      expect.objectContaining({ parser: "babel" }),
    ),
  );
  expect(editor.button).toBeDisabled();
  await act(async () => job.resolve(formatted));
  expect(editor.view.state.doc.toString()).toBe(newer);
  expect(screen.getByLabelText("模型代码")).toHaveTextContent(newer);
  expect(editor.changed).toHaveBeenCalledOnce();
  expect(
    screen.getByText("代码已变化，未应用旧的格式化结果，请重新格式化。"),
  ).toBeInTheDocument();
  expect(editor.button).toBeEnabled();
});

it("formatting applies one successful result and blocks duplicate requests while busy", async () => {
  const job = deferred<string>();
  formatter.mockReturnValue(job.promise);
  const editor = show();
  fireEvent.click(editor.button);
  fireEvent.click(editor.button);
  await waitFor(() => expect(formatter).toHaveBeenCalledOnce());
  expect(editor.button).toHaveAttribute("aria-busy", "true");
  await act(async () => job.resolve(formatted));
  expect(editor.view.state.doc.toString()).toBe(formatted);
  expect(editor.changed).toHaveBeenCalledOnce();
  expect(editor.changed).toHaveBeenCalledWith(formatted);
  expect(editor.button).toBeEnabled();
  expect(screen.getByText("代码格式化完成")).toBeInTheDocument();
});

it("formatting rejects an old result even if the user edits and returns to the same text", async () => {
  const job = deferred<string>();
  formatter.mockReturnValue(job.promise);
  const editor = show();
  fireEvent.click(editor.button);
  await waitFor(() => expect(formatter).toHaveBeenCalledOnce());
  editor.replace(newer);
  editor.replace(original);
  await act(async () => job.resolve(formatted));
  expect(editor.view.state.doc.toString()).toBe(original);
  expect(editor.changed).toHaveBeenCalledTimes(2);
  expect(
    screen.getByText("代码已变化，未应用旧的格式化结果，请重新格式化。"),
  ).toBeInTheDocument();
});

it("formatting ignores an old failure and cannot overwrite the current syntax error", async () => {
  const job = deferred<string>();
  formatter.mockReturnValue(job.promise);
  const editor = show();
  fireEvent.click(editor.button);
  await waitFor(() => expect(formatter).toHaveBeenCalledOnce());
  editor.replace("function {");
  await act(async () => job.reject(new Error("旧格式化失败")));
  expect(editor.view.state.doc.toString()).toBe("function {");
  expect(screen.getByRole("alert")).toHaveTextContent("语法错误");
  expect(screen.queryByText("旧格式化失败")).not.toBeInTheDocument();
  expect(editor.button).toBeEnabled();
});

it("formatting cannot update a destroyed editor and permits retry after a current failure", async () => {
  const first = deferred<string>();
  formatter.mockReturnValueOnce(first.promise).mockResolvedValueOnce(formatted);
  const editor = show();
  fireEvent.click(editor.button);
  await waitFor(() => expect(formatter).toHaveBeenCalledOnce());
  await act(async () => first.reject(new Error("格式化失败")));
  expect(screen.getByRole("alert")).toHaveTextContent("格式化失败");
  expect(editor.button).toBeEnabled();
  fireEvent.click(editor.button);
  await waitFor(() => expect(editor.changed).toHaveBeenCalledWith(formatted));
  const third = deferred<string>();
  formatter.mockReturnValueOnce(third.promise);
  fireEvent.click(editor.button);
  await waitFor(() => expect(formatter).toHaveBeenCalledTimes(3));
  const calls = editor.changed.mock.calls.length;
  editor.unmount();
  await act(async () => third.resolve(newer));
  expect(editor.changed).toHaveBeenCalledTimes(calls);
});
