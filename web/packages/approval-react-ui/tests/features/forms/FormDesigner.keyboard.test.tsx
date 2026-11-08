import { useState } from "react";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import type { FormSchema } from "@approval/shared";
import { ApprovalProvider, createApprovalClient } from "../../../src/client";
import { FormDesigner } from "../../../src/features/forms/FormDesigner";
const initial: FormSchema = {
  fields: [
    { key: "a", label: "甲", type: "Text" },
    {
      key: "b",
      label: "乙",
      type: "Text",
      required: true,
      maxLength: 31,
      defaultValue: "原值",
    },
    { key: "c", label: "丙", type: "Text" },
  ],
};
function show() {
  const changed = vi.fn();
  let current = initial;
  function Designer() {
    const [value, setValue] = useState(initial);
    current = value;
    return (
      <FormDesigner
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
      client={createApprovalClient({ fetcher: async () => new Response("[]") })}
    >
      <Designer />
    </ApprovalProvider>,
  );
  const card = (label: string) =>
    screen.getByRole("group", { name: new RegExp(`^字段：${label}（`) });
  return {
    changed,
    card,
    keys: () => current.fields.map((field) => field.key),
    value: () => current,
  };
}
it("field cards use one tab stop and keyboard selection never mutates the schema", async () => {
  const editor = show();
  const user = userEvent.setup();
  expect(editor.card("甲")).toHaveAttribute("tabindex", "0");
  expect(editor.card("乙")).toHaveAttribute("tabindex", "-1");
  act(() => editor.card("甲").focus());
  await user.keyboard("{ArrowDown}");
  expect(editor.card("乙")).toHaveFocus();
  expect(screen.getByRole("textbox", { name: "字段标识" })).toHaveValue("b");
  await user.keyboard("{End}");
  expect(editor.card("丙")).toHaveFocus();
  await user.keyboard("{Home}{Enter} ");
  expect(editor.card("甲")).toHaveFocus();
  expect(editor.card("甲")).toHaveAttribute("aria-current", "true");
  expect(editor.changed).not.toHaveBeenCalled();
});

it("keyboard and button reordering preserve field values and focus and reject boundary moves", async () => {
  const editor = show();
  const user = userEvent.setup();
  act(() => editor.card("甲").focus());
  await user.keyboard("{Alt>}{ArrowDown}{/Alt}");
  expect(editor.keys()).toEqual(["b", "a", "c"]);
  expect(editor.card("甲")).toHaveFocus();
  await user.click(
    within(editor.card("甲")).getByRole("button", { name: "上移字段" }),
  );
  expect(editor.keys()).toEqual(["a", "b", "c"]);
  expect(editor.card("甲")).toHaveFocus();
  await user.keyboard("{Alt>}{ArrowUp}{/Alt}{End}{Alt>}{ArrowDown}{/Alt}");
  expect(editor.changed).toHaveBeenCalledTimes(2);
  expect(editor.value().fields[1]).toEqual(initial.fields[1]);
});

it("keyboard copy and delete preserve options and unique keys and restore an actionable focus", async () => {
  const editor = show();
  const user = userEvent.setup();
  act(() => editor.card("乙").focus());
  await user.keyboard("{Control>}d{/Control}");
  const copy = editor.value().fields[2]!;
  expect(copy).toMatchObject({
    label: "乙（副本）",
    type: "Text",
    required: true,
    maxLength: 31,
    defaultValue: "原值",
  });
  expect(initial.fields.some((field) => field.key === copy.key)).toBe(false);
  expect(editor.value().fields).toHaveLength(4);
  expect(editor.card("乙（副本）")).toHaveFocus();
  await user.keyboard("{Delete}");
  expect(editor.keys()).toEqual(["a", "b", "c"]);
  expect(editor.card("丙")).toHaveFocus();
  await user.keyboard("{Delete}{Delete}{Delete}");
  expect(editor.keys()).toEqual([]);
  expect(screen.getByRole("button", { name: "单行文本" })).toHaveFocus();
});

it("keys inside field action buttons do not invoke card deletion or duplicate handlers", () => {
  const editor = show();
  const copy = within(editor.card("甲")).getByRole("button", {
    name: "复制字段",
  });
  fireEvent.keyDown(copy, { key: "Delete" });
  fireEvent.keyDown(copy, { key: "d", ctrlKey: true });
  expect(editor.keys()).toEqual(["a", "b", "c"]);
  expect(editor.changed).not.toHaveBeenCalled();
});
