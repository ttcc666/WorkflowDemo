import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { emptyFilters } from "@approval/shared/features/lists/listModel";
import { ListFilterBar } from "../../../src/features/workbench/ListFilterBar";

it("list filters associate labels with inputs and block incomplete dates before searching", async () => {
  const search = vi.fn();
  function Example() {
    const [filters, setFilters] = useState(emptyFilters());
    return (
      <ListFilterBar
        filters={filters}
        onChange={setFilters}
        search={search}
        reset={() => setFilters(emptyFilters())}
      />
    );
  }
  render(<Example />);
  const user = userEvent.setup();
  await user.click(screen.getByText("关键词", { exact: true }));
  expect(screen.getByRole("textbox", { name: "关键词" })).toHaveFocus();
  await user.type(screen.getByRole("textbox", { name: "开始日期" }), "2026-");
  await user.click(screen.getByRole("button", { name: "搜索" }));
  expect(search).not.toHaveBeenCalled();
  expect(screen.getByRole("textbox", { name: "开始日期" })).toHaveAttribute(
    "aria-invalid",
    "true",
  );
  await user.click(screen.getByRole("button", { name: "重置" }));
  expect(screen.getByRole("textbox", { name: "开始日期" })).toHaveValue("");
  await user.click(screen.getByRole("button", { name: "搜索" }));
  expect(search).toHaveBeenCalledOnce();
});
