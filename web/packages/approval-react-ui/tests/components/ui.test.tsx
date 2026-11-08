import { useState } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import {
  Button,
  Dialog,
  JsonEditor,
  ConfirmDialog,
  Field,
  Input,
  Select,
} from "../../src/components/ui/primitives";
import { Calendar, Command } from "../../src/components/ui/extended";

it("exposes field validation and descriptions to controls without changing their values", () => {
  const changed = vi.fn();
  render(
    <Field label="审批字段" error="内容无效" help="填写提示">
      <Input aria-label="姓名" value="Alice" onChange={changed} />
      <Select
        aria-label="级别"
        value="a"
        options={[{ value: "a", label: "一级" }]}
        onValueChange={changed}
      />
    </Field>,
  );
  const input = screen.getByRole("textbox", { name: "姓名" });
  const select = screen.getByRole("combobox", { name: "级别" });
  expect(input).toHaveValue("Alice");
  expect(select).toHaveAttribute("data-value", "a");
  expect(input).toHaveAttribute("aria-invalid", "true");
  expect(select).toHaveAttribute("aria-invalid", "true");
  expect(screen.getByRole("group", { name: "审批字段" })).toHaveAttribute(
    "data-invalid",
    "true",
  );
  const ids = input.getAttribute("aria-describedby")!.split(" ");
  expect(ids.map((id) => document.getElementById(id)?.textContent)).toEqual([
    "填写提示",
    "内容无效",
  ]);
  expect(changed).not.toHaveBeenCalled();
});

it("keeps a controlled confirmation open after failure and restores trigger focus when canceled", async () => {
  let fail!: () => void;
  const command = vi.fn(
    () =>
      new Promise<void>((_, reject) => {
        fail = () => reject(new Error("失败"));
      }),
  );
  function Example() {
    const [open, setOpen] = useState(false);
    const [busy, setBusy] = useState(false);
    return (
      <>
        <Button onClick={() => setOpen(true)}>执行命令</Button>
        <ConfirmDialog
          title="命令确认"
          open={open}
          onOpenChange={setOpen}
          busy={busy}
          confirm={() => {
            setBusy(true);
            void command().catch(() => setBusy(false));
          }}
        >
          确认执行？
        </ConfirmDialog>
      </>
    );
  }
  render(<Example />);
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "执行命令" }));
  await user.click(screen.getByRole("button", { name: "确认" }));
  expect(
    screen.getByRole("alertdialog", { name: "命令确认" }),
  ).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "确认" })).toBeDisabled();
  fail();
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "确认" })).toBeEnabled(),
  );
  expect(command).toHaveBeenCalledOnce();
  expect(
    screen.getByRole("alertdialog", { name: "命令确认" }),
  ).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "取消" }));
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "执行命令" })).toHaveFocus(),
  );
});

it("keeps Radix select clear and disabled behavior through the official control", async () => {
  const changed = vi.fn();
  const view = render(
    <Select
      aria-label="状态"
      value="a"
      options={[{ value: "a", label: "一级" }]}
      onValueChange={changed}
    />,
  );
  const user = userEvent.setup();
  await user.click(screen.getByRole("combobox", { name: "状态" }));
  await user.click(screen.getByRole("option", { name: "请选择" }));
  expect(changed).toHaveBeenLastCalledWith("");
  view.rerender(
    <Select
      aria-label="状态"
      value="a"
      disabled
      size="sm"
      options={[{ value: "a", label: "一级" }]}
      onValueChange={changed}
    />,
  );
  const select = screen.getByLabelText("状态");
  expect(select).toBeDisabled();
  expect(select).toHaveAttribute("data-value", "a");
  expect(select).toHaveAttribute("data-size", "sm");
  await user.click(select);
  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  expect(changed).toHaveBeenCalledOnce();
});

it("Dialog receives focus traps keyboard navigation and closes on Escape", async () => {
  function Example() {
    const [open, setOpen] = useState(false);
    return (
      <>
        <Button onClick={() => setOpen(true)}>打开窗口</Button>
        <Dialog open={open} onOpenChange={setOpen} title="键盘窗口">
          <input aria-label="窗口输入" />
        </Dialog>
      </>
    );
  }
  render(<Example />);
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "打开窗口" }));
  expect(screen.getByRole("dialog", { name: "键盘窗口" })).toBeInTheDocument();
  await user.keyboard("{Escape}");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "打开窗口" })).toHaveFocus(),
  );
});
it("JsonEditor rejects malformed or wrong-type JSON without mutating its model", () => {
  const change = vi.fn();
  render(<JsonEditor value={{ amount: 1 }} onChange={change} label="配置" />);
  fireEvent.change(screen.getByRole("textbox", { name: "配置" }), {
    target: { value: "[1,2]" },
  });
  expect(change).not.toHaveBeenCalled();
  expect(screen.getByRole("alert")).toHaveTextContent("必须是 JSON 对象");
  fireEvent.change(screen.getByRole("textbox", { name: "配置" }), {
    target: { value: '{"amount":2}' },
  });
  expect(change).toHaveBeenLastCalledWith({ amount: 2 });
});
it("Command supports keyboard selection and returns the selected value", () => {
  const select = vi.fn();
  render(
    <Command
      label="查找"
      options={[
        { value: "a", label: "Alpha" },
        { value: "b", label: "Beta" },
      ]}
      onSelect={select}
    />,
  );
  const input = screen.getByRole("combobox", { name: "查找" });
  fireEvent.keyDown(input, { key: "ArrowDown" });
  fireEvent.keyDown(input, { key: "Enter" });
  expect(select).toHaveBeenCalledWith("b");
});
it("Calendar navigates months and keeps date boundaries disabled", () => {
  const changed = vi.fn();
  render(
    <Calendar
      value="2026-11-06"
      min="2026-11-05"
      max="2026-12-10"
      onChange={changed}
    />,
  );
  expect(screen.getByRole("button", { name: "2026-11-04" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "下个月" }));
  fireEvent.click(screen.getByRole("button", { name: "2026-12-10" }));
  expect(changed).toHaveBeenLastCalledWith("2026-12-10");
  expect(screen.getByRole("button", { name: "2026-12-11" })).toBeDisabled();
});
