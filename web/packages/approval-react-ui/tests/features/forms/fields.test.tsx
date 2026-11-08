import { useState } from "react";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import userEvent from "@testing-library/user-event";
import type { FormField, FormSchema, FormValues } from "@approval/shared";
import {
  ApprovalProvider,
  createApprovalClient,
  type ApprovalUIOptions,
} from "../../../src/client";
import {
  DynamicForm,
  FormValueText,
} from "../../../src/features/forms/DynamicForm";

afterEach(() => vi.useRealTimers());
function show(
  field: FormField,
  initial: unknown,
  fetcher: NonNullable<ApprovalUIOptions["fetcher"]> = async () =>
    new Response("{}"),
) {
  const changed = vi.fn();
  const source = { [field.key]: initial };
  const client = createApprovalClient({ fetcher });
  function Form() {
    const [value, setValue] = useState<FormValues>(source);
    return (
      <DynamicForm
        schema={{ fields: [field] }}
        value={value}
        onChange={(next) => {
          changed(next);
          setValue(next);
        }}
      />
    );
  }
  render(
    <ApprovalProvider client={client}>
      <Form />
    </ApprovalProvider>,
  );
  return { changed, source, client };
}
it.each([
  ["Text", "textbox", "initial", "changed", "changed"],
  ["TextArea", "textbox", "first", "first\nsecond", "first\nsecond"],
  ["Number", "spinbutton", 2, "4.25", 4.25],
  ["Money", "spinbutton", 2.5, "4.75", 4.75],
] as const)(
  "keeps %s input typed and leaves the original model unchanged",
  (type, role, initial, input, expected) => {
    const form = show({ key: "field", label: "字段", type }, initial);
    fireEvent.change(screen.getByRole(role, { name: "字段" }), {
      target: { value: input },
    });
    expect(form.changed).toHaveBeenLastCalledWith({ field: expected });
    expect(form.source.field).toBe(initial);
  },
);
it("keeps Date and DateRange as date strings without a timezone conversion", () => {
  const form = show({ key: "dates", label: "日期区间", type: "DateRange" }, [
    "2026-11-06",
    "2026-11-07",
  ]);
  fireEvent.change(screen.getByLabelText("日期区间结束"), {
    target: { value: "2026-11-09" },
  });
  expect(form.changed).toHaveBeenLastCalledWith({
    dates: ["2026-11-06", "2026-11-09"],
  });
});
it("edits a Date as one canonical day string", () => {
  const form = show({ key: "date", label: "日期", type: "Date" }, "2026-11-06");
  fireEvent.change(screen.getByLabelText("日期", { selector: "input" }), {
    target: { value: "2026-11-08" },
  });
  expect(form.changed).toHaveBeenLastCalledWith({ date: "2026-11-08" });
});
it("uses option values rather than labels for Select", async () => {
  const form = show(
    {
      key: "choice",
      label: "选择",
      type: "Select",
      options: [
        { value: "a", label: "Alpha" },
        { value: "b", label: "Beta" },
      ],
    },
    "a",
  );
  const user = userEvent.setup();
  await user.click(screen.getByRole("combobox", { name: "选择" }));
  await user.click(screen.getByRole("option", { name: "Beta" }));
  expect(form.changed).toHaveBeenLastCalledWith({ choice: "b" });
});
it("preserves the selected array ordering for MultiSelect and removes only the unchecked value", () => {
  const form = show(
    {
      key: "choice",
      label: "多选",
      type: "MultiSelect",
      options: [
        { value: "a", label: "Alpha" },
        { value: "b", label: "Beta" },
      ],
    },
    ["a"],
  );
  fireEvent.click(screen.getByRole("checkbox", { name: "Beta" }));
  expect(form.changed).toHaveBeenLastCalledWith({ choice: ["a", "b"] });
  fireEvent.click(screen.getByRole("checkbox", { name: "Alpha" }));
  expect(form.changed).toHaveBeenLastCalledWith({ choice: ["b"] });
  expect(form.source.choice).toEqual(["a"]);
});
it("Radio groups in separate form instances do not change one another", () => {
  const field: FormField = {
    key: "radio",
    label: "单选",
    type: "Radio",
    options: [
      { value: "a", label: "Alpha" },
      { value: "b", label: "Beta" },
    ],
  };
  const left = show(field, "a");
  const right = show(field, "b");
  const alpha = screen.getAllByRole("radio", { name: "Alpha" });
  const beta = screen.getAllByRole("radio", { name: "Beta" });
  fireEvent.click(beta[0]!);
  expect(left.changed).toHaveBeenLastCalledWith({ radio: "b" });
  expect(right.changed).not.toHaveBeenCalled();
  expect(beta[0]).toBeChecked();
  expect(beta[1]).toBeChecked();
  expect(alpha[1]).not.toBeChecked();
});
it("Switch sends a boolean and applies its required rule on change", () => {
  const form = show(
    { key: "accepted", label: "承诺", type: "Switch", required: true },
    false,
  );
  fireEvent.click(screen.getByRole("switch", { name: "承诺" }));
  expect(form.changed).toHaveBeenLastCalledWith({ accepted: true });
  fireEvent.click(screen.getByRole("switch", { name: "承诺" }));
  expect(screen.getByRole("alert")).toHaveTextContent("请勾选承诺");
});
it.each(["User", "Users"] as const)(
  "selects %s using the actual directory IDs and removes a selected user",
  async (type) => {
    vi.useFakeTimers();
    const people = [
      { id: "u1", name: "One", roles: [], isActive: true },
      { id: "u2", name: "Two", roles: [], isActive: true },
    ];
    const form = show(
      { key: "person", label: "人员", type },
      type === "User" ? null : ["u1"],
      async () => new Response(JSON.stringify(people)),
    );
    fireEvent.focus(screen.getByRole("combobox", { name: "人员" }));
    fireEvent.change(screen.getByRole("combobox", { name: "人员" }), {
      target: { value: "Two" },
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200);
    });
    fireEvent.click(
      within(screen.getByRole("listbox", { name: "人员搜索结果" })).getByRole(
        "option",
        { name: /Two/ },
      ),
    );
    expect(form.changed).toHaveBeenLastCalledWith({
      person: type === "User" ? "u2" : ["u1", "u2"],
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    fireEvent.click(screen.getByRole("button", { name: "删除 Two" }));
    expect(form.changed).toHaveBeenLastCalledWith({
      person: type === "User" ? null : ["u1"],
    });
  },
);
it("adds and deletes Table rows with six supported column types and computes row formulas and totals", () => {
  const schema: FormSchema = {
    fields: [
      {
        key: "items",
        label: "明细",
        type: "Table",
        columns: [
          { key: "name", label: "项目", type: "Text" },
          { key: "quantity", label: "数量", type: "Number" },
          { key: "date", label: "明细日期", type: "Date" },
          {
            key: "unit",
            label: "单位",
            type: "Select",
            options: [{ label: "件", value: "piece" }],
          },
          { key: "price", label: "价格", type: "Money", sum: true },
          {
            key: "subtotal",
            label: "行总额",
            type: "Formula",
            formula: "price * 2",
          },
        ],
      },
      {
        key: "total",
        label: "总额",
        type: "Formula",
        formula: "items_price_sum",
      },
    ],
  };
  const changed = vi.fn();
  function Form() {
    const [value, setValue] = useState<FormValues>({
      items: [{ name: "first", price: 2 }],
    });
    return (
      <DynamicForm
        schema={schema}
        value={value}
        onChange={(next) => {
          changed(next);
          setValue(next);
        }}
      />
    );
  }
  render(
    <ApprovalProvider
      client={createApprovalClient({ fetcher: async () => new Response("{}") })}
    >
      <Form />
    </ApprovalProvider>,
  );
  expect(screen.getByLabelText("总额", { selector: "input" })).toHaveValue("2");
  fireEvent.change(screen.getByRole("spinbutton", { name: "价格" }), {
    target: { value: "3.5" },
  });
  expect(changed.mock.calls.at(-1)?.[0]).toEqual({
    items: [{ name: "first", price: 3.5, subtotal: 7 }],
    total: 3.5,
  });
  fireEvent.click(screen.getByRole("button", { name: "添加一行" }));
  expect(changed.mock.calls.at(-1)?.[0].items).toEqual([
    { name: "first", price: 3.5, subtotal: 7 },
    {
      name: null,
      quantity: null,
      date: null,
      unit: null,
      price: null,
      subtotal: null,
    },
  ]);
  fireEvent.click(screen.getByRole("button", { name: "删除行 1" }));
  expect(changed.mock.calls.at(-1)?.[0].items).toEqual([
    {
      name: null,
      quantity: null,
      date: null,
      unit: null,
      price: null,
      subtotal: null,
    },
  ]);
});
it("formats readonly Money and Formula with Vue precision and summarizes tables and empty selections", () => {
  render(
    <>
      <FormValueText
        field={{ key: "amount", label: "金额", type: "Money" }}
        value={1234.5}
      />
      <FormValueText
        field={{ key: "total", label: "公式", type: "Formula", precision: 3 }}
        value={2.5}
      />
      <FormValueText
        field={{ key: "rows", label: "明细", type: "Table" }}
        value={[{}, {}]}
      />
      <FormValueText
        field={{ key: "options", label: "多选", type: "MultiSelect" }}
        value={[]}
      />
    </>,
  );
  expect(screen.getByText("1,234.50")).toBeInTheDocument();
  expect(screen.getByText("2.500")).toBeInTheDocument();
  expect(screen.getByText("共 2 行")).toBeInTheDocument();
  expect(screen.getByText("—")).toBeInTheDocument();
});
it("passes readonly and editable user states to the host picker without changing ID types", () => {
  const changed = vi.fn();
  const supplied = createApprovalClient({
    fetcher: async () => new Response("{}"),
    userPicker: ({ value, onChange, disabled, multiple, label }) => (
      <button
        aria-label={label ?? "只读人员"}
        disabled={disabled}
        onClick={() => onChange(multiple ? [...value, "u2"] : ["u2"])}
      >
        {value.join(",")}
      </button>
    ),
  });
  render(
    <ApprovalProvider client={supplied}>
      <DynamicForm
        schema={{
          fields: [
            { key: "owner", label: "所有者", type: "User" },
            { key: "people", label: "参与者", type: "Users" },
          ],
        }}
        value={{ owner: "u1", people: ["u1"] }}
        readonly
        editableFields={["PEOPLE"]}
        onChange={changed}
      />
    </ApprovalProvider>,
  );
  expect(screen.getByRole("button", { name: "只读人员" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "参与者" }));
  expect(changed).toHaveBeenLastCalledWith({
    owner: "u1",
    people: ["u1", "u2"],
  });
});
it("discards an upload that finishes after unmount and performs no automatic retry after a storage failure", async () => {
  let finish!: (response: Response) => void;
  const changed = vi.fn();
  const fetcher = vi.fn((url: string) =>
    url.endsWith("/files")
      ? new Promise<Response>((resolve) => {
          finish = resolve;
        })
      : Promise.resolve(new Response("{}")),
  );
  const client = createApprovalClient({ fetcher });
  const view = render(
    <ApprovalProvider client={client}>
      <DynamicForm
        schema={{
          fields: [
            { key: "files", label: "附件", type: "File", accept: ["txt"] },
          ],
        }}
        value={{ files: [] }}
        onChange={changed}
      />
    </ApprovalProvider>,
  );
  await act(async () => {
    fireEvent.change(document.querySelector("input[type=file]")!, {
      target: { files: [new File(["x"], "file.txt")] },
    });
  });
  view.unmount();
  await act(async () => {
    finish(
      new Response(
        JSON.stringify({
          id: "old",
          name: "file.txt",
          size: 1,
          contentType: "text/plain",
        }),
      ),
    );
  });
  expect(changed).not.toHaveBeenCalled();
  expect(
    fetcher.mock.calls.filter(([url]) => url.endsWith("/files")),
  ).toHaveLength(1);
  const failed = vi.fn(async (url: string) =>
    url.endsWith("/files")
      ? new Response("storage down", { status: 503 })
      : new Response("{}"),
  );
  show(
    { key: "files", label: "附件", type: "File", accept: ["txt"] },
    [],
    failed,
  );
  await act(async () => {
    fireEvent.change(document.querySelector("input[type=file]")!, {
      target: { files: [new File(["x"], "file.txt")] },
    });
  });
  expect(screen.getByRole("alert")).toHaveTextContent("请求失败（HTTP 503）");
  expect(
    failed.mock.calls.filter(([url]) => url.endsWith("/files")),
  ).toHaveLength(1);
});
it("uploads File data as authenticated binary and retains only server metadata in the form", async () => {
  const fetcher = vi.fn(async (url: string) =>
    url.endsWith("/files")
      ? new Response(
          JSON.stringify({
            id: "f1",
            name: "hello.txt",
            size: 5,
            contentType: "text/plain",
          }),
        )
      : new Response("{}"),
  );
  const form = show(
    { key: "files", label: "附件", type: "File", accept: ["txt"] },
    [],
    fetcher,
  );
  const input = document.querySelector("input[type=file]")!;
  await act(async () => {
    fireEvent.change(input, {
      target: {
        files: [new File(["hello"], "hello.txt", { type: "text/plain" })],
      },
    });
  });
  expect(form.changed).toHaveBeenLastCalledWith({
    files: [
      { id: "f1", name: "hello.txt", size: 5, contentType: "text/plain" },
    ],
  });
  expect(
    fetcher.mock.calls.filter(([url]) => url.endsWith("/files")),
  ).toHaveLength(1);
  expect(screen.getByRole("button", { name: "hello.txt" })).toBeInTheDocument();
});
it("rejects a disallowed File extension before invoking storage", async () => {
  const fetcher = vi.fn(async (_url: string) => new Response("{}"));
  show(
    { key: "files", label: "附件", type: "File", accept: ["txt"] },
    [],
    fetcher,
  );
  await act(async () => {
    fireEvent.change(document.querySelector("input[type=file]")!, {
      target: { files: [new File(["x"], "file.exe")] },
    });
  });
  expect(screen.getByRole("alert")).toHaveTextContent("文件类型不允许");
  expect(
    fetcher.mock.calls.some((call) => String(call[0]).endsWith("/files")),
  ).toBe(false);
});
it("renders Description as escaped text and never writes a data value", () => {
  const form = show(
    {
      key: "help",
      label: "说明",
      type: "Description",
      help: "<script>danger</script>",
    },
    undefined,
  );
  expect(screen.getByText("<script>danger</script>")).toBeInTheDocument();
  expect(document.querySelector("script")).toBeNull();
  expect(form.changed).not.toHaveBeenCalled();
});
