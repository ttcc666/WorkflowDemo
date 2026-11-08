import { createRef } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import {
  fieldTypes,
  initialValues,
  newField,
  type FormSchema,
} from "@approval/shared";
import { ApprovalProvider, createApprovalClient } from "../../../src/client";
import {
  DynamicForm,
  validateForm,
  type DynamicFormHandle,
} from "../../../src/features/forms/DynamicForm";
import { FormDesigner } from "../../../src/features/forms/FormDesigner";

const client = () =>
  createApprovalClient({ fetcher: async () => new Response("[]") });
it("renders all sixteen schema field types without changing input values", () => {
  const schema: FormSchema = {
    fields: fieldTypes.map((item) => newField(item.type, [])),
  };
  schema.fields.find((field) => field.type === "Formula")!.formula = "1 + 2";
  const values = initialValues(schema);
  const onChange = vi.fn();
  render(
    <ApprovalProvider client={client()}>
      <DynamicForm schema={schema} value={values} onChange={onChange} />
    </ApprovalProvider>,
  );
  expect(screen.getByRole("textbox", { name: "单行文本" })).toHaveValue("");
  expect(screen.getByRole("textbox", { name: "多行文本" })).toHaveValue("");
  expect(screen.getByRole("switch", { name: "开关" })).toHaveAttribute(
    "aria-checked",
    "false",
  );
  expect(screen.getByRole("button", { name: "上传附件" })).toBeEnabled();
  expect(screen.getByRole("button", { name: "添加一行" })).toBeEnabled();
  expect(screen.getByText("在这里填写说明内容")).toBeInTheDocument();
  expect(onChange.mock.calls.at(-1)?.[0].formula1).toBe(3);
  expect(values.formula1).toBeNull();
});
it("validates nested detail errors switch required precision date order and user limits", () => {
  const schema: FormSchema = {
    fields: [
      { key: "accepted", label: "承诺", type: "Switch", required: true },
      {
        key: "money",
        label: "金额",
        type: "Money",
        precision: 2,
        min: 0,
        max: 10,
      },
      { key: "dates", label: "日期", type: "DateRange" },
      { key: "users", label: "人员", type: "Users" },
      {
        key: "items",
        label: "明细",
        type: "Table",
        columns: [
          { key: "amount", label: "行金额", type: "Money", required: true },
        ],
      },
    ],
  };
  const errors = validateForm(schema, {
    accepted: false,
    money: 1.234,
    dates: ["2026-11-07", "2026-11-06"],
    users: Array.from({ length: 101 }, (_, index) => String(index)),
    items: [{}],
  });
  expect(errors).toEqual({
    accepted: ["请勾选承诺"],
    money: ["金额最多 2 位小数"],
    dates: ["日期的开始日期不能晚于结束日期"],
    users: ["人员最多选择 100 人"],
    "items[0].amount": ["请填写行金额"],
  });
  expect(
    validateForm(schema, { accepted: false, money: 100 }, ["MONEY"]),
  ).toEqual({ money: ["金额不能大于 10"] });
});
it("permits only editable fields in readonly mode and reports server errors without replacing values", () => {
  const schema: FormSchema = {
    fields: [
      { key: "amount", label: "金额", type: "Money", required: true },
      { key: "reason", label: "原因", type: "Text" },
    ],
  };
  const handle = createRef<DynamicFormHandle>();
  const change = vi.fn();
  render(
    <ApprovalProvider client={client()}>
      <DynamicForm
        ref={handle}
        schema={schema}
        value={{ amount: 2, reason: "只读内容" }}
        readonly
        editableFields={["AMOUNT"]}
        errors={{ amount: ["远程校验错误"] }}
        onChange={change}
      />
    </ApprovalProvider>,
  );
  fireEvent.change(screen.getByRole("spinbutton", { name: "金额" }), {
    target: { value: "3.5" },
  });
  expect(change).toHaveBeenLastCalledWith({ amount: 3.5, reason: "只读内容" });
  expect(
    screen.queryByRole("textbox", { name: "原因" }),
  ).not.toBeInTheDocument();
  expect(screen.getByText("远程校验错误")).toBeInTheDocument();
  expect(handle.current!.validate()).toBe(true);
});
it("adds duplicates and deletes fields while preserving existing schema entries", () => {
  const change = vi.fn();
  const schema: FormSchema = {
    fields: [{ key: "amount", label: "金额", type: "Money", precision: 2 }],
  };
  render(
    <ApprovalProvider client={client()}>
      <FormDesigner value={schema} onChange={change} />
    </ApprovalProvider>,
  );
  fireEvent.click(screen.getByRole("button", { name: "复制字段" }));
  expect(change.mock.calls.at(-1)?.[0].fields).toEqual([
    { key: "amount", label: "金额", type: "Money", precision: 2 },
    { key: "money1", label: "金额（副本）", type: "Money", precision: 2 },
  ]);
  fireEvent.click(screen.getByRole("button", { name: "删除字段" }));
  expect(change.mock.calls.at(-1)?.[0].fields).toEqual([]);
});
