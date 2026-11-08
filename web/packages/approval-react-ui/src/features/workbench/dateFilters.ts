import { validateValue } from "@approval/shared";
import { localDayStart } from "@approval/shared/features/lists/listModel";

function dateError(label: string, value: string, addDays = 0) {
  if (value && value.trim() !== value) return `${label}应为日期（yyyy-MM-dd）`;
  const error = validateValue({ key: label, label, type: "Date" }, value);
  if (error || !value) return error;
  const boundary = localDayStart(value, addDays);
  return /^\d{4}-/.test(boundary) && !boundary.startsWith("0000-")
    ? null
    : `${label}超出可查询范围`;
}
export function validateDateFilters(from: string, to: string) {
  const fromError = dateError("开始日期", from);
  const toError =
    dateError("结束日期", to, 1) ||
    (!fromError && from && to && from > to ? "结束日期不能早于开始日期" : null);
  return { fromError, toError };
}
