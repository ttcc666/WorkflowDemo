import { useState } from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import type { FormSchema } from "@approval/shared";
import { FormDesigner } from "../../../src/features/forms/FormDesigner";

afterEach(() => vi.restoreAllMocks());
it("mobile field selection opens an inspector and edits survive closing and reopening it", async () => {
  vi.spyOn(window, "matchMedia").mockReturnValue({
    matches: true,
    media: "(max-width: 700px)",
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent() {
      return false;
    },
  });
  function Example() {
    const [schema, setSchema] = useState<FormSchema>({
      fields: [{ key: "product", label: "产品编码", type: "Text" }],
    });
    return <FormDesigner value={schema} onChange={setSchema} />;
  }
  render(<Example />);
  await screen.findByRole("button", { name: "编辑字段属性" });
  const user = userEvent.setup();
  const card = screen.getByRole("group", { name: "字段：产品编码（product）" });
  await user.click(card);
  const dialog = await screen.findByRole("dialog", { name: "字段属性" });
  const name = within(dialog).getByRole("textbox", { name: "字段名称" });
  await user.clear(name);
  await user.type(name, "设备编码");
  await user.click(within(dialog).getByRole("button", { name: "关闭抽屉" }));
  await waitFor(() =>
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
  );
  expect(
    screen.getByRole("group", { name: "字段：设备编码（product）" }),
  ).toHaveFocus();
  await user.click(screen.getByRole("button", { name: "编辑字段属性" }));
  expect(
    within(screen.getByRole("dialog", { name: "字段属性" })).getByRole(
      "textbox",
      { name: "字段名称" },
    ),
  ).toHaveValue("设备编码");
});
