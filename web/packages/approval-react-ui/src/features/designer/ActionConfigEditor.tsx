import { lazy, Suspense } from "react";
import type {
  ActionStep,
  ApprovalMeta,
  ApproverRule,
  FormSchema,
} from "@approval/shared";
import {
  Button,
  Checkbox,
  Field,
  Input,
  JsonEditor,
  Loading,
  Select,
  Textarea,
} from "../../components/ui/primitives";
import { ApproverRulesEditor } from "./RuleEditors";
import { ActionMappingEditor } from "./ActionMappingEditor";
const ActionScriptEditor = lazy(() =>
  import("./ActionScriptEditor").then((module) => ({
    default: module.ActionScriptEditor,
  })),
);
type ConfigField = {
  type?: string;
  title?: string;
  description?: string;
  enum?: string[];
  default?: unknown;
  minimum?: number;
  maximum?: number;
};
type ConfigSchema = {
  properties?: Record<string, ConfigField>;
  required?: string[];
  queries?: {
    dataSource: string;
    queryId: string;
    parameters: Record<string, string>;
    maxRows: number;
    timeoutSeconds: number;
  }[];
};
export function ActionConfigEditor({
  step,
  onChange,
  meta,
  outputs,
  form,
}: {
  step: ActionStep;
  onChange: (config: Record<string, unknown>) => void;
  meta: ApprovalMeta;
  outputs: string[];
  form: FormSchema;
}) {
  const schema = meta.actions?.find((item) => item.type === step.type)
    ?.configSchema as ConfigSchema | null | undefined;
  const config = step.config;
  function set(key: string, value: unknown) {
    const next = { ...config };
    if (value === undefined) delete next[key];
    else next[key] = value;
    if (key === "dataSource" && value !== config.dataSource) {
      delete next.queryId;
      delete next.parameters;
    }
    onChange(next);
  }
  if (step.type === "JavaScript")
    return (
      <Suspense fallback={<Loading />}>
        <ActionScriptEditor
          value={String(config.script ?? "")}
          onChange={(script) => set("script", script)}
          outputs={outputs}
          fields={form.fields.map((field) => field.key)}
        />
      </Suspense>
    );
  if (step.type === "Mapping")
    return (
      <ActionMappingEditor
        value={config.mappings}
        onChange={(mappings) => set("mappings", mappings)}
        outputs={outputs}
        formKeys={form.fields.map((field) => field.key)}
      />
    );
  if (step.type === "Notification")
    return (
      <div className="ar-stack">
        <Field label="接收人" group>
          <ApproverRulesEditor
            value={(config.recipients ?? []) as ApproverRule[]}
            onChange={(recipients) => set("recipients", recipients)}
            types={meta.recipientTypes}
            form={form}
            defaultType="Initiator"
          />
        </Field>
        <Field label="渠道" group>
          <div className="ar-row">
            {meta.channels.map((channel) => (
              <Checkbox
                key={channel.name}
                checked={((config.channels ?? []) as string[]).includes(
                  channel.name,
                )}
                onChange={(checked) =>
                  set(
                    "channels",
                    checked
                      ? [...((config.channels as string[]) ?? []), channel.name]
                      : (config.channels as string[]).filter(
                          (item) => item !== channel.name,
                        ),
                  )
                }
              >
                {channel.displayName}
              </Checkbox>
            ))}
          </div>
        </Field>
        <Field label="消息标题">
          <Input
            aria-label="消息标题"
            value={String(config.title ?? "")}
            onChange={(event) => set("title", event.target.value)}
          />
        </Field>
        <Field label="消息内容">
          <Textarea
            aria-label="消息内容"
            value={String(config.content ?? "")}
            onChange={(event) => set("content", event.target.value)}
          />
          <div className="ar-row">
            {[
              "business.key",
              ...form.fields.map((field) => `form.${field.key}`),
              ...outputs.map((output) => `outputs.${output}`),
            ].map((variable) => (
              <Button
                key={variable}
                onClick={() =>
                  set(
                    "content",
                    String(config.content ?? "") + `{{${variable}}}`,
                  )
                }
              >
                {variable}
              </Button>
            ))}
          </div>
        </Field>
      </div>
    );
  const fields = Object.entries(schema?.properties ?? {});
  if (!fields.length)
    return (
      <JsonEditor
        label="动作配置 JSON"
        value={config}
        onChange={(value) => onChange(value as Record<string, unknown>)}
      />
    );
  const labels: Record<string, string> = {
    endpoint: "宿主目标",
    method: "HTTP 方法",
    timeoutSeconds: "超时（秒）",
    query: "查询参数",
    headers: "请求头",
    body: "JSON 请求体",
    form: "表单请求体",
    dataSource: "数据源",
    queryId: "查询 ID",
    parameters: "查询参数",
  };
  const query = schema?.queries?.find(
    (item) =>
      item.dataSource === config.dataSource && item.queryId === config.queryId,
  );
  return (
    <div className="ar-stack">
      {fields.map(([key, field]) => {
        const label = field.title ?? labels[key] ?? key;
        const required = schema?.required?.includes(key);
        const choices =
          key === "queryId" && schema?.queries && config.dataSource
            ? schema.queries
                .filter((item) => item.dataSource === config.dataSource)
                .map((item) => item.queryId)
            : field.enum;
        if (field.type === "object" || field.type === "array")
          return (
            <JsonEditor
              key={key}
              kind={field.type}
              label={label}
              value={config[key]}
              optional={!required}
              onChange={(value) => set(key, value)}
            />
          );
        return (
          <Field
            key={key}
            label={label}
            required={required}
            help={field.description}
          >
            {choices ? (
              <Select
                aria-label={label}
                value={String(config[key] ?? field.default ?? "")}
                options={choices.map((value) => ({ value, label: value }))}
                onValueChange={(nextValue) => set(key, nextValue || undefined)}
              />
            ) : field.type === "integer" || field.type === "number" ? (
              <Input
                aria-label={label}
                type="number"
                min={field.minimum}
                max={field.maximum}
                value={(config[key] ?? field.default ?? "") as string | number}
                onChange={(event) =>
                  set(
                    key,
                    event.target.value ? Number(event.target.value) : undefined,
                  )
                }
              />
            ) : field.type === "boolean" ? (
              <Checkbox
                checked={(config[key] ?? field.default) === true}
                onChange={(value) => set(key, value)}
              >
                {label}
              </Checkbox>
            ) : (
              <Input
                aria-label={label}
                value={String(config[key] ?? field.default ?? "")}
                onChange={(event) => set(key, event.target.value)}
              />
            )}
          </Field>
        );
      })}
      {step.type === "Http" && (
        <small>
          仅使用宿主登记 HTTPS 目标；写请求提交后投递，凭证归宿主。同步 GET
          可返回 statusCode/body；HTTP 预演仅展示请求计划。
        </small>
      )}
      {step.type === "Sql" && (
        <small>查询由宿主登记并只读，不能在前端输入 SQL。</small>
      )}
      {query && (
        <small>
          最多 {query.maxRows} 行 · {query.timeoutSeconds} 秒 ·{" "}
          {Object.entries(query.parameters)
            .map(([name, type]) => `${name} (${type})`)
            .join("、") || "无参数"}
        </small>
      )}
      <small>
        模板可引用 {"{{form.field}}"} 与 {"{{outputs.key.path}}"}。
      </small>
    </div>
  );
}
