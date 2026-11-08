import { useState } from "react";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { Select } from "../../src/components/ui/primitives";
import {
  DatePicker,
  DateRangePicker,
  SuggestionInput,
} from "../../src/components/ui/extended";
import { Metrics } from "../../src/components/ui/Metrics";
import { ActionMappingRuleEditor } from "../../src/features/designer/ActionMappingEditor";

it("Radix select supports keyboard values clear and independent controlled instances", async () => {
  const changed = vi.fn();
  function Example() {
    const [value, setValue] = useState("a");
    return (
      <>
        <Select
          aria-label="左侧"
          value={value}
          onValueChange={(next) => {
            changed(next);
            setValue(next);
          }}
          options={[
            { value: "a", label: "Alpha" },
            { value: "value:stock", label: "Stock" },
          ]}
        />
        <Select
          aria-label="右侧"
          value="a"
          options={[{ value: "a", label: "Alpha" }]}
        />
      </>
    );
  }
  render(<Example />);
  const user = userEvent.setup();
  const left = screen.getByRole("combobox", { name: "左侧" });
  expect(left.tagName).toBe("BUTTON");
  expect(left).toHaveAttribute("data-slot", "select-trigger");
  left.focus();
  await user.keyboard("{ArrowDown}{End}{Enter}");
  await waitFor(() =>
    expect(left).toHaveAttribute("data-value", "value:stock"),
  );
  expect(changed).toHaveBeenLastCalledWith("value:stock");
  expect(left).toHaveTextContent("Stock");
  await user.click(left);
  await user.click(screen.getByRole("option", { name: "请选择" }));
  expect(changed).toHaveBeenLastCalledWith("");
  expect(left).toHaveAttribute("data-value", "");
  expect(screen.getByRole("combobox", { name: "右侧" })).toHaveAttribute(
    "data-value",
    "a",
  );
});

it("Radix select closes an open popup when its command becomes disabled", async () => {
  const changed = vi.fn();
  const props = {
    value: "a",
    onValueChange: changed,
    options: [
      { value: "a", label: "Alpha" },
      { value: "b", label: "Beta" },
    ],
    "aria-label": "选择",
  };
  const view = render(<Select {...props} />);
  const user = userEvent.setup();
  await user.click(screen.getByRole("combobox", { name: "选择" }));
  expect(screen.getByRole("listbox")).toBeInTheDocument();
  view.rerender(<Select {...props} disabled />);
  await waitFor(() =>
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument(),
  );
  expect(screen.getByRole("combobox", { name: "选择" })).toBeDisabled();
  expect(changed).not.toHaveBeenCalled();
});

it("DatePicker uses a calendar popup and preserves canonical days and min max boundaries", async () => {
  const changed = vi.fn();
  function Example() {
    const [value, setValue] = useState("2026-11-06");
    return (
      <DatePicker
        label="日期"
        value={value}
        min="2026-11-05"
        max="2026-11-10"
        onChange={(next) => {
          changed(next);
          setValue(next);
        }}
      />
    );
  }
  render(<Example />);
  const user = userEvent.setup();
  expect(screen.getByRole("textbox", { name: "日期" })).not.toHaveAttribute(
    "type",
    "date",
  );
  await user.click(screen.getByRole("button", { name: "选择日期" }));
  expect(screen.getByRole("button", { name: "2026-11-04" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "2026-11-11" })).toBeDisabled();
  await user.click(screen.getByRole("button", { name: "2026-11-10" }));
  expect(changed).toHaveBeenLastCalledWith("2026-11-10");
  expect(screen.getByRole("textbox", { name: "日期" })).toHaveValue(
    "2026-11-10",
  );
  await user.click(screen.getByRole("button", { name: "选择日期" }));
  await user.click(screen.getByRole("button", { name: "清空日期" }));
  expect(changed).toHaveBeenLastCalledWith("");
  expect(screen.getByRole("textbox", { name: "日期" })).toHaveValue("");
});

it("DatePicker reports invalid or outside dates and closes its popup when disabled", async () => {
  const changed = vi.fn();
  const props = {
    label: "日期",
    value: "2026-02-30",
    min: "2026-11-05",
    max: "2026-11-10",
    onChange: changed,
  };
  const view = render(<DatePicker {...props} />);
  const input = screen.getByRole("textbox", { name: "日期" });
  expect(input).toHaveAttribute("aria-invalid", "true");
  expect(
    document.getElementById(input.getAttribute("aria-describedby")!),
  ).toHaveTextContent("请输入有效日期");
  view.rerender(<DatePicker {...props} value="2026-11-04" />);
  expect(input).toHaveAttribute("aria-invalid", "true");
  view.rerender(<DatePicker {...props} value="2026-11-11" />);
  expect(input).toHaveAttribute("aria-invalid", "true");
  view.rerender(<DatePicker {...props} value="2026-11-05" />);
  expect(input).not.toHaveAttribute("aria-invalid");
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "选择日期" }));
  view.rerender(<DatePicker {...props} value="2026-11-05" disabled />);
  await waitFor(() =>
    expect(
      screen.queryByRole("button", { name: "2026-11-05" }),
    ).not.toBeInTheDocument(),
  );
  expect(input).toBeDisabled();
  expect(changed).not.toHaveBeenCalled();
});

