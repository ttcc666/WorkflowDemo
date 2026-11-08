import { Card } from "../../components/ui/primitives";
import { useRef } from "react";
import { FieldGroup } from "../../components/shadcn/field";
import {
  fieldVariables,
  type FlowSettings,
  type FormSchema,
} from "@approval/shared";
import {
  Button,
  Checkbox,
  Field,
  Input,
  Switch,
  Textarea,
} from "../../components/ui/primitives";
export function FlowSettingsPanel({
  value,
  onChange,
  form,
  roles,
}: {
  value: FlowSettings;
  onChange: (value: FlowSettings) => void;
  form: FormSchema;
  roles: string[];
}) {
  const title = useRef<HTMLInputElement>(null);
  const variables = [
    { name: "流程名", label: "流程名称" },
    { name: "申请人", label: "发起人 ID" },
    { name: "发起时间", label: "发起时间" },
    ...fieldVariables(form)
      .filter((field) => field.type !== "Users")
      .map((field) => ({ name: `form.${field.name}`, label: field.label })),
  ];
  function insert(name: string) {
    const current = value.titleTemplate ?? "";
    const token = `{${name}}`;
    const start = title.current?.selectionStart ?? current.length;
    const end = title.current?.selectionEnd ?? current.length;
    onChange({
      ...value,
      titleTemplate: current.slice(0, start) + token + current.slice(end),
    });
    requestAnimationFrame(() => {
      title.current?.focus();
      title.current?.setSelectionRange(
        start + token.length,
        start + token.length,
      );
    });
  }
  return (
    <Card
      className="ar-settings-page"
      title="发起设置"
      description="设置申请入口、分类、发起范围和审批标题。"
    >
      <FieldGroup>
        <Field
          label="在发起中心显示"
          help="打开后可通过动态表单发起；由业务模块发起的流程通常保持关闭。"
        >
          <Switch
            label="在发起中心显示"
            checked={value.startable === true}
            onChange={(startable) => onChange({ ...value, startable })}
          />
        </Field>
        {value.startable && (
          <>
            <Field label="分类">
              <Input
                aria-label="分类"
                value={value.category ?? ""}
                maxLength={100}
                onChange={(event) =>
                  onChange({ ...value, category: event.target.value || null })
                }
              />
            </Field>
            <Field label="说明">
              <Textarea
                aria-label="流程说明"
                value={value.description ?? ""}
                maxLength={500}
                onChange={(event) =>
                  onChange({
                    ...value,
                    description: event.target.value || null,
                  })
                }
              />
            </Field>
            <Field
              label="允许发起的角色"
              group
              help="不设置则不限制角色，最终仍由宿主权限授权。"
            >
              <div className="ar-option-row">
                {roles.map((role) => (
                  <Checkbox
                    key={role}
                    checked={value.initiatorRoles?.includes(role) === true}
                    onChange={(checked) =>
                      onChange({
                        ...value,
                        initiatorRoles: checked
                          ? [...(value.initiatorRoles ?? []), role]
                          : value.initiatorRoles?.filter(
                              (item) => item !== role,
                            ),
                      })
                    }
                  >
                    {role}
                  </Checkbox>
                ))}
              </div>
              <Input
                aria-label="允许发起角色"
                value={value.initiatorRoles?.join(",") ?? ""}
                placeholder="可输入宿主角色，逗号分隔"
                onChange={(event) =>
                  onChange({
                    ...value,
                    initiatorRoles: event.target.value
                      .split(",")
                      .map((role) => role.trim())
                      .filter(Boolean),
                  })
                }
              />
            </Field>
          </>
        )}
        <Field label="审批标题">
          <Input
            ref={title}
            aria-label="标题模板"
            value={value.titleTemplate ?? ""}
            maxLength={200}
            placeholder="{流程名} - {申请人}"
            onChange={(event) =>
              onChange({ ...value, titleTemplate: event.target.value || null })
            }
          />
          <div className="ar-row">
            {variables.map((variable) => (
              <Button
                size="sm"
                key={variable.name}
                title={variable.label}
                onClick={() => insert(variable.name)}
              >
                {variable.name}
              </Button>
            ))}
          </div>
        </Field>
      </FieldGroup>
    </Card>
  );
}
