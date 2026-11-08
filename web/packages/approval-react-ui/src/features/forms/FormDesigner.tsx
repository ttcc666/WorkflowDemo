import { FieldError } from "../../components/shadcn/field";
import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";
import { Card } from "../../components/ui/primitives";
import {
  Card as UiCard,
  CardHeader,
  CardTitle,
  CardDescription,
  CardFooter,
} from "../../components/shadcn/card";
import { useLayoutEffect, useRef, useState } from "react";
import {
  Type,
  AlignLeft,
  Hash,
  CircleDollarSign,
  Calculator,
  CalendarDays,
  CalendarRange,
  ListFilter,
  ListChecks,
  CircleDot,
  ToggleLeft,
  UserRound,
  UsersRound,
  Table2,
  Paperclip,
  Info,
  Copy,
  Trash2,
  ArrowUp,
  ArrowDown,
} from "lucide-react";
import { cn } from "cn";
import {
  checkSchema,
  cloneField,
  columnTypes,
  defaultFileExtensions,
  fieldTypes,
  fieldTypeLabels,
  initialValues,
  isValidFieldKey,
  newField,
  newFieldKey,
  numberTypes,
  numericTypes,
  numericVariables,
  optionTypes,
  parseFormula,
  type FormField,
  type FormFieldType,
  type FormSchema,
  type FormValues,
} from "@approval/shared";
import {
  Button,
  Checkbox,
  Field,
  Input,
  Select,
  Textarea,
} from "../../components/ui/primitives";
import { DynamicForm, FormFieldInput } from "./DynamicForm";
import { useIsMobile } from "../../hooks/use-mobile";
import { ResponsiveInspector } from "../../components/ui/ResponsiveInspector";
const fieldIcons = {
  Text: Type,
  TextArea: AlignLeft,
  Number: Hash,
  Money: CircleDollarSign,
  Formula: Calculator,
  Date: CalendarDays,
  DateRange: CalendarRange,
  Select: ListFilter,
  MultiSelect: ListChecks,
  Radio: CircleDot,
  Switch: ToggleLeft,
  User: UserRound,
  Users: UsersRound,
  Table: Table2,
  File: Paperclip,
  Description: Info,
};

