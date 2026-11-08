import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";
import { Card } from "../../components/ui/primitives";
import { SuggestionInput } from "../../components/ui/extended";
import { useState } from "react";
import {
  mappingTransforms,
  newMappingRule,
  type MappingRule,
} from "@approval/shared";
import {
  Button,
  Checkbox,
  Field,
  Input,
  Select,
} from "../../components/ui/primitives";

function JsonValue({
  value,
  onChange,
  label,
  scalar,
}: {
  value: unknown;
  onChange: (value: unknown) => void;
  label: string;
  scalar?: boolean;
}) {
  const [error, setError] = useState("");
  return (
    <Field label={label} error={error}>
      <Input
        aria-label={label}
        defaultValue={JSON.stringify(value) ?? ""}
        key={JSON.stringify(value)}
        onBlur={(event) => {
          try {
            const next: unknown = event.target.value.trim()
              ? JSON.parse(event.target.value)
              : undefined;
            if (scalar && next !== null && typeof next === "object")
              throw new Error("必须是 JSON 标量");
            onChange(next);
            setError("");
          } catch {
            setError("请输入合法的 JSON 值");
          }
        }}
      />
    </Field>
  );
}
export function ActionMappingRuleEditor({
  value,
  onChange,
  remove,
  paths = [],
  depth = 0,
}: {
  value: MappingRule;
  onChange: (value: MappingRule) => void;
  remove: () => void;
  paths?: string[];
  depth?: number;
}) {
  const update = (patch: Partial<MappingRule>) =>
    onChange({ ...value, ...patch });
  const nested = ["Array", "Object"].includes(value.transform ?? "");
  return (
    <Card size="sm">
      <Field label="源路径">
        <SuggestionInput
          aria-label="源路径"
          value={value.source}
          suggestions={paths}
          onValueChange={(next) => update({ source: next })}
          onDragOver={(event) => event.preventDefault()}
          onDrop={(event) => {
            event.preventDefault();
            const path = event.dataTransfer.getData("text/plain");
            if (paths.includes(path)) update({ source: path });
          }}
        />
      </Field>
      <Field label="目标路径">
        <Input
          aria-label="目标路径"
          value={value.target}
          onChange={(event) => update({ target: event.target.value })}
        />
      </Field>
      <Select
        aria-label="转换类型"
        value={value.transform ?? "Copy"}
        options={mappingTransforms.map((type) => ({
          value: type,
          label: type,
        }))}
        onValueChange={(nextValue) => {
          const transform = nextValue;
          update({
            transform,
            precision: transform === "Number" ? value.precision : undefined,
            items: ["Array", "Object"].includes(transform)
              ? value.items
              : undefined,
            filterPath: transform === "Array" ? value.filterPath : undefined,
            filterEquals:
              transform === "Array" ? value.filterEquals : undefined,
          });
        }}
      />
      <Accordion type="single" collapsible>
        <AccordionItem value="content" className="approval:group/disclosure">
          <AccordionTrigger>默认值与转换选项</AccordionTrigger>
          <AccordionContent
            forceMount
            className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
          >
            <div className="approval:flex approval:flex-col approval:gap-4">
              <div className="ar-stack">
                <JsonValue
                  label="默认值 JSON"
                  value={value.default}
                  onChange={(next) => update({ default: next })}
                />
                <Checkbox
                  checked={!!value.omitNull}
                  onChange={(omitNull) => update({ omitNull })}
                >
                  省略空值
                </Checkbox>
                {value.transform === "Number" && (
                  <Field label="精度">
                    <Input
                      type="number"
                      aria-label="精度"
                      min={0}
                      max={10}
                      value={value.precision ?? ""}
                      onChange={(event) =>
                        update({
                          precision: event.target.value
                            ? Number(event.target.value)
                            : undefined,
                        })
                      }
                    />
                  </Field>
                )}
                {value.transform === "Array" && (
                  <>
                    <Field label="按路径过滤">
                      <Input
                        aria-label="按路径过滤"
                        value={value.filterPath ?? ""}
                        onChange={(event) =>
                          update({
                            filterPath: event.target.value || undefined,
                            filterEquals: event.target.value
                              ? (value.filterEquals ?? null)
                              : undefined,
                          })
                        }
                      />
                    </Field>
                    {value.filterPath && (
                      <JsonValue
                        label="过滤等于 JSON 值"
                        value={value.filterEquals}
                        scalar
                        onChange={(filterEquals) => update({ filterEquals })}
                      />
                    )}
                  </>
                )}
              </div>
            </div>
          </AccordionContent>
        </AccordionItem>
      </Accordion>
      {nested && depth < 4 && (
        <div className="ar-stack">
          {value.items?.map((item, index) => (
            <ActionMappingRuleEditor
              key={index}
              value={item}
              depth={depth + 1}
              onChange={(next) =>
                update({
                  items: value.items?.map((current, i) =>
                    i === index ? next : current,
                  ),
                })
              }
              remove={() =>
                update({
                  items:
                    value.items?.length === 1
                      ? undefined
                      : value.items?.filter((_, i) => i !== index),
                })
              }
            />
          ))}
          <Button
            disabled={(value.items?.length ?? 0) >= 64}
            onClick={() =>
              update({ items: [...(value.items ?? []), newMappingRule()] })
            }
          >
            添加子字段
          </Button>
        </div>
      )}
      <Button variant="danger" onClick={remove}>
        删除映射字段
      </Button>
    </Card>
  );
}
export function ActionMappingEditor({
  value,
  onChange,
  outputs,
  formKeys,
}: {
  value: unknown;
  onChange: (rules: MappingRule[]) => void;
  outputs: string[];
  formKeys: string[];
}) {
  const rules = Array.isArray(value) ? (value as MappingRule[]) : [];
  const paths = [
    ...formKeys.map((key) => `form.${key}`),
    ...outputs.map((output) => `outputs.${output}`),
    "business.key",
    "instance.id",
    "initiator.id",
  ];
  return (
    <div className="ar-stack">
      <div className="ar-row">
        {paths.map((path) => (
          <Button
            key={path}
            draggable
            onDragStart={(event) =>
              event.dataTransfer.setData("text/plain", path)
            }
          >
            {path}
          </Button>
        ))}
      </div>
      {rules.map((rule, index) => (
        <ActionMappingRuleEditor
          key={index}
          value={rule}
          paths={paths}
          onChange={(next) =>
            onChange(rules.map((item, i) => (i === index ? next : item)))
          }
          remove={() => onChange(rules.filter((_, i) => i !== index))}
        />
      ))}
      <Button
        disabled={rules.length >= 64}
        onClick={() => onChange([...rules, newMappingRule()])}
      >
        添加映射规则
      </Button>
      <small>预览使用后端单步骤模拟，不在浏览器执行另一套 Mapping 规则。</small>
    </div>
  );
}
