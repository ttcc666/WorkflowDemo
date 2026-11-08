import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import userEvent from "@testing-library/user-event";
import type { FormField, TimeoutPolicy } from "@approval/shared";
import { TimeoutEditor } from "../../../src/features/designer/RuleEditors";
import { FieldInspector } from "../../../src/features/forms/FormDesigner";

it("converts timeout and repeated reminders to integer minutes while preserving maximum reminders", async () => {
  const changed = vi.fn();
  function Editor() {
    const [value, setValue] = useState<TimeoutPolicy | null>(null);
    return (
      <TimeoutEditor
        value={value}
        onChange={(next) => {
          changed(next);
          setValue(next);
        }}
      />
    );
  }
  render(<Editor />);
  const user = userEvent.setup();
  fireEvent.click(screen.getByRole("checkbox", { name: "启用时限" }));
  expect(changed).toHaveBeenLastCalledWith({
    minutes: 1440,
    action: "Remind",
    repeatMinutes: null,
    maxReminders: 3,
  });
  await user.click(screen.getByRole("combobox", { name: "时限单位" }));
  await user.click(screen.getByRole("option", { name: "小时" }));
  expect(changed).toHaveBeenCalledTimes(1);
  expect(screen.getByRole("spinbutton", { name: "时限数值" })).toHaveValue(24);
  fireEvent.change(screen.getByRole("spinbutton", { name: "时限数值" }), {
    target: { value: "1.5" },
  });
  fireEvent.click(screen.getByRole("checkbox", { name: "仍未处理时重复催办" }));
  await user.click(screen.getByRole("combobox", { name: "催办单位" }));
  await user.click(screen.getByRole("option", { name: "分钟" }));
  fireEvent.change(screen.getByRole("spinbutton", { name: "催办数值" }), {
    target: { value: "45" },
  });
  fireEvent.change(screen.getByRole("spinbutton", { name: "最多提醒次数" }), {
    target: { value: "5" },
  });
  expect(changed).toHaveBeenLastCalledWith({
    minutes: 90,
    action: "Remind",
    repeatMinutes: 45,
    maxReminders: 5,
  });
  await user.click(screen.getByRole("combobox", { name: "超时处理" }));
  const rejectOption = screen
    .getAllByRole("option")
    .find(
      (option) => option.getAttribute("data-option-value") === "AutoReject",
    );
  expect(rejectOption).toBeInTheDocument();
  await user.click(rejectOption!);
  expect(changed).toHaveBeenLastCalledWith({
    minutes: 90,
    action: "AutoReject",
    repeatMinutes: 45,
    maxReminders: 5,
  });
  fireEvent.click(screen.getByRole("checkbox", { name: "启用时限" }));
  expect(changed).toHaveBeenLastCalledWith(null);
});

it("edits formula precision without losing the expression or allowing self-reference suggestions", () => {
  const changed = vi.fn();
  function Editor() {
    const [field, setField] = useState<FormField>({
      key: "total",
      label: "总额",
      type: "Formula",
      formula: "amount / 3",
      precision: 2,
    });
    return (
      <FieldInspector
        field={field}
        otherKeys={["amount"]}
        variables={[
          { name: "total", label: "总额" },
          { name: "amount", label: "金额" },
        ]}
        update={(patch) => {
          const next = { ...field, ...patch };
          changed(next);
          setField(next);
        }}
      />
    );
  }
  render(<Editor />);
  expect(
    screen.queryByRole("button", { name: "total" }),
  ).not.toBeInTheDocument();
  fireEvent.change(screen.getByRole("spinbutton", { name: "计算精度" }), {
    target: { value: "3" },
  });
  expect(changed).toHaveBeenLastCalledWith({
    key: "total",
    label: "总额",
    type: "Formula",
    formula: "amount / 3",
    precision: 3,
  });
  expect(screen.getByRole("textbox", { name: "公式表达式" })).toHaveValue(
    "amount / 3",
  );
  fireEvent.change(screen.getByRole("textbox", { name: "字段标识" }), {
    target: { value: "AMOUNT" },
  });
  expect(screen.getByRole("alert")).toHaveTextContent("字段标识重复");
});