export function FormulaInput({
  value,
  onChange,
  variables,
}: {
  value: string;
  onChange: (value: string) => void;
  variables: { name: string; label: string }[];
}) {
  const parsed = parseFormula(value);
  const [samples, setSamples] = useState<Record<string, number>>({});
  const preview = parsed.formula?.evaluate((name) => samples[name] ?? null);
  return (
    <Field
      label="公式表达式"
      error={parsed.error}
      help="复用原公式语法；明细列使用同一行变量。"
    >
      <Textarea
        aria-label="公式表达式"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
      <div className="ar-row">
        {variables.map((variable) => (
          <Button
            key={variable.name}
            onClick={() => onChange(value + variable.name)}
            title={variable.label}
          >
            {variable.name}
          </Button>
        ))}
      </div>
      <Accordion type="single" collapsible>
        <AccordionItem value="content" className="approval:group/disclosure">
          <AccordionTrigger>计算预览</AccordionTrigger>
          <AccordionContent
            forceMount
            className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
          >
            <div className="approval:flex approval:flex-col approval:gap-4">
              {variables.map((variable) => (
                <Field key={variable.name} label={variable.label}>
                  <Input
                    type="number"
                    aria-label={`预览 ${variable.name}`}
                    value={samples[variable.name] ?? ""}
                    onChange={(event) =>
                      setSamples({
                        ...samples,
                        [variable.name]: Number(event.target.value),
                      })
                    }
                  />
                </Field>
              ))}
              <output>预览结果：{preview ?? "—"}</output>
            </div>
          </AccordionContent>
        </AccordionItem>
      </Accordion>
    </Field>
  );
}
export function OptionsEditor({
  value,
  onChange,
}: {
  value: { label: string; value: string }[];
  onChange: (options: { label: string; value: string }[]) => void;
}) {
  function move(index: number, delta: number) {
    const next = [...value];
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item!);
    onChange(next);
  }
  return (
    <div className="ar-stack" style={{ gap: 8 }}>
      {value.map((option, index) => (
        <div className="ar-row" key={index}>
          <Input
            aria-label={`选项 ${index + 1}名称`}
            value={option.label}
            onChange={(event) =>
              onChange(
                value.map((item, i) =>
                  i === index ? { ...item, label: event.target.value } : item,
                ),
              )
            }
          />
          <Input
            aria-label={`选项 ${index + 1}值`}
            value={option.value}
            onChange={(event) =>
              onChange(
                value.map((item, i) =>
                  i === index ? { ...item, value: event.target.value } : item,
                ),
              )
            }
          />
          <Button disabled={index === 0} onClick={() => move(index, -1)}>
            ↑
          </Button>
          <Button
            disabled={index === value.length - 1}
            onClick={() => move(index, 1)}
          >
            ↓
          </Button>
          <Button
            variant="danger"
            onClick={() => onChange(value.filter((_, i) => i !== index))}
          >
            删除选项
          </Button>
        </div>
      ))}
      <Button
        onClick={() =>
          onChange([
            ...value,
            { label: "新选项", value: `option${value.length + 1}` },
          ])
        }
      >
        添加选项
      </Button>
    </div>
  );
}
export function FieldInspector({
  field,
  otherKeys,
  update,
  variables,
  isColumn = false,
}: {
  field: FormField;
  otherKeys: string[];
  update: (patch: Partial<FormField>) => void;
  variables: { name: string; label: string }[];
  isColumn?: boolean;
}) {
  const keyError = !isValidFieldKey(field.key)
    ? "标识只能包含字母、数字和下划线，不能以数字开头"
    : otherKeys.some((key) => key.toLowerCase() === field.key.toLowerCase())
      ? "字段标识重复"
      : undefined;
  function numeric(
    key: "min" | "max" | "precision" | "maxLength" | "maxRows" | "maxFiles",
    text: string,
  ) {
    update({ [key]: text === "" ? null : Number(text) });
  }
  function changeType(type: FormFieldType) {
    update({
      type,
      defaultValue: undefined,
      ...(optionTypes.has(type) && !field.options?.length
        ? { options: [{ label: "选项一", value: "option1" }] }
        : {}),
      ...(type === "Money" ? { precision: 2 } : {}),
      ...(type === "Formula"
        ? { formula: field.formula ?? "", required: false }
        : {}),
      ...(isColumn ? { sum: numericTypes.has(type) ? field.sum : false } : {}),
    });
  }
  return (
    <div className="ar-stack">
      <Field label="字段名称">
        <Input
          aria-label="字段名称"
          value={field.label}
          onChange={(event) => update({ label: event.target.value })}
        />
      </Field>
      {field.type !== "Description" && (
        <Field label="字段标识" error={keyError}>
          <Input
            aria-label="字段标识"
            value={field.key}
            onChange={(event) => update({ key: event.target.value.trim() })}
          />
        </Field>
      )}
      <Field label="字段类型">
        <Select
          aria-label="字段类型"
          value={field.type}
          options={(isColumn
            ? columnTypes
            : fieldTypes.map((item) => item.type)
          ).map((type) => ({ value: type, label: fieldTypeLabels[type] }))}
          onValueChange={(nextValue) => changeType(nextValue as FormFieldType)}
        />
      </Field>
      {!isColumn && (
        <Field label="字段宽度">
          <Select
            aria-label="字段宽度"
            value={field.span ?? 24}
            options={[
              { label: "半行", value: "12" },
              { label: "整行", value: "24" },
            ]}
            onValueChange={(nextValue) => update({ span: Number(nextValue) })}
          />
        </Field>
      )}
      {!["Formula", "Description"].includes(field.type) && (
        <Checkbox
          checked={!!field.required}
          onChange={(required) => update({ required })}
        >
          必填
        </Checkbox>
      )}
      <Field label="占位符">
        <Input
          aria-label="占位符"
          value={field.placeholder ?? ""}
          onChange={(event) =>
            update({ placeholder: event.target.value || null })
          }
        />
      </Field>
      <Field label="帮助说明">
        <Textarea
          aria-label="帮助说明"
          value={field.help ?? ""}
          onChange={(event) => update({ help: event.target.value || null })}
        />
      </Field>
      {![
        "Table",
        "Description",
        "User",
        "Users",
        "DateRange",
        "Formula",
        "File",
      ].includes(field.type) && (
        <Field label="默认值">
          <FormFieldInput
            field={{
              ...field,
              label: "默认值",
              required: false,
              defaultValue: undefined,
            }}
            value={field.defaultValue}
            onChange={(defaultValue) => update({ defaultValue })}
          />
        </Field>
      )}
      {numberTypes.has(field.type) &&
        (["min", "max", "precision"] as const).map((key) => (
          <Field
            key={key}
            label={{ min: "最小值", max: "最大值", precision: "小数位数" }[key]}
          >
            <Input
              type="number"
              aria-label={key}
              value={field[key] ?? ""}
              onChange={(event) => numeric(key, event.target.value)}
            />
          </Field>
        ))}
      {field.type === "Formula" && (
        <Field label="小数位数">
          <Input
            aria-label="计算精度"
            type="number"
            min={0}
            max={10}
            value={field.precision ?? 2}
            onChange={(event) => numeric("precision", event.target.value)}
          />
        </Field>
      )}
      {["Text", "TextArea"].includes(field.type) && (
        <Field label="最大长度">
          <Input
            aria-label="最大长度"
            type="number"
            value={field.maxLength ?? ""}
            onChange={(event) => numeric("maxLength", event.target.value)}
          />
        </Field>
      )}
      {optionTypes.has(field.type) && (
        <Field label="选项配置">
          <OptionsEditor
            value={field.options ?? []}
            onChange={(options) => update({ options })}
          />
        </Field>
      )}
      {field.type === "Formula" && (
        <FormulaInput
          value={field.formula ?? ""}
          onChange={(formula) => update({ formula })}
          variables={variables.filter(
            (variable) =>
              variable.name.toLowerCase() !== field.key.toLowerCase(),
          )}
        />
      )}
      {isColumn && numericTypes.has(field.type) && (
        <Checkbox checked={!!field.sum} onChange={(sum) => update({ sum })}>
          列合计
        </Checkbox>
      )}
      {field.type === "File" && (
        <>
          <Field label="最多文件数">
            <Input
              aria-label="最多文件数"
              type="number"
              min={1}
              max={50}
              value={field.maxFiles ?? 10}
              onChange={(event) => numeric("maxFiles", event.target.value)}
            />
          </Field>
          <Field
            label="允许文件类型"
            help="空配置使用宿主默认限制；文件大小由宿主校验。"
          >
            <div className="ar-row">
              {defaultFileExtensions.map((type) => (
                <Checkbox
                  key={type}
                  checked={field.accept?.includes(type) ?? false}
                  onChange={(checked) =>
                    update({
                      accept: checked
                        ? [...(field.accept ?? []), type]
                        : field.accept?.filter((item) => item !== type),
                    })
                  }
                >
                  {type}
                </Checkbox>
              ))}
            </div>
          </Field>
        </>
      )}
      {field.type === "Table" && (
        <>
          <Field label="最多明细行">
            <Input
              aria-label="最多明细行"
              type="number"
              value={field.maxRows ?? 100}
              onChange={(event) => numeric("maxRows", event.target.value)}
            />
          </Field>
          <TableColumns field={field} update={update} />
        </>
      )}
    </div>
  );
}
function TableColumns({
  field,
  update,
}: {
  field: FormField;
  update: (patch: Partial<FormField>) => void;
}) {
  const columns = field.columns ?? [];
  return (
    <div className="ar-stack">
      {columns.map((column, index) => (
        <Accordion key={index} type="single" collapsible>
          <AccordionItem value="content" className="approval:group/disclosure">
            <AccordionTrigger>
              {column.label} · {column.key}
            </AccordionTrigger>
            <AccordionContent
              forceMount
              className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
            >
              <div className="approval:flex approval:flex-col approval:gap-4">
                <FieldInspector
                  field={column}
                  isColumn
                  otherKeys={columns
                    .filter((_, i) => i !== index)
                    .map((item) => item.key)}
                  variables={columns
                    .filter((item) => numericTypes.has(item.type))
                    .map((item) => ({ name: item.key, label: item.label }))}
                  update={(patch) =>
                    update({
                      columns: columns.map((item, i) =>
                        i === index ? { ...item, ...patch } : item,
                      ),
                    })
                  }
                />
                <Button
                  variant="danger"
                  onClick={() =>
                    update({ columns: columns.filter((_, i) => i !== index) })
                  }
                >
                  删除列
                </Button>
              </div>
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      ))}
      <Button
        onClick={() =>
          update({
            columns: [
              ...columns,
              {
                key: newFieldKey(
                  "Text",
                  columns.map((item) => item.key),
                ),
                label: "新列",
                type: "Text",
              },
            ],
          })
        }
      >
        添加列
      </Button>
      <small>合计变量：{field.key}_列标识_sum</small>
    </div>
  );
}
export function FormDesigner({
  value,
  onChange,
}: {
  value: FormSchema;
  onChange: (schema: FormSchema) => void;
}) {
  const [selected, setSelected] = useState<number | null>(
    value.fields.length ? 0 : null,
  );
  const [preview, setPreview] = useState(false);
  const mobile = useIsMobile(701);
  const [inspectorOpen, setInspectorOpen] = useState(false);
  const [data, setData] = useState<FormValues>({});
  const [dropIndex, setDropIndex] = useState<number | null>(null);
  const fields = value.fields;
  const cards = useRef(new Map<string, HTMLDivElement>());
  const palette = useRef<HTMLElement>(null);
  const focusPalette = useRef(false);
  const pendingFocus = useRef<string | null>(null);
  const activeIndex = fields.length
    ? Math.min(selected ?? 0, fields.length - 1)
    : -1;
  useLayoutEffect(() => {
    if (selected !== null && selected >= fields.length)
      setSelected(fields.length ? fields.length - 1 : null);
    if (pendingFocus.current) {
      cards.current.get(pendingFocus.current)?.focus();
      pendingFocus.current = null;
    }
    if (focusPalette.current) {
      palette.current?.querySelector<HTMLButtonElement>("button")?.focus();
      focusPalette.current = false;
    }
  }, [fields, selected]);
  function selectField(index: number) {
    if (index < 0 || index >= fields.length) return;
    setSelected(index);
    cards.current.get(fields[index]!.key)?.focus();
  }
  function setFields(next: FormField[]) {
    onChange({ ...value, fields: next });
  }
  function insert(type: FormFieldType, index = fields.length) {
    const next = [...fields];
    next.splice(
      index,
      0,
      newField(
        type,
        fields.map((field) => field.key),
      ),
    );
    pendingFocus.current = next[index]!.key;
    setFields(next);
    setSelected(index);
  }
  function move(from: number, target: number) {
    if (
      from < 0 ||
      from >= fields.length ||
      target < 0 ||
      target > fields.length ||
      from === target ||
      from + 1 === target
    )
      return;
    const next = [...fields];
    const [field] = next.splice(from, 1);
    const index = target > from ? target - 1 : target;
    next.splice(index, 0, field!);
    pendingFocus.current = field!.key;
    setFields(next);
    setSelected(index);
  }
  function copyField(index: number) {
    const copy = cloneField(fields[index]!);
    copy.key = newFieldKey(
      copy.type,
      fields.map((field) => field.key),
    );
    copy.label += "（副本）";
    const next = [...fields];
    next.splice(index + 1, 0, copy);
    pendingFocus.current = copy.key;
    setFields(next);
    setSelected(index + 1);
  }
  function removeField(index: number) {
    const next = fields.filter((_, i) => i !== index);
    const nextIndex = next.length ? Math.min(index, next.length - 1) : null;
    pendingFocus.current = nextIndex === null ? null : next[nextIndex]!.key;
    focusPalette.current = nextIndex === null;
    setFields(next);
    setSelected(nextIndex);
  }
  function drop(event: React.DragEvent, index: number) {
    event.preventDefault();
    event.stopPropagation();
    setDropIndex(null);
    const type = event.dataTransfer.getData(
      "application/x-approval-field-type",
    ) as FormFieldType;
    if (fieldTypes.some((item) => item.type === type)) insert(type, index);
    else {
      const source = event.dataTransfer.getData(
        "application/x-approval-field-index",
      );
      if (source !== "") move(Number(source), index);
    }
  }
  const selectedField = selected === null ? undefined : fields[selected];
  return (
    <div className="ar-form-designer">
      <aside className="ar-form-palette" ref={palette}>
        <Card title="字段库" description="点击或拖入表单。">
          {fieldTypes.map((item) => {
            const Icon = fieldIcons[item.type];
            return (
              <Button
                key={item.type}
                draggable
                onDragStart={(event) =>
                  event.dataTransfer.setData(
                    "application/x-approval-field-type",
                    item.type,
                  )
                }
                onClick={() => {
                  insert(item.type);
                  if (mobile) setInspectorOpen(true);
                }}
              >
                <Icon data-icon="inline-start" aria-hidden="true" />
                {item.label}
              </Button>
            );
          })}
        </Card>
      </aside>
      <Card
        className="ar-form-editor"
        title="当前表单"
        description={fields.length + " 个字段"}
        action={
          <div className="ar-row">
            {mobile && selectedField && (
              <Button onClick={() => setInspectorOpen(true)}>
                编辑字段属性
              </Button>
            )}
            <Button
              onClick={() => {
                setPreview(!preview);
                setData(initialValues(value));
              }}
            >
              {preview ? "返回编辑" : "实时预览"}
            </Button>
          </div>
        }
      >
        {!preview && (
          <p className="approval:text-xs approval:text-muted-foreground">
            方向键选择字段，Alt+↑/↓移动，Ctrl+D复制，Delete删除。
          </p>
        )}
        {preview ? (
          <DynamicForm schema={value} value={data} onChange={setData} />
        ) : (
          <div
            className="ar-form-grid"
            onDragOver={(event) => {
              event.preventDefault();
              setDropIndex(fields.length);
            }}
            onDrop={(event) => drop(event, fields.length)}
          >
            {fields.map((field, index) => (
              <UiCard
                key={index}
                ref={(element) => {
                  if (element) cards.current.set(field.key, element);
                  else cards.current.delete(field.key);
                }}
                role="group"
                aria-label={`字段：${field.label}（${field.key}）`}
                aria-roledescription="字段卡片"
                aria-current={selected === index ? "true" : undefined}
                tabIndex={activeIndex === index ? 0 : -1}
                onFocus={(event) => {
                  if (event.target === event.currentTarget) setSelected(index);
                }}
                onKeyDown={(event) => {
                  if (event.target !== event.currentTarget) return;
                  if (
                    event.altKey &&
                    ["ArrowUp", "ArrowDown"].includes(event.key)
                  ) {
                    event.preventDefault();
                    move(
                      index,
                      event.key === "ArrowUp" ? index - 1 : index + 2,
                    );
                    return;
                  }
                  if (
                    (event.ctrlKey || event.metaKey) &&
                    event.key.toLowerCase() === "d"
                  ) {
                    event.preventDefault();
                    copyField(index);
                    return;
                  }
                  if (event.ctrlKey || event.metaKey || event.altKey) return;
                  const next = ["ArrowDown", "ArrowRight"].includes(event.key)
                    ? index + 1
                    : ["ArrowUp", "ArrowLeft"].includes(event.key)
                      ? index - 1
                      : event.key === "Home"
                        ? 0
                        : event.key === "End"
                          ? fields.length - 1
                          : null;
                  if (next !== null) {
                    event.preventDefault();
                    selectField(next);
                  } else if (["Enter", " "].includes(event.key)) {
                    event.preventDefault();
                    selectField(index);
                    if (mobile) setInspectorOpen(true);
                  } else if (["Delete", "Backspace"].includes(event.key)) {
                    event.preventDefault();
                    removeField(index);
                  }
                }}
                className={cn(
                  "ar-field-card approval:cursor-pointer approval:focus-visible:outline-none approval:focus-visible:ring-2 approval:focus-visible:ring-ring",
                  {
                    "selected approval:ring-2 approval:ring-primary":
                      selected === index,
                    "drop-target approval:outline approval:outline-primary":
                      dropIndex === index,
                  },
                )}
                style={{
                  gridColumn: `span ${field.type === "Table" || field.type === "Description" ? 24 : (field.span ?? 24)}`,
                }}
                draggable
                onDragStart={(event) =>
                  event.dataTransfer.setData(
                    "application/x-approval-field-index",
                    String(index),
                  )
                }
                onDragOver={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  setDropIndex(index);
                }}
                onDrop={(event) => drop(event, index)}
                onClick={() => {
                  setSelected(index);
                  if (mobile) setInspectorOpen(true);
                }}
              >
                <CardHeader>
                  <CardTitle>
                    {field.label}
                    {field.required ? " *" : ""}
                  </CardTitle>
                  <CardDescription>
                    {fieldTypeLabels[field.type]} · {field.key}
                  </CardDescription>
                </CardHeader>
                <CardFooter className="approval:flex approval:flex-wrap approval:gap-2">
                  <Button
                    size="icon"
                    aria-label="复制字段"
                    title="复制字段"
                    onClick={(event) => {
                      event.stopPropagation();
                      copyField(index);
                    }}
                  >
                    <Copy data-icon="inline-start" aria-hidden="true" />
                  </Button>
                  <Button
                    size="icon"
                    aria-label="删除字段"
                    title="删除字段"
                    variant="danger"
                    onClick={(event) => {
                      event.stopPropagation();
                      removeField(index);
                    }}
                  >
                    <Trash2 data-icon="inline-start" aria-hidden="true" />
                  </Button>
                  <Button
                    size="icon"
                    aria-label="上移字段"
                    title="上移字段"
                    disabled={index === 0}
                    onClick={(event) => {
                      event.stopPropagation();
                      move(index, index - 1);
                    }}
                  >
                    <ArrowUp data-icon="inline-start" aria-hidden="true" />
                  </Button>
                  <Button
                    size="icon"
                    aria-label="下移字段"
                    title="下移字段（Alt+↓）"
                    disabled={index === fields.length - 1}
                    onClick={(event) => {
                      event.stopPropagation();
                      move(index, index + 2);
                    }}
                  >
                    <ArrowDown data-icon="inline-start" aria-hidden="true" />
                  </Button>
                </CardFooter>
              </UiCard>
            ))}
          </div>
        )}
        {checkSchema(value).map((error) => (
          <FieldError role="status" key={error}>
            {error}
          </FieldError>
        ))}
      </Card>
      <ResponsiveInspector
        mobile={mobile}
        open={inspectorOpen}
        onOpenChange={setInspectorOpen}
        title="字段属性"
        className="ar-form-inspector"
      >
        <Card
          title="字段属性"
          description={selectedField?.label ?? "选择字段后可编辑属性。"}
        >
          {selectedField ? (
            <FieldInspector
              field={selectedField}
              otherKeys={fields
                .filter((_, index) => index !== selected)
                .map((field) => field.key)}
              variables={numericVariables(value)}
              update={(patch) =>
                setFields(
                  fields.map((field, index) =>
                    index === selected ? { ...field, ...patch } : field,
                  ),
                )
              }
            />
          ) : (
            <p>选择字段编辑属性</p>
          )}
        </Card>
      </ResponsiveInspector>
    </div>
  );
}
