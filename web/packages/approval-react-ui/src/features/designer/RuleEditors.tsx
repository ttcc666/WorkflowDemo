import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";
import { useState } from "react";
import { SuggestionInput } from "../../components/ui/extended";
import {
  approverTypeLabels,
  defaultNotification,
  fieldVariables,
  formatLevel,
  levelApproverTypes,
  notifyEventLabels,
  notifyEventsFor,
  parseLevel,
  userFields,
  valuelessApproverTypes,
  type ApproverRule,
  type ApproverTypeMeta,
  type ApprovalMeta,
  type FieldAccess,
  type FlowNodeData,
  type FormSchema,
  type NotificationRule,
  type NotifyEvent,
  type TimeoutPolicy,
} from "@approval/shared";
import { useApprovalUser, useDirectory } from "../../client";
import { UserPicker } from "../../components/UserPicker";
import {
  Button,
  Checkbox,
  Field,
  Input,
  Select,
  Tabs,
  Textarea,
} from "../../components/ui/primitives";

export function ApproverRulesEditor({
  value,
  onChange,
  types,
  form,
  defaultType = "Role",
  min = 1,
  preview,
}: {
  value: ApproverRule[];
  onChange: (rules: ApproverRule[]) => void;
  types: ApproverTypeMeta[];
  form: FormSchema;
  defaultType?: string;
  min?: number;
  preview?: (index?: number) => void;
}) {
  const directory = useDirectory();
  const canPreview = useApprovalUser().can("approval.definition.simulate");
  const people = userFields(form);
  const descriptor = (type: string) => types.find((item) => item.type === type);
  const kind = (type: string) =>
    descriptor(type)?.valueKind ??
    (valuelessApproverTypes.has(type)
      ? "None"
      : levelApproverTypes.has(type)
        ? "Level"
        : "Text");
  function patch(index: number, next: Partial<ApproverRule>) {
    onChange(
      value.map((rule, i) => (i === index ? { ...rule, ...next } : rule)),
    );
  }
  return (
    <div className="ar-stack">
      {value.map((rule, index) => {
        const level = parseLevel(rule.value) ?? { level: 1, field: null };
        return (
          <div className="ar-stack" key={index} style={{ gap: 6 }}>
            <Select
              aria-label={`规则 ${index + 1}类型`}
              value={rule.type}
              options={[
                ...types.map((item) => ({
                  value: item.type,
                  label: item.displayName ?? item.type,
                })),
                ...(types.some((item) => item.type === rule.type)
                  ? []
                  : [
                      {
                        value: rule.type,
                        label: approverTypeLabels[rule.type] ?? rule.type,
                      },
                    ]),
              ]}
              onValueChange={(nextValue) =>
                patch(index, {
                  type: nextValue,
                  value: kind(nextValue) === "Level" ? "1" : "",
                })
              }
            />
            {kind(rule.type) === "User" ? (
              <UserPicker
                multiple
                value={rule.value.split(",").filter(Boolean)}
                onChange={(ids) => patch(index, { value: ids.join(",") })}
              />
            ) : kind(rule.type) === "Role" ? (
              <>
                <SuggestionInput
                  aria-label="角色"
                  value={rule.value}
                  suggestions={directory.roles}
                  onValueChange={(next) => patch(index, { value: next })}
                />
              </>
            ) : kind(rule.type) === "FormUserField" && people.length ? (
              <Select
                aria-label="人员字段"
                value={rule.value}
                options={people.map((field) => ({
                  value: field.key,
                  label: `${field.label}（${field.key}）`,
                }))}
                onValueChange={(nextValue) =>
                  patch(index, { value: nextValue })
                }
              />
            ) : kind(rule.type) === "Level" ? (
              <>
                <Input
                  aria-label="级数"
                  type="number"
                  min={1}
                  max={10}
                  value={level.level}
                  onChange={(event) =>
                    patch(index, {
                      value: formatLevel(
                        Math.min(10, Math.max(1, Number(event.target.value))),
                        level.field,
                      ),
                    })
                  }
                />
                <Select
                  aria-label="基准人员字段"
                  value={level.field ?? ""}
                  placeholder="发起人"
                  options={people.map((field) => ({
                    value: field.key,
                    label: field.label,
                  }))}
                  onValueChange={(nextValue) =>
                    patch(index, {
                      value: formatLevel(level.level, nextValue || null),
                    })
                  }
                />
              </>
            ) : kind(rule.type) === "Text" &&
              descriptor(rule.type)?.options?.length ? (
              <Select
                aria-label="规则枚举参数"
                value={rule.value}
                options={descriptor(rule.type)!.options!.map((value) => ({
                  value,
                  label: value,
                }))}
                onValueChange={(nextValue) =>
                  patch(index, { value: nextValue })
                }
              />
            ) : kind(rule.type) !== "None" ? (
              <Input
                aria-label="规则参数"
                value={rule.value}
                onChange={(event) =>
                  patch(index, { value: event.target.value })
                }
              />
            ) : null}
            {descriptor(rule.type)?.help && (
              <small>{descriptor(rule.type)?.help}</small>
            )}
            {preview && canPreview && (
              <Button onClick={() => preview(index)}>
                预览规则 {index + 1}
              </Button>
            )}
            <Button
              variant="danger"
              disabled={value.length <= min}
              onClick={() => onChange(value.filter((_, i) => i !== index))}
            >
              删除规则
            </Button>
          </div>
        );
      })}
      <Button
        onClick={() =>
          onChange([
            ...value,
            {
              type: defaultType,
              value: kind(defaultType) === "Level" ? "1" : "",
            },
          ])
        }
      >
        添加规则
      </Button>
    </div>
  );
}
export function NotificationRulesEditor({
  value,
  onChange,
  events,
  meta,
  form,
}: {
  value: NotificationRule[];
  onChange: (rules: NotificationRule[]) => void;
  events: NotifyEvent[];
  meta: ApprovalMeta;
  form: FormSchema;
}) {
  const patch = (index: number, next: Partial<NotificationRule>) =>
    onChange(
      value.map((rule, i) => (i === index ? { ...rule, ...next } : rule)),
    );
  return (
    <div className="ar-stack">
      {value.map((rule, index) => (
        <Accordion key={index} type="single" collapsible defaultValue="content">
          <AccordionItem value="content" className="approval:group/disclosure">
            <AccordionTrigger>通知规则 {index + 1}</AccordionTrigger>
            <AccordionContent
              forceMount
              className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
            >
              <div className="approval:flex approval:flex-col approval:gap-4">
                <div className="ar-stack">
                  <Field label="触发事件" group>
                    <div className="ar-row">
                      {events.map((event) => (
                        <Checkbox
                          key={event}
                          checked={rule.events.includes(event)}
                          onChange={(checked) =>
                            patch(index, {
                              events: checked
                                ? [...rule.events, event]
                                : rule.events.filter((item) => item !== event),
                            })
                          }
                        >
                          {notifyEventLabels[event]}
                        </Checkbox>
                      ))}
                    </div>
                  </Field>
                  <Field label="接收人" group>
                    <ApproverRulesEditor
                      value={rule.recipients}
                      onChange={(recipients) => patch(index, { recipients })}
                      types={meta.recipientTypes}
                      form={form}
                      defaultType="Initiator"
                    />
                  </Field>
                  <Field label="通知渠道" group>
                    <div className="ar-row">
                      {meta.channels.map((channel) => (
                        <Checkbox
                          key={channel.name}
                          checked={rule.channels.includes(channel.name)}
                          onChange={(checked) =>
                            patch(index, {
                              channels: checked
                                ? [...rule.channels, channel.name]
                                : rule.channels.filter(
                                    (item) => item !== channel.name,
                                  ),
                            })
                          }
                        >
                          {channel.displayName}
                        </Checkbox>
                      ))}
                    </div>
                  </Field>
                  <Field label="通知标题">
                    <Input
                      aria-label="通知标题"
                      value={rule.title ?? ""}
                      onChange={(event) =>
                        patch(index, { title: event.target.value })
                      }
                    />
                  </Field>
                  <Field label="通知内容">
                    <Textarea
                      aria-label="通知内容"
                      value={rule.content ?? ""}
                      onChange={(event) =>
                        patch(index, { content: event.target.value })
                      }
                    />
                    <div className="ar-row">
                      {[
                        ...meta.templateVariables,
                        ...fieldVariables(form)
                          .filter((field) => field.type !== "Users")
                          .map((field) => ({
                            name: `form.${field.name}`,
                            description: field.label,
                          })),
                      ].map((variable) => (
                        <Button
                          key={variable.name}
                          onClick={() =>
                            patch(index, {
                              content:
                                (rule.content ?? "") + `{${variable.name}}`,
                            })
                          }
                        >
                          {variable.description || variable.name}
                        </Button>
                      ))}
                    </div>
                  </Field>
                  <Button
                    variant="danger"
                    onClick={() =>
                      onChange(value.filter((_, i) => i !== index))
                    }
                  >
                    删除通知
                  </Button>
                </div>
              </div>
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      ))}
      <Button
        onClick={() =>
          onChange([...value, defaultNotification(events[0] ?? "Enter")])
        }
      >
        添加通知
      </Button>
    </div>
  );
}
export function TimeoutEditor({
  value,
  onChange,
}: {
  value: TimeoutPolicy | null;
  onChange: (value: TimeoutPolicy | null) => void;
}) {
  type Unit = "minute" | "hour" | "day";
  const minutes = { minute: 1, hour: 60, day: 1440 };
  const pick = (value: number): Unit =>
    value % 1440 === 0 ? "day" : value % 60 === 0 ? "hour" : "minute";
  const [unit, setUnit] = useState<Unit>(pick(value?.minutes ?? 1440));
  const [repeatUnit, setRepeatUnit] = useState<Unit>(
    pick(value?.repeatMinutes ?? 240),
  );
  const units = [
    { value: "minute", label: "分钟" },
    { value: "hour", label: "小时" },
    { value: "day", label: "天" },
  ];
  return (
    <div className="ar-stack">
      <Checkbox
        checked={!!value}
        onChange={(checked) =>
          onChange(
            checked
              ? {
                  minutes: 1440,
                  action: "Remind",
                  repeatMinutes: null,
                  maxReminders: 3,
                }
              : null,
          )
        }
      >
        启用时限
      </Checkbox>
      {value && (
        <>
          <Field label="处理时限" group>
            <div className="ar-row">
              <Input
                aria-label="时限数值"
                type="number"
                min={0.1}
                value={+(value.minutes / minutes[unit]).toFixed(2)}
                onChange={(event) =>
                  onChange({
                    ...value,
                    minutes: Math.max(
                      1,
                      Math.round(Number(event.target.value) * minutes[unit]),
                    ),
                  })
                }
              />
              <Select
                aria-label="时限单位"
                value={unit}
                options={units}
                onValueChange={(nextValue) => setUnit(nextValue as Unit)}
              />
            </div>
          </Field>
          <Field label="超时处理">
            <Select
              aria-label="超时处理"
              value={value.action}
              options={[
                { value: "Remind", label: "提醒" },
                { value: "AutoApprove", label: "自动同意（宿主明确允许）" },
                { value: "AutoReject", label: "自动驳回（宿主明确允许）" },
              ]}
              onValueChange={(nextValue) =>
                onChange({
                  ...value,
                  action: nextValue as TimeoutPolicy["action"],
                })
              }
            />
          </Field>
          {value.action === "Remind" && (
            <>
              <Checkbox
                checked={!!value.repeatMinutes}
                onChange={(checked) =>
                  onChange({ ...value, repeatMinutes: checked ? 240 : null })
                }
              >
                仍未处理时重复催办
              </Checkbox>
              {value.repeatMinutes && (
                <>
                  <Field label="催办间隔">
                    <div className="ar-row">
                      <Input
                        aria-label="催办数值"
                        type="number"
                        min={0.1}
                        value={
                          +(value.repeatMinutes / minutes[repeatUnit]).toFixed(
                            2,
                          )
                        }
                        onChange={(event) =>
                          onChange({
                            ...value,
                            repeatMinutes: Math.max(
                              1,
                              Math.round(
                                Number(event.target.value) *
                                  minutes[repeatUnit],
                              ),
                            ),
                          })
                        }
                      />
                      <Select
                        aria-label="催办单位"
                        value={repeatUnit}
                        options={units}
                        onValueChange={(nextValue) =>
                          setRepeatUnit(nextValue as Unit)
                        }
                      />
                    </div>
                  </Field>
                  <Field label="最多提醒次数">
                    <Input
                      aria-label="最多提醒次数"
                      type="number"
                      min={1}
                      value={value.maxReminders ?? 3}
                      onChange={(event) =>
                        onChange({
                          ...value,
                          maxReminders: Number(event.target.value),
                        })
                      }
                    />
                  </Field>
                </>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
export function FieldPermissionsEditor({
  value,
  onChange,
  form,
  type,
}: {
  value: Record<string, FieldAccess>;
  onChange: (value: Record<string, FieldAccess>) => void;
  form: FormSchema;
  type: "Approval" | "Cc";
}) {
  const keys = new Set(form.fields.map((field) => field.key.toLowerCase()));
  function set(key: string, permission: string) {
    const next = { ...value };
    for (const previous of Object.keys(next))
      if (previous.toLowerCase() === key.toLowerCase()) delete next[previous];
    if (permission !== "ReadOnly") next[key] = permission as FieldAccess;
    onChange(next);
  }
  const stale = Object.keys(value).filter(
    (key) => !keys.has(key.toLowerCase()),
  );
  return (
    <div className="ar-stack">
      {form.fields
        .filter((field) => field.type !== "Description")
        .map((field) => (
          <Field key={field.key} label={field.label}>
            <Select
              aria-label={`权限 ${field.key}`}
              value={
                Object.entries(value).find(
                  ([key]) => key.toLowerCase() === field.key.toLowerCase(),
                )?.[1] ?? "ReadOnly"
              }
              options={[
                { value: "ReadOnly", label: "只读" },
                ...(type === "Approval" && field.type !== "Formula"
                  ? [{ value: "Editable", label: "可编辑" }]
                  : []),
                { value: "Hidden", label: "隐藏" },
              ]}
              onValueChange={(nextValue) => set(field.key, nextValue)}
            />
          </Field>
        ))}
      {stale.length > 0 && (
        <Button
          onClick={() =>
            onChange(
              Object.fromEntries(
                Object.entries(value).filter(([key]) =>
                  keys.has(key.toLowerCase()),
                ),
              ),
            )
          }
        >
          清除失效字段权限：{stale.join("、")}
        </Button>
      )}
    </div>
  );
}
export function NodeBasicEditor({
  data,
  onChange,
  form,
  meta,
  actionEditor,
  previewRule,
}: {
  data: FlowNodeData;
  onChange: (patch: Partial<FlowNodeData>) => void;
  form: FormSchema;
  meta: ApprovalMeta;
  actionEditor?: React.ReactNode;
  previewRule?: (index?: number) => void;
}) {
  const [tab, setTab] = useState("basic");
  return (
    <div className="ar-stack">
      <Field label="节点名称">
        <Input
          aria-label="节点名称"
          value={data.name}
          onChange={(event) => onChange({ name: event.target.value })}
        />
      </Field>
      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          { value: "basic", label: "配置" },
          ...(notifyEventsFor(data.type).length
            ? [{ value: "notification", label: "通知" }]
            : []),
          ...(["Approval", "Cc"].includes(data.type)
            ? [{ value: "fields", label: "字段" }]
            : []),
        ]}
      />
      {tab === "basic" && (
        <>
          {data.type === "Approval" && (
            <>
              <Field label="审批模式">
                <Select
                  aria-label="审批模式"
                  value={data.mode}
                  options={[
                    { value: "Any", label: "或签" },
                    { value: "All", label: "会签" },
                    { value: "Sequential", label: "依次审批" },
                  ]}
                  onValueChange={(nextValue) =>
                    onChange({
                      mode: nextValue as FlowNodeData["mode"],
                    })
                  }
                />
              </Field>
              <Field label="空审批人策略">
                <Select
                  aria-label="空审批人策略"
                  value={data.emptyApproverPolicy}
                  options={[
                    { value: "Error", label: "拒绝推进" },
                    { value: "AutoApprove", label: "自动通过" },
                  ]}
                  onValueChange={(nextValue) =>
                    onChange({
                      emptyApproverPolicy:
                        nextValue as FlowNodeData["emptyApproverPolicy"],
                    })
                  }
                />
              </Field>
              {(
                [
                  "allowReturn",
                  "allowAddSign",
                  "allowTransfer",
                  "requireComment",
                  "requireQualifiedAssignee",
                ] as const
              ).map((key) => (
                <Checkbox
                  key={key}
                  checked={data[key] === true}
                  onChange={(checked) => onChange({ [key]: checked })}
                >
                  {
                    {
                      allowReturn: "允许退回",
                      allowAddSign: "允许加签",
                      allowTransfer: "允许转交",
                      requireComment: "要求意见",
                      requireQualifiedAssignee: "要求合格处理人",
                    }[key]
                  }
                </Checkbox>
              ))}
              <TimeoutEditor
                value={data.timeout}
                onChange={(timeout) => onChange({ timeout })}
              />
            </>
          )}
          {["Approval", "Cc"].includes(data.type) && (
            <ApproverRulesEditor
              value={data.approvers}
              onChange={(approvers) => onChange({ approvers })}
              types={
                data.type === "Cc" ? meta.recipientTypes : meta.approverTypes
              }
              form={form}
              preview={previewRule}
            />
          )}
          {data.type === "Action" && actionEditor}
          {["Start", "End", "Parallel", "Join", "Condition"].includes(
            data.type,
          ) && <p>连线与分支由画布配置，完整结构由服务端校验。</p>}
        </>
      )}
      {tab === "fields" && (data.type === "Approval" || data.type === "Cc") && (
        <FieldPermissionsEditor
          value={data.fieldPermissions}
          onChange={(fieldPermissions) => onChange({ fieldPermissions })}
          form={form}
          type={data.type}
        />
      )}
      {tab === "notification" && (
        <NotificationRulesEditor
          value={data.notifications}
          onChange={(notifications) => onChange({ notifications })}
          events={notifyEventsFor(data.type)}
          meta={meta}
          form={form}
        />
      )}
    </div>
  );
}
