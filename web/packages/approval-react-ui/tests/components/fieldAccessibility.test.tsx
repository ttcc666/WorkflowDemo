import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import {
  Field,
  Input,
  Select,
  Textarea,
} from "../../src/components/ui/primitives";
import { DateRangePicker } from "../../src/components/ui/extended";
import { createRef } from "react";
import { act } from "@testing-library/react";
import {
  DynamicForm,
  type DynamicFormHandle,
} from "../../src/features/forms/DynamicForm";

it("field labels focus the associated control and expose required and error descriptions", async () => {
  render(
    <Field
      label="产品编码"
      required
      error="请填写产品编码"
      help="按设备清单填写"
    >
      <Input />
    </Field>,
  );
  const input = screen.getByRole("textbox", {
    name: /产品编码/,
  }) as HTMLInputElement;
  await userEvent.setup().click(screen.getByText("产品编码", { exact: true }));
  expect(input).toHaveFocus();
  expect(input).toHaveAttribute("aria-required", "true");
  expect(input).toHaveAttribute("aria-invalid", "true");
  expect(input.labels).toHaveLength(1);
  const descriptions = input
    .getAttribute("aria-describedby")!
    .split(" ")
    .map((id) => document.getElementById(id)?.textContent);
  expect(descriptions).toEqual(["按设备清单填写", "请填写产品编码"]);
});

it("independent and explicitly named controls retain unique ids and their values", () => {
  const changed = vi.fn();
  render(
    <>
      <Field label="左侧" required>
        <Input value="Alice" onChange={changed} />
        <Select
          aria-label="级别"
          value="a"
          options={[{ value: "a", label: "一级" }]}
        />
      </Field>
      <Field label="右侧">
        <Textarea aria-label="说明" value="备注" onChange={changed} />
      </Field>
    </>,
  );
  const controls = [
    screen.getByRole("textbox", { name: /左侧/ }),
    screen.getByRole("combobox", { name: "级别" }),
    screen.getByRole("textbox", { name: "说明" }),
  ];
  expect(new Set(controls.map((control) => control.id)).size).toBe(3);
  expect(controls[0]).toHaveValue("Alice");
  expect(controls[1]).toHaveAttribute("data-value", "a");
  expect(controls[2]).toHaveValue("备注");
  expect(controls[2]).not.toHaveAttribute("aria-required");
  expect(changed).not.toHaveBeenCalled();
});

it("date range field groups keep both controls independently named and required", () => {
  const changed = vi.fn();
  render(
    <Field label="申请日期范围" group required>
      <DateRangePicker
        label="日期"
        value={["2026-11-08", "2026-11-09"]}
        onChange={changed}
      />
    </Field>,
  );
  const start = screen.getByRole("textbox", { name: "日期开始" });
  const end = screen.getByRole("textbox", { name: "日期结束" });
  expect(start.id).not.toBe(end.id);
  expect(start).toHaveAttribute("aria-required", "true");
  expect(end).toHaveAttribute("aria-required", "true");
  fireEvent.change(end, { target: { value: "2026-11-10" } });
  expect(changed).toHaveBeenLastCalledWith(["2026-11-08", "2026-11-10"]);
});

it("table cells expose their own required flags and validation description without inheriting the table requirement", () => {
  const form = createRef<DynamicFormHandle>();
  render(
    <DynamicForm
      ref={form}
      schema={{
        fields: [
          {
            key: "items",
            label: "明细",
            type: "Table",
            required: true,
            columns: [
              { key: "product", label: "项目", type: "Text", required: true },
              { key: "quantity", label: "数量", type: "Number" },
            ],
          },
        ],
      }}
      value={{ items: [{ product: "", quantity: 2 }] }}
      onChange={() => {}}
    />,
  );
  act(() => {
    expect(form.current!.validate()).toBe(false);
  });
  const required = screen.getByRole("textbox", { name: "项目" });
  const optional = screen.getByRole("spinbutton", { name: "数量" });
  expect(required).toHaveAttribute("aria-required", "true");
  expect(required).toHaveAttribute("aria-invalid", "true");
  expect(
    document.getElementById(required.getAttribute("aria-describedby")!),
  ).toHaveTextContent("请填写项目");
  expect(optional).not.toHaveAttribute("aria-required");
  expect(optional).not.toHaveAttribute("aria-describedby");
  expect(optional).toHaveValue(2);
});
