import { createRef, useState } from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { ApprovalProvider, createApprovalClient } from "../../../src/client";
import {
  DatePicker,
  DateRangePicker,
} from "../../../src/components/ui/extended";
import {
  DynamicForm,
  type DynamicFormHandle,
} from "../../../src/features/forms/DynamicForm";
import type { FormValues } from "@approval/shared";

it("date editing updates its model immediately and shows a format error only after blur", async () => {
  const changed = vi.fn();
  function Date() {
    const [value, setValue] = useState("");
    return (
      <>
        <DatePicker
          label="日期"
          value={value}
          onChange={(next) => {
            changed(next);
            setValue(next);
          }}
        />
        <button>离开</button>
      </>
    );
  }
  render(<Date />);
  const user = userEvent.setup();
  await user.type(screen.getByRole("textbox", { name: "日期" }), "2026-");
  expect(changed).toHaveBeenLastCalledWith("2026-");
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "离开" }));
  expect(screen.getByRole("alert")).toHaveTextContent("请输入有效日期");
});

it("dynamic date input suppresses partial feedback but submission rejects invalid raw dates", async () => {
  const handle = createRef<DynamicFormHandle>();
  const changed = vi.fn();
  function Form() {
    const [value, setValue] = useState<FormValues>({ date: "" });
    return (
      <DynamicForm
        ref={handle}
        schema={{
          fields: [
            { key: "date", label: "日期", type: "Date", required: true },
          ],
        }}
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
  const user = userEvent.setup();
  const input = screen.getByRole("textbox", { name: "日期" });
  await user.type(input, "2026-");
  expect(changed).toHaveBeenLastCalledWith({ date: "2026-" });
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  let valid = true;
  act(() => {
    valid = handle.current!.validate();
  });
  expect(valid).toBe(false);
  expect(screen.getByRole("alert")).toHaveTextContent(
    "日期应为日期（yyyy-MM-dd）",
  );
  fireEvent.change(input, { target: { value: "2026-11-06" } });
  act(() => {
    valid = handle.current!.validate();
  });
  expect(valid).toBe(true);
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it("date range calendars enforce the opposite endpoint while text values remain editable", async () => {
  const changed = vi.fn();
  render(
    <DateRangePicker
      label="区间"
      value={["2026-11-06", "2026-11-10"]}
      onChange={changed}
    />,
  );
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "选择区间结束" }));
  expect(screen.getByRole("button", { name: "2026-11-05" })).toBeDisabled();
  await user.keyboard("{Escape}");
  await user.click(screen.getByRole("button", { name: "选择区间开始" }));
  expect(screen.getByRole("button", { name: "2026-11-11" })).toBeDisabled();
  await user.click(screen.getByRole("button", { name: "2026-11-10" }));
  expect(changed).toHaveBeenCalledWith(["2026-11-10", "2026-11-10"]);
});