it("SuggestionInput searches framework options and preserves custom host values", async () => {
  const changed = vi.fn();
  function Example() {
    const [value, setValue] = useState("");
    return (
      <SuggestionInput
        aria-label="角色"
        value={value}
        suggestions={["Finance", "HR"]}
        onValueChange={(next) => {
          changed(next);
          setValue(next);
        }}
      />
    );
  }
  render(<Example />);
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "角色选项" }));
  await user.type(
    screen.getByRole("combobox", { name: "角色搜索" }),
    "Finance",
  );
  expect(screen.queryByRole("option", { name: "HR" })).not.toBeInTheDocument();
  await user.click(screen.getByRole("option", { name: "Finance" }));
  expect(screen.getByRole("textbox", { name: "角色" })).toHaveValue("Finance");
  fireEvent.change(screen.getByRole("textbox", { name: "角色" }), {
    target: { value: "HostCustomRole" },
  });
  expect(changed).toHaveBeenLastCalledWith("HostCustomRole");
});

it("Accordion keeps an invalid mapping buffer and error while collapsed and reopened", async () => {
  const changed = vi.fn();
  render(
    <ActionMappingRuleEditor
      value={{ source: "form.amount", target: "amount", transform: "Copy" }}
      onChange={changed}
      remove={() => {}}
    />,
  );
  const user = userEvent.setup();
  const trigger = screen.getByRole("button", { name: "默认值与转换选项" });
  expect(trigger).toHaveAttribute("aria-expanded", "false");
  await user.click(trigger);
  const input = screen.getByRole("textbox", { name: "默认值 JSON" });
  await user.type(input, "not-json");
  await user.click(trigger);
  expect(trigger).toHaveAttribute("aria-expanded", "false");
  trigger.focus();
  await user.keyboard("{Enter}");
  expect(trigger).toHaveAttribute("aria-expanded", "true");
  expect(screen.getByRole("textbox", { name: "默认值 JSON" })).toHaveValue(
    "not-json",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("请输入合法的 JSON 值");
  expect(changed).not.toHaveBeenCalled();
});

it("Metrics composes official cards and keeps zero and unknown values distinct", () => {
  const view = render(
    <Metrics
      items={[
        { label: "失败投递", value: 0 },
        { label: "等待投递", value: "—" },
      ]}
    />,
  );
  expect(screen.getAllByRole("listitem")).toHaveLength(2);
  expect(view.container.querySelectorAll('[data-slot="card"]')).toHaveLength(2);
  const [failed, pending] = screen.getAllByRole("listitem");
  expect(within(failed!).getByText("失败投递")).toBeInTheDocument();
  expect(within(failed!).getByText("0")).toBeInTheDocument();
  expect(within(pending!).getByText("等待投递")).toBeInTheDocument();
  expect(within(pending!).getByText("—")).toBeInTheDocument();
});

it("DateRangePicker changes only the selected boundary and keeps the original tuple intact", async () => {
  const original: [string, string] = ["2026-11-06", "2026-11-07"];
  const changed = vi.fn();
  function Example() {
    const [value, setValue] = useState(original);
    return (
      <DateRangePicker
        label="区间"
        value={value}
        onChange={(next) => {
          changed(next);
          setValue(next);
        }}
      />
    );
  }
  render(<Example />);
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "选择区间结束" }));
  await user.click(screen.getByRole("button", { name: "2026-11-10" }));
  expect(changed).toHaveBeenLastCalledWith(["2026-11-06", "2026-11-10"]);
  expect(screen.getByRole("textbox", { name: "区间开始" })).toHaveValue(
    "2026-11-06",
  );
  expect(screen.getByRole("textbox", { name: "区间结束" })).toHaveValue(
    "2026-11-10",
  );
  expect(original).toEqual(["2026-11-06", "2026-11-07"]);
});

it("SuggestionInput closes its options when disabled without changing the host value", async () => {
  const changed = vi.fn();
  const props = {
    "aria-label": "角色",
    value: "Custom",
    suggestions: ["Finance", "HR"],
    onValueChange: changed,
  };
  const view = render(<SuggestionInput {...props} />);
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "角色选项" }));
  expect(screen.getByRole("option", { name: "Finance" })).toBeInTheDocument();
  view.rerender(<SuggestionInput {...props} disabled />);
  await waitFor(() =>
    expect(
      screen.queryByRole("option", { name: "Finance" }),
    ).not.toBeInTheDocument(),
  );
  expect(screen.getByRole("textbox", { name: "角色" })).toBeDisabled();
  expect(screen.getByRole("textbox", { name: "角色" })).toHaveValue("Custom");
  expect(changed).not.toHaveBeenCalled();
});
