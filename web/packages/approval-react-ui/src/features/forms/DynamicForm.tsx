import { FieldError } from "../../components/shadcn/field";
import { FieldState } from "../../components/ui/fieldContext";
import { DatePicker } from "../../components/ui/extended";
import { FieldGroup } from "../../components/shadcn/field";
import { RadioGroup } from "../../components/ui/extended";
import {
  Table as UiTable,
  TableHeader,
  TableBody,
  TableFooter,
  TableRow,
  TableHead,
  TableCell,
} from "../../components/shadcn/table";
import {
  useEffect,
  useId,
  useImperativeHandle,
  useState,
  type Ref,
} from "react";
import {
  applyFormulas,
  cloneField,
  fieldTypeLabels,
  newRow as initialTableRow,
  numberTypes,
  textTypes,
  tableSum,
  validateValue,
  type FileValue,
  type FormField,
  type FormSchema,
  type FormValues,
} from "@approval/shared";
import { UserPicker } from "../../components/UserPicker";
import {
  Button,
  Checkbox,
  EmptyState,
  Field,
  Input,
  Select,
  Switch,
  Textarea,
} from "../../components/ui/primitives";
import { FileList, FileUploadInput } from "./FileInput";
import { Plus, Info } from "lucide-react";

export type FieldErrors = Record<string, string[]>;
export interface DynamicFormHandle {
  validate: () => boolean;
}
export function validateForm(
  schema: FormSchema,
  values: FormValues,
  editable?: string[],
): FieldErrors {
  const errors: FieldErrors = {};
  const allowed = editable
    ? new Set(editable.map((key) => key.toLowerCase()))
    : null;
  for (const field of schema.fields) {
    if (allowed && !allowed.has(field.key.toLowerCase())) continue;
    const error = validateValue(field, values[field.key]);
    if (error) errors[field.key] = [error];
    if (field.type === "Table" && Array.isArray(values[field.key]))
      (values[field.key] as FormValues[]).forEach((row, index) => {
        for (const column of field.columns ?? []) {
          const columnError = validateValue(column, row[column.key]);
          if (columnError)
            errors[`${field.key}[${index}].${column.key}`] = [columnError];
        }
      });
  }
  return errors;
}
export function FormValueText({
  field,
  value,
}: {
  field: FormField;
  value: unknown;
}) {
  if (field.type === "File")
    return (
      <FileList files={Array.isArray(value) ? (value as FileValue[]) : []} />
    );
  if (field.type === "User" || field.type === "Users")
    return (
      <UserPicker
        value={
          Array.isArray(value)
            ? (value as string[])
            : value
              ? [String(value)]
              : []
        }
        disabled
        onChange={() => {}}
        multiple={field.type === "Users"}
      />
    );
  if (field.type === "Switch")
    return <span>{value === true ? "是" : "否"}</span>;
  if (value != null && value !== "") {
    if (field.type === "Money" || field.type === "Formula") {
      const digits = field.type === "Money" ? 2 : (field.precision ?? 2);
      return (
        <span>
          {Number(value).toLocaleString("zh-CN", {
            minimumFractionDigits: digits,
            maximumFractionDigits: digits,
          })}
        </span>
      );
    }
    if (field.type === "Table")
      return (
        <span>
          {Array.isArray(value) ? `共 ${value.length} 行` : String(value)}
        </span>
      );
  }
  const label = (item: unknown) =>
    field.options?.find((option) => option.value === item)?.label ??
    String(item ?? "");
  return (
    <span style={{ whiteSpace: "pre-wrap" }}>
      {value == null || value === "" || (Array.isArray(value) && !value.length)
        ? "—"
        : Array.isArray(value)
          ? value.map(label).join(field.type === "DateRange" ? " 至 " : "、")
          : label(value)}
    </span>
  );
}
export function FormFieldInput({
  field,
  value,
  onChange,
  showDateInputError = true,
}: {
  field: FormField;
  value: unknown;
  onChange: (value: unknown) => void;
  showDateInputError?: boolean;
}) {
  const label = field.label || fieldTypeLabels[field.type];
  if (field.type === "Formula")
    return (
      <Input
        aria-label={label}
        value={value == null ? "" : String(value)}
        disabled
        placeholder="自动计算"
      />
    );
  if (numberTypes.has(field.type))
    return (
      <Input
        type="number"
        aria-label={label}
        value={value == null ? "" : String(value)}
        min={field.min ?? undefined}
        max={field.max ?? undefined}
        step={field.precision === 0 ? 1 : "any"}
        placeholder={field.placeholder ?? undefined}
        onChange={(event) =>
          onChange(
            event.target.value === "" ? null : Number(event.target.value),
          )
        }
      />
    );
  if (field.type === "Text" || field.type === "TextArea") {
    const props = {
      "aria-label": label,
      value: String(value ?? ""),
      maxLength: field.maxLength ?? (field.type === "Text" ? 200 : 2000),
      placeholder: field.placeholder ?? undefined,
      onChange: (
        event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>,
      ) => onChange(event.target.value),
    };
    return field.type === "TextArea" ? (
      <Textarea {...props} rows={3} />
    ) : (
      <Input {...props} />
    );
  }
  if (field.type === "Date")
    return (
      <DatePicker
        label={label}
        value={String(value ?? "")}
        showInputError={showDateInputError}
        onChange={(nextValue) => onChange(nextValue || null)}
      />
    );
  if (field.type === "DateRange") {
    const dates = Array.isArray(value) ? (value as string[]) : [];
    return (
      <div className="ar-row">
        <DatePicker
          label={`${label}开始`}
          value={dates[0] ?? ""}
          showInputError={showDateInputError}
          max={dates[1] || undefined}
          onChange={(nextValue) =>
            onChange(nextValue || dates[1] ? [nextValue, dates[1] ?? ""] : [])
          }
        />
        <DatePicker
          label={`${label}结束`}
          value={dates[1] ?? ""}
          showInputError={showDateInputError}
          min={dates[0] || undefined}
          onChange={(nextValue) =>
            onChange(dates[0] || nextValue ? [dates[0] ?? "", nextValue] : [])
          }
        />
      </div>
    );
  }
  if (field.type === "Select")
    return (
      <Select
        aria-label={label}
        value={String(value ?? "")}
        options={field.options ?? []}
        placeholder={field.placeholder ?? "请选择"}
        onValueChange={(nextValue) => onChange(nextValue || null)}
      />
    );
  if (field.type === "MultiSelect") {
    const selected = Array.isArray(value) ? (value as string[]) : [];
    return (
      <div className="ar-row">
        {field.options?.map((option) => (
          <Checkbox
            key={option.value}
            checked={selected.includes(option.value)}
            onChange={(checked) =>
              onChange(
                checked
                  ? [...selected, option.value]
                  : selected.filter((item) => item !== option.value),
              )
            }
          >
            {option.label}
          </Checkbox>
        ))}
      </div>
    );
  }
  if (field.type === "Radio")
    return (
      <RadioGroup
        value={String(value ?? "")}
        onChange={onChange}
        options={field.options ?? []}
        label={label}
      />
    );
  if (field.type === "Switch")
    return (
      <Switch checked={value === true} label={label} onChange={onChange} />
    );
  if (field.type === "User" || field.type === "Users")
    return (
      <UserPicker
        value={
          Array.isArray(value)
            ? (value as string[])
            : value
              ? [String(value)]
              : []
        }
        multiple={field.type === "Users"}
        label={label}
        onChange={(ids) =>
          onChange(field.type === "Users" ? ids : (ids[0] ?? null))
        }
      />
    );
  if (field.type === "File")
    return (
      <FileUploadInput
        field={field}
        value={Array.isArray(value) ? (value as FileValue[]) : []}
        onChange={onChange}
      />
    );
  return <span>{field.help ?? ""}</span>;
}
export function FormTableField({
  field,
  value,
  onChange,
  readonly,
  errors = {},
}: {
  field: FormField;
  value: unknown;
  onChange: (value: unknown) => void;
  readonly?: boolean;
  errors?: FieldErrors;
}) {
  const rows = Array.isArray(value) ? (value as FormValues[]) : [];
  const columns = field.columns ?? [];
  const tableId = useId();
  return (
    <div className="ar-stack ar-detail-table">
      <p className="approval:text-xs approval:text-muted-foreground approval:md:hidden">
        左右滑动查看全部明细列
      </p>
      <div className="ar-table-wrap">
        <UiTable className="ar-table">
          <TableHeader>
            <TableRow>
              {columns.map((column) => (
                <TableHead
                  key={column.key}
                  className={
                    numberTypes.has(column.type) || column.type === "Formula"
                      ? "ar-numeric-cell"
                      : undefined
                  }
                >
                  {column.label}
                  {column.required && " *"}
                </TableHead>
              ))}
              {!readonly && <TableHead>操作</TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {!rows.length && (
              <TableRow>
                <TableCell colSpan={columns.length + (readonly ? 0 : 1)}>
                  <EmptyState text="暂无明细" />
                </TableCell>
              </TableRow>
            )}
            {rows.map((row, index) => (
              <TableRow key={index}>
                {columns.map((column) => (
                  <TableCell
                    key={column.key}
                    className={
                      numberTypes.has(column.type) || column.type === "Formula"
                        ? "ar-numeric-cell"
                        : undefined
                    }
                  >
                    {readonly ? (
                      <FormValueText field={column} value={row[column.key]} />
                    ) : (
                      <FieldState.Provider
                        value={{
                          invalid:
                            !!errors[`${field.key}[${index}].${column.key}`]
                              ?.length,
                          required: column.required,
                          describedBy: errors[
                            `${field.key}[${index}].${column.key}`
                          ]?.length
                            ? `${tableId}-${index}-${column.key}-error`
                            : undefined,
                        }}
                      >
                        <FormFieldInput
                          field={column}
                          value={row[column.key]}
                          onChange={(next) =>
                            onChange(
                              rows.map((item, itemIndex) =>
                                itemIndex === index
                                  ? { ...item, [column.key]: next }
                                  : item,
                              ),
                            )
                          }
                        />
                      </FieldState.Provider>
                    )}
                    {!!errors[`${field.key}[${index}].${column.key}`]
                      ?.length && (
                      <FieldError
                        role="alert"
                        id={`${tableId}-${index}-${column.key}-error`}
                      >
                        {errors[`${field.key}[${index}].${column.key}`]!.join(
                          "；",
                        )}
                      </FieldError>
                    )}
                  </TableCell>
                ))}
                {!readonly && (
                  <TableCell>
                    <Button
                      variant="danger"
                      onClick={() =>
                        onChange(
                          rows.filter((_, rowIndex) => rowIndex !== index),
                        )
                      }
                    >
                      删除行 {index + 1}
                    </Button>
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
          {rows.length > 0 && columns.some((column) => column.sum) && (
            <TableFooter>
              <TableRow>
                {columns.map((column) => (
                  <TableCell
                    key={column.key}
                    className={
                      numberTypes.has(column.type) || column.type === "Formula"
                        ? "ar-numeric-cell"
                        : undefined
                    }
                  >
                    {column.sum ? `合计 ${tableSum(rows, column.key)}` : ""}
                  </TableCell>
                ))}
              </TableRow>
            </TableFooter>
          )}
        </UiTable>
      </div>
      {!readonly && (
        <Button
          className="ar-detail-add"
          disabled={rows.length >= (field.maxRows ?? 100)}
          onClick={() => onChange([...rows, initialTableRow(field)])}
        >
          <Plus size={16} /> 添加一行
        </Button>
      )}
    </div>
  );
}
export function DynamicForm({
  schema,
  value,
  onChange,
  readonly = false,
  editableFields = [],
  errors = {},
  ref,
}: {
  schema: FormSchema;
  value: FormValues;
  onChange: (value: FormValues) => void;
  readonly?: boolean;
  editableFields?: string[];
  errors?: FieldErrors;
  ref?: Ref<DynamicFormHandle>;
}) {
  const [localErrors, setLocalErrors] = useState<FieldErrors>({});
  const editable = new Set(editableFields.map((key) => key.toLowerCase()));
  const editing = !readonly || editable.size > 0;
  const allErrors = { ...localErrors, ...errors };
  useImperativeHandle(
    ref,
    () => ({
      validate() {
        const next = validateForm(
          schema,
          value,
          readonly ? editableFields : undefined,
        );
        setLocalErrors(next);
        return Object.keys(next).length === 0;
      },
    }),
    [schema, value, readonly, JSON.stringify(editableFields)],
  );
  function update(key: string, next: unknown) {
    const values = cloneField({ ...value, [key]: next });
    applyFormulas(schema, values);
    onChange(values);
    setLocalErrors((previous) => {
      const result = { ...previous };
      const field = schema.fields.find((field) => field.key === key);
      const error =
        field &&
        !textTypes.has(field.type) &&
        !numberTypes.has(field.type) &&
        !["Date", "DateRange"].includes(field.type)
          ? validateValue(field, next)
          : null;
      if (error) result[key] = [error];
      else delete result[key];
      return result;
    });
  }
  useEffect(() => {
    if (!editing) return;
    const next = cloneField(value);
    if (applyFormulas(schema, next)) onChange(next);
  }, [schema, value, editing]);
  if (!schema.fields.length) return <EmptyState text="没有表单字段" />;
  return (
    <FieldGroup className="ar-form-grid">
      {schema.fields.map((field) => {
        const canEdit = !readonly || editable.has(field.key.toLowerCase());
        return (
          <div
            key={field.key}
            className={`ar-form-field ar-form-field-${field.type.toLowerCase()}`}
            style={{
              gridColumn: `span ${field.type === "Table" || field.type === "Description" ? 24 : (field.span ?? 24)}`,
            }}
            onBlur={(event) => {
              if (
                !canEdit ||
                event.currentTarget.contains(event.relatedTarget as Node) ||
                (!textTypes.has(field.type) &&
                  !numberTypes.has(field.type) &&
                  !["Date", "DateRange"].includes(field.type))
              )
                return;
              const error = validateValue(field, value[field.key]);
              setLocalErrors((previous) => {
                const next = { ...previous };
                if (error) next[field.key] = [error];
                else delete next[field.key];
                return next;
              });
            }}
          >
            {field.type === "Description" ? (
              <p className="ar-form-description">
                <Info size={16} />
                <span>{field.help ?? field.label}</span>
              </p>
            ) : (
              <Field
                label={field.label + (readonly && canEdit ? "（可修改）" : "")}
                required={canEdit && field.required}
                group={[
                  "DateRange",
                  "Table",
                  "MultiSelect",
                  "Radio",
                  "File",
                ].includes(field.type)}
                help={field.help}
                error={allErrors[field.key]?.join("；")}
              >
                {field.type === "Table" ? (
                  <FormTableField
                    field={field}
                    value={value[field.key]}
                    onChange={(next) => update(field.key, next)}
                    readonly={!canEdit}
                    errors={allErrors}
                  />
                ) : canEdit ? (
                  <FormFieldInput
                    field={field}
                    value={value[field.key]}
                    showDateInputError={false}
                    onChange={(next) => update(field.key, next)}
                  />
                ) : (
                  <FormValueText field={field} value={value[field.key]} />
                )}
              </Field>
            )}
          </div>
        );
      })}
    </FieldGroup>
  );
}
