import { FieldSet } from "../../components/shadcn/field";
import { useEffect, useRef, useState } from "react";
import {
  initialValues,
  type AddSignMode,
  type ApprovalInstance,
  type ApprovalTask,
  type FlowNodeDef,
  type FormSchema,
  type FormValues,
} from "@approval/shared";
import { useApprovalClient, useApprovalIdentity } from "../../client";
import { useAsyncData } from "../../hooks/useAsyncData";
import { UserPicker } from "../../components/UserPicker";
import {
  Button,
  Dialog,
  ErrorState,
  Field,
  Select,
  Textarea,
} from "../../components/ui/primitives";
import { DynamicForm, type DynamicFormHandle } from "../forms/DynamicForm";

export type ApprovalActionKind =
  | "approve"
  | "reject"
  | "return"
  | "transfer"
  | "add-sign"
  | "withdraw"
  | "resubmit"
  | "terminate"
  | "reassign";
export interface ApprovalActionInput {
  kind: ApprovalActionKind;
  taskId?: string;
  comment: string;
  target?: string;
  users?: string[];
  mode?: AddSignMode;
  formData?: FormValues;
}
const titles: Record<ApprovalActionKind, string> = {
  approve: "同意",
  reject: "驳回",
  return: "退回",
  transfer: "转交",
  "add-sign": "加签",
  withdraw: "撤回",
  resubmit: "重新提交",
  terminate: "终止",
  reassign: "改派",
};
export function ApprovalActions({
  instance,
  task,
  capabilities,
  node,
  form,
  data,
  changes,
  userId,
  busy,
  onAction,
}: {
  instance: ApprovalInstance;
  task?: ApprovalTask | null;
  capabilities: Record<string, boolean>;
  node?: FlowNodeDef;
  form?: FormSchema;
  data: FormValues;
  changes: string[];
  userId: string;
  busy: boolean;
  onAction: (input: ApprovalActionInput) => Promise<boolean>;
}) {
  const [dialog, setDialog] = useState<{
    kind: ApprovalActionKind;
    task?: ApprovalTask;
  } | null>(null);
  const identity = useApprovalIdentity();
  useEffect(() => {
    setDialog(null);
  }, [identity, instance.id]);
  useEffect(() => {
    setDialog(null);
  }, [task?.id]);
  const can = (kind: string) => capabilities[`approval.task.${kind}`] === true;
  const buttons: ApprovalActionKind[] = [];
  if (task) {
    if (can("approve")) buttons.push("approve");
    if (can("reject")) buttons.push("reject");
    if (
      can("return") &&
      node?.type === "Approval" &&
      node.allowReturn !== false
    )
      buttons.push("return");
    if (
      can("transfer") &&
      node?.type === "Approval" &&
      node.allowTransfer !== false
    )
      buttons.push("transfer");
    if (
      can("add-sign") &&
      node?.type === "Approval" &&
      node.allowAddSign !== false
    )
      buttons.push("add-sign");
    if (task.kind === "Resubmit" && can("resubmit")) buttons.push("resubmit");
  }
  if (instance.canWithdraw) buttons.push("withdraw");
  if (
    ["Running", "Returned"].includes(instance.status) &&
    instance.capabilities?.["approval.instance.terminate"]
  )
    buttons.push("terminate");
  return (
    <div className="ar-stack">
      <div className="ar-row">
        {buttons.map((kind) => (
          <Button
            key={kind}
            variant={
              kind === "approve" || kind === "resubmit"
                ? "primary"
                : kind === "reject" || kind === "terminate"
                  ? "danger"
                  : "default"
            }
            disabled={busy}
            onClick={() => setDialog({ kind, task: task ?? undefined })}
          >
            {titles[kind]}
          </Button>
        ))}
        {instance.status === "Running" &&
          instance.tasks
            .filter(
              (item) =>
                item.capabilities?.["approval.task.reassign"] &&
                ["Pending", "Waiting", "Suspended"].includes(item.status),
            )
            .map((item) => (
              <Button
                key={item.id}
                disabled={busy}
                onClick={() => setDialog({ kind: "reassign", task: item })}
              >
                改派 {item.assigneeValue}
              </Button>
            ))}
      </div>
      {dialog && (
        <ActionDialog
          key={dialog.kind + (dialog.task?.id ?? "")}
          kind={dialog.kind}
          task={dialog.task}
          instance={instance}
          form={form}
          data={data}
          changes={changes}
          node={node}
          userId={userId}
          busy={busy}
          allowed={
            dialog.kind === "withdraw"
              ? instance.canWithdraw
              : dialog.kind === "terminate"
                ? instance.capabilities?.["approval.instance.terminate"] ===
                    true && ["Running", "Returned"].includes(instance.status)
                : dialog.kind === "reassign"
                  ? instance.tasks.find((item) => item.id === dialog.task?.id)
                      ?.capabilities?.["approval.task.reassign"] === true
                  : dialog.task?.id === task?.id &&
                    capabilities[`approval.task.${dialog.kind}`] === true
          }
          close={() => setDialog(null)}
          confirm={onAction}
        />
      )}
    </div>
  );
}
function ActionDialog({
  kind,
  task,
  instance,
  form,
  data,
  changes,
  node,
  userId,
  busy,
  allowed,
  close,
  confirm,
}: {
  kind: ApprovalActionKind;
  task?: ApprovalTask;
  instance: ApprovalInstance;
  form?: FormSchema;
  data: FormValues;
  changes: string[];
  node?: FlowNodeDef;
  userId: string;
  busy: boolean;
  allowed: boolean;
  close: () => void;
  confirm: (input: ApprovalActionInput) => Promise<boolean>;
}) {
  const { api } = useApprovalClient();
  const [comment, setComment] = useState("");
  const [target, setTarget] = useState("");
  const [users, setUsers] = useState<string[]>([]);
  const [mode, setMode] = useState<AddSignMode>("Before");
  const [values, setValues] = useState(initialValues(form, data));
  const formRef = useRef<DynamicFormHandle>(null);
  const targets = useAsyncData(
    (signal) => api.returnTargets(task!.id, signal),
    [task?.id],
    kind === "return" && !!task,
  );
  const required =
    ["reject", "return", "terminate"].includes(kind) ||
    (node?.requireComment === true &&
      (kind === "approve" || (kind === "add-sign" && mode === "After")));
  const missing =
    !allowed ||
    (required && !comment.trim()) ||
    (["transfer", "add-sign", "reassign"].includes(kind) && !users.length) ||
    (kind === "return" && !target);
  async function submit() {
    if (busy || missing) return;
    if (kind === "resubmit" && form && !formRef.current?.validate()) return;
    const okay = await confirm({
      kind,
      taskId: task?.id,
      comment: comment.trim(),
      target: target || undefined,
      users,
      mode,
      formData: kind === "resubmit" && form ? values : undefined,
    });
    if (okay) close();
  }
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busy) close();
      }}
      title={titles[kind]}
      wide={kind === "resubmit" && !!form}
      footer={
        <>
          <Button disabled={busy} onClick={close}>
            取消
          </Button>
          <Button
            variant="primary"
            disabled={!!missing}
            busy={busy}
            onClick={() => void submit()}
          >
            确认{titles[kind]}
          </Button>
        </>
      }
    >
      <FieldSet disabled={busy} className="ar-stack">
        {kind === "approve" && changes.length > 0 && (
          <p>同意时将提交字段修改：{changes.join("、")}</p>
        )}
        {kind === "return" && (
          <>
            <Field label="退回节点">
              <Select
                aria-label="退回节点"
                value={target}
                options={(targets.data ?? []).map((item) => ({
                  value: item.nodeId,
                  label: item.nodeName,
                }))}
                onValueChange={(nextValue) => setTarget(nextValue)}
              />
            </Field>
            <ErrorState error={targets.error} retry={targets.reload} />
            {targets.loading && <p>正在加载可退回节点…</p>}
          </>
        )}
        {["transfer", "add-sign", "reassign"].includes(kind) && (
          <Field label={kind === "add-sign" ? "加签人员" : "接收人"}>
            <UserPicker
              value={users}
              onChange={setUsers}
              multiple={kind === "add-sign"}
              excludedUsers={
                kind === "reassign"
                  ? [
                      instance.initiatorId,
                      task?.assigneeKind === "User" ? task.assigneeValue : "",
                    ].filter(Boolean)
                  : [userId, instance.initiatorId]
              }
            />
          </Field>
        )}
        {kind === "add-sign" && (
          <Field label="加签方式">
            <Select
              aria-label="加签方式"
              value={mode}
              options={[
                { value: "Before", label: "前加签：对方完成后回到原审批人" },
                { value: "After", label: "后加签：双方通过后完成" },
                { value: "Parallel", label: "并加签：与原审批同时进行" },
              ]}
              onValueChange={(nextValue) => setMode(nextValue as AddSignMode)}
            />
          </Field>
        )}
        {kind === "resubmit" && form && (
          <DynamicForm
            ref={formRef}
            schema={form}
            value={values}
            onChange={setValues}
          />
        )}
        <Field
          label={required ? "原因 / 意见（必填）" : "审批意见"}
          required={required}
        >
          <Textarea
            aria-label="审批意见"
            value={comment}
            onChange={(event) => setComment(event.target.value)}
          />
        </Field>
        {kind === "withdraw" && <p>确认撤回当前申请？</p>}
        {kind === "terminate" && <p>终止后当前审批将结束。</p>}
      </FieldSet>
    </Dialog>
  );
}
