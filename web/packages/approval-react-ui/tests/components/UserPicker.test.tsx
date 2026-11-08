import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { ApprovalProvider } from "../../src/client";
import { UserPicker } from "../../src/components/UserPicker";
import { Field } from "../../src/components/ui/primitives";

it("picker expanded state follows search selection and Escape while closed lists are not referenced", async () => {
  const changed = vi.fn();
  const fetcher = vi.fn(
    async (input: string | URL | Request) =>
      new Response(
        JSON.stringify(
          String(input).includes("/users")
            ? [{ id: "bob", name: "Bob", isActive: true, roles: [] }]
            : { userId: "alice", roles: [], capabilities: {} },
        ),
      ),
  );
  function Example() {
    const [value, setValue] = useState<string[]>([]);
    return (
      <Field label="审批人" required>
        <UserPicker
          label="人员"
          value={value}
          onChange={(next) => {
            setValue(next);
            changed(next);
          }}
        />
      </Field>
    );
  }
  render(
    <ApprovalProvider options={{ fetcher }}>
      <Example />
    </ApprovalProvider>,
  );
  const input = screen.getByRole("combobox", { name: "人员" });
  expect(input).toHaveAttribute("aria-expanded", "false");
  expect(input).not.toHaveAttribute("aria-controls");
  expect(input).toHaveAttribute("aria-required", "true");
  const user = userEvent.setup();
  await user.click(input);
  await screen.findByRole("option", { name: /Bob/ });
  expect(input).toHaveAttribute("aria-expanded", "true");
  expect(document.getElementById(input.getAttribute("aria-controls")!)).toBe(
    screen.getByRole("listbox"),
  );
  await user.keyboard("{Escape}");
  await waitFor(() =>
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument(),
  );
  expect(input).toHaveAttribute("aria-expanded", "false");
  expect(input).not.toHaveAttribute("aria-controls");
  expect(changed).not.toHaveBeenCalled();
  await user.keyboard("{ArrowDown}");
  await screen.findByRole("option", { name: /Bob/ });
  expect(input).toHaveAttribute("aria-expanded", "true");
  await user.type(input, "bob");
  await screen.findByRole("option", { name: /Bob/ });
  expect(input).toHaveValue("bob");
  expect(
    fetcher.mock.calls.some(
      ([input]) =>
        new URL(String(input), "http://example.test").searchParams.get(
          "keyword",
        ) === "bob",
    ),
  ).toBe(true);
  await user.keyboard("{ArrowDown}{Enter}");
  await waitFor(() => expect(changed).toHaveBeenCalledWith(["bob"]));
  expect(input).toHaveAttribute("aria-expanded", "false");
  expect(input).not.toHaveAttribute("aria-controls");
  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "删除 Bob" })).toBeInTheDocument();
});
