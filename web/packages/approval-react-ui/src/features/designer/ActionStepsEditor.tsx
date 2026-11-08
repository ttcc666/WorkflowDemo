import { FieldError } from "../../components/shadcn/field";
import { Card } from "../../components/ui/primitives";
import { useState } from "react";
import {
  actionOutputPaths,
  cloneField,
  configureActionStep,
  type ActionMode,
  type ActionStep,
  type ApprovalMeta,
  type FormSchema,
} from "@approval/shared";
import { Button, Field, Input, Select } from "../../components/ui/primitives";
import { ActionConfigEditor } from "./ActionConfigEditor";

export function ActionStepsEditor({
  value,
  onChange,
  meta,
  upstream,
  form,
  preview,
}: {
  value: ActionStep[];
  onChange: (steps: ActionStep[]) => void;
  meta: ApprovalMeta;
  upstream: ActionStep[];
  form: FormSchema;
  preview: (index: number) => void;
}) {
  const [selected, setSelected] = useState<string | null>(value[0]?.id ?? null);
  const [dragged, setDragged] = useState<string | null>(null);
  const descriptor = (type: string) =>
    meta.actions?.find((item) => item.type === type);
  function nextId() {
    let index = 1;
    while (value.some((step) => step.id === `step${index}`)) index++;
    return `step${index}`;
  }
  function update(id: string, patch: Partial<ActionStep>) {
    onChange(
      value.map((step) => (step.id === id ? { ...step, ...patch } : step)),
    );
  }
  function move(id: string, index: number) {
    const previous = value.findIndex((step) => step.id === id);
    if (index < 0 || index >= value.length || previous < 0) return;
    const next = [...value];
    const [step] = next.splice(previous, 1);
    next.splice(index, 0, step!);
    onChange(next);
  }
  function add() {
    const first = meta.actions?.[0];
    if (!first) return;
    const id = nextId();
    onChange([
      ...value,
      configureActionStep(
        {
          id,
          type: first.type,
          config: {},
          configVersion: first.configVersion,
          mode: "Sync",
          failurePolicy: "Fail",
        },
        first,
      ),
    ]);
    setSelected(id);
  }
  return (
    <div className="ar-stack ar-action-editor">
      {value.map((step, index) => {
        const active = selected === step.id;
        const details = descriptor(step.type);
        return (
          <Card
            key={step.id}
            className="ar-action-step"
            data-active={active}
            draggable
            onDragStart={() => setDragged(step.id)}
            onDragOver={(event) => event.preventDefault()}
            onDrop={(event) => {
              event.preventDefault();
              if (dragged) move(dragged, index);
              setDragged(null);
            }}
          >
            <div className="ar-row">
              <Button
                size="sm"
                onClick={() => setSelected(active ? null : step.id)}
              >
                {index + 1}. {step.name ?? step.type}
              </Button>
              <Button
                size="sm"
                disabled={index === 0}
                aria-label={`上移步骤 ${step.name ?? step.id}`}
                onClick={() => move(step.id, index - 1)}
              >
                ↑
              </Button>
              <Button
                size="sm"
                disabled={index === value.length - 1}
                aria-label={`下移步骤 ${step.name ?? step.id}`}
                onClick={() => move(step.id, index + 1)}
              >
                ↓
              </Button>
              <Button
                size="sm"
                onClick={() => {
                  const id = nextId();
                  onChange([
                    ...value,
                    { ...cloneField(step), id, outputKey: null },
                  ]);
                  setSelected(id);
                }}
              >
                复制步骤
              </Button>
              <Button
                size="sm"
                variant="danger"
                onClick={() =>
                  onChange(value.filter((item) => item.id !== step.id))
                }
              >
                删除步骤
              </Button>
            </div>
            {active && (
              <>
                <Field label="步骤名称">
                  <Input
                    aria-label="步骤名称"
                    value={step.name ?? ""}
                    onChange={(event) =>
                      update(step.id, { name: event.target.value })
                    }
                  />
                </Field>
                <Field label="动作类型">
                  <Select
                    aria-label="动作类型"
                    value={step.type}
                    options={[
                      ...(meta.actions?.map((item) => ({
                        value: item.type,
                        label: item.displayName,
                      })) ?? []),
                      ...(details
                        ? []
                        : [{ value: step.type, label: step.type }]),
                    ]}
                    onValueChange={(nextValue) => {
                      const meta = descriptor(nextValue);
                      if (meta)
                        onChange(
                          value.map((item) =>
                            item.id === step.id
                              ? configureActionStep(step, meta)
                              : item,
                          ),
                        );
                    }}
                  />
                </Field>
                {!details && (
                  <FieldError>
                    宿主未注册此动作；原配置保留，发布由后端验证。
                  </FieldError>
                )}
                <Field label="执行模式">
                  <Select
                    aria-label="执行模式"
                    value={step.mode}
                    options={(details?.supportedModes ?? [step.mode]).map(
                      (mode) => ({
                        value: mode,
                        label: mode === "Sync" ? "同步" : "提交后投递",
                      }),
                    )}
                    onValueChange={(nextValue) => {
                      const mode = nextValue as ActionMode;
                      update(step.id, {
                        mode,
                        outputKey: mode === "Async" ? null : step.outputKey,
                        failurePolicy: mode === "Async" ? "Retry" : "Fail",
                      });
                    }}
                  />
                </Field>
                {step.mode === "Sync" && step.type !== "Notification" && (
                  <Field label="输出变量">
                    <Input
                      aria-label="输出变量"
                      maxLength={64}
                      value={step.outputKey ?? ""}
                      onChange={(event) =>
                        update(step.id, {
                          outputKey: event.target.value || null,
                        })
                      }
                    />
                  </Field>
                )}
                <Field label="失败策略">
                  <Select
                    aria-label="失败策略"
                    value={step.failurePolicy}
                    options={
                      step.mode === "Async"
                        ? [{ value: "Retry", label: "重试" }]
                        : [
                            { value: "Fail", label: "停止并回滚" },
                            ...(details?.allowsContinue
                              ? [{ value: "Continue", label: "记录失败后继续" }]
                              : []),
                          ]
                    }
                    onValueChange={(nextValue) =>
                      update(step.id, {
                        failurePolicy: nextValue as ActionStep["failurePolicy"],
                      })
                    }
                  />
                </Field>
                <small>配置版本 {step.configVersion}</small>
                <ActionConfigEditor
                  step={step}
                  meta={meta}
                  form={form}
                  outputs={actionOutputPaths([
                    ...upstream,
                    ...value.slice(0, index),
                  ])}
                  onChange={(config) => update(step.id, { config })}
                />
                <Button onClick={() => preview(index)}>
                  预演节点并定位步骤
                </Button>
              </>
            )}
          </Card>
        );
      })}
      <Button disabled={!meta.actions?.length} onClick={add}>
        添加动作步骤
      </Button>
    </div>
  );
}
