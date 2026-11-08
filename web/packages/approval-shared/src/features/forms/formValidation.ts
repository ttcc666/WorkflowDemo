import type {
  FormField,
  FormFieldType,
  FormSchema,
} from "../../contracts/forms.js";
import {
  isValidFieldKey,
  numberTypes,
  optionTypes,
  textTypes,
} from "./formFields.js";
import { parseFormula } from "./formula.js";

const isEmpty = (value: unknown) =>
  value === null ||
  value === undefined ||
  (typeof value === "string" && value.trim() === "") ||
  (Array.isArray(value) && value.length === 0);

export function requiredMessage(field: FormField) {
  if (field.type === "Switch") return `请勾选${field.label}`;
  if (field.type === "Table") return `请至少添加一行${field.label}`;
  if (field.type === "File") return `请上传${field.label}`;
  const choose: FormFieldType[] = [
    "Select",
    "MultiSelect",
    "Radio",
    "User",
    "Users",
    "Date",
    "DateRange",
  ];
  return choose.includes(field.type)
    ? `请选择${field.label}`
    : `请填写${field.label}`;
}

const formatNumber = (n: number) => String(n);

function isCanonicalDay(value: unknown): value is string {
  if (
    typeof value !== "string" ||
    !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
    Number(value.slice(0, 4)) < 1
  )
    return false;
  const date = new Date(value + "T00:00:00Z");
  return (
    Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value
  );
}

/** 返回字段值的错误信息（无错误时为 null），供 Element Plus 规则与单元测试使用 */
export function validateValue(field: FormField, value: unknown): string | null {
  if (field.type === "Description" || field.type === "Formula") return null;
  if (field.type === "Switch")
    return field.required && value !== true ? requiredMessage(field) : null;
  if (isEmpty(value)) return field.required ? requiredMessage(field) : null;

  if (textTypes.has(field.type)) {
    const limit = Math.min(
      field.maxLength ?? (field.type === "Text" ? 200 : 2000),
      4000,
    );
    return String(value).trim().length > limit
      ? `${field.label}不能超过 ${limit} 个字符`
      : null;
  }
  if (numberTypes.has(field.type)) {
    const n = Number(value);
    if (!Number.isFinite(n)) return `${field.label}应为数字`;
    const precision = field.precision ?? (field.type === "Money" ? 2 : null);
    if (
      precision !== null &&
      Math.round(n * 10 ** precision) / 10 ** precision !== n
    )
      return precision === 0
        ? `${field.label}应为整数`
        : `${field.label}最多 ${precision} 位小数`;
    if (field.min !== null && field.min !== undefined && n < field.min)
      return `${field.label}不能小于 ${formatNumber(field.min)}`;
    if (field.max !== null && field.max !== undefined && n > field.max)
      return `${field.label}不能大于 ${formatNumber(field.max)}`;
    return null;
  }
  if (field.type === "Date")
    return isCanonicalDay(value)
      ? null
      : `${field.label}应为日期（yyyy-MM-dd）`;
  if (field.type === "DateRange") {
    if (
      !Array.isArray(value) ||
      value.length !== 2 ||
      !value.every(isCanonicalDay)
    )
      return `${field.label}应为开始和结束日期`;
    return value[0] > value[1]
      ? `${field.label}的开始日期不能晚于结束日期`
      : null;
  }
  if (field.type === "Table") {
    const maxRows = field.maxRows ?? 100;
    return (value as unknown[]).length > maxRows
      ? `${field.label}最多 ${maxRows} 行`
      : null;
  }
  if (field.type === "Users" && (value as unknown[]).length > 100)
    return `${field.label}最多选择 100 人`;
  if (
    field.type === "File" &&
    (value as unknown[]).length > (field.maxFiles ?? 10)
  )
    return `${field.label}最多上传 ${field.maxFiles ?? 10} 个文件`;
  return null;
}

/** 设计器本地检查：标识合法且不重复、名称、选项（其余由服务端校验） */
export function checkSchema(schema: FormSchema): string[] {
  const errors: string[] = [];
  const seen = new Set<string>();
  const check = (field: FormField, owner: string) => {
    const name = `${owner}「${field.label || field.key}」`;
    if (!field.label.trim()) errors.push(`${owner}字段 ${field.key} 缺少名称`);
    if (field.type !== "Description" && !isValidFieldKey(field.key))
      errors.push(`${name}的标识只能包含字母、数字和下划线，且不能以数字开头`);
    if (optionTypes.has(field.type) && !(field.options ?? []).length)
      errors.push(`${name}没有选项`);
    if (field.type === "Formula") {
      const { error } = parseFormula(field.formula);
      if (error) errors.push(`${name}的公式有误：${error}`);
    }
  };
  for (const field of schema.fields) {
    check(field, "表单字段");
    if (field.type !== "Description") {
      if (seen.has(field.key.toLowerCase()))
        errors.push(`表单字段标识 ${field.key} 重复`);
      seen.add(field.key.toLowerCase());
    }
    if (field.type === "Table") {
      if (!(field.columns ?? []).length)
        errors.push(`明细表「${field.label}」没有列`);
      for (const column of field.columns ?? [])
        check(column, `明细表「${field.label}」的列`);
    }
  }
  return errors;
}
