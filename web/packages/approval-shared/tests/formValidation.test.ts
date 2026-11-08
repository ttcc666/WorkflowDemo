import { expect, it } from "vitest";
import { validateValue } from "../src/features/forms/formValidation";
const date = { key: "date", label: "日期", type: "Date" } as const;
const range = { key: "period", label: "期间", type: "DateRange" } as const;
it.each([
  { name: "leap", value: "2024-02-29" },
  { name: "minimum", value: "0001-01-01" },
  { name: "maximum", value: "9999-12-31" },
  { name: "empty", value: null },
])(
  "canonical date accepts $name according to server semantics",
  ({ value }) => {
    expect(validateValue(date, value)).toBeNull();
  },
);
it.each([
  { name: "non-leap", value: "2026-02-29" },
  { name: "overflow", value: "2026-02-30" },
  { name: "year-zero", value: "0000-01-01" },
  { name: "partial", value: "2026-" },
  { name: "short-month", value: "2026-1-01" },
  { name: "timestamp", value: "2026-01-01T00:00:00Z" },
  { name: "wrong-kind", value: 123 },
])("canonical date rejects $name before submission", ({ value }) => {
  expect(validateValue(date, value)).toBe("日期应为日期（yyyy-MM-dd）");
});
it("date ranges require two canonical endpoints preserve ordering and allow optional emptiness", () => {
  expect(validateValue(range, ["2026-11-06", "2026-11-06"])).toBeNull();
  expect(validateValue(range, ["2026-11-07", "2026-11-06"])).toBe(
    "期间的开始日期不能晚于结束日期",
  );
  for (const invalid of [
    ["2026-11-06"],
    ["2026-11-06", ""],
    ["2026-02-30", "2026-11-06"],
    123,
  ])
    expect(validateValue(range, invalid)).toBe("期间应为开始和结束日期");
  expect(validateValue(range, [])).toBeNull();
  expect(validateValue({ ...range, required: true }, [])).toBe("请选择期间");
});
