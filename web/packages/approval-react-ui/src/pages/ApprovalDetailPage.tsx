import { ItemMedia } from "../components/shadcn/item";
import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router";
import { ArrowLeft, FileText, RefreshCw, UserRound } from "lucide-react";
import { Button as UiButton } from "../components/shadcn/button";
import { Avatar, AvatarFallback } from "../components/shadcn/avatar";
import { Alert, AlertDescription } from "../components/shadcn/alert";
import {
  ApiError,
  changedFields,
  formatDateTime,
  instanceStatusLabels,
  parseFormValues,
  pickValues,
  withoutFields,
  type FlowDefinition,
  type FormValues,
} from "@approval/shared";
import {
  useApprovalClient,
  useApprovalIdentity,
  useApprovalUser,
} from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "../components/CommandRecovery";
import {
  Badge,
  Card,
  EmptyState,
  ErrorState,
  Loading,
} from "../components/ui/primitives";
import {
  DynamicForm,
  type DynamicFormHandle,
  type FieldErrors,
} from "../features/forms/DynamicForm";
import { FlowViewer } from "../features/flow/FlowViewer";
import {
  ApprovalActions,
  type ApprovalActionInput,
} from "../features/approval/ApprovalActions";
import {
  ActionExecutionList,
  ApprovalTimeline,
  CarbonCopyHistory,
} from "../features/approval/ApprovalHistory";

export default function ApprovalDetailPage() {
  const { id = "" } = useParams();
  const client = useApprovalClient();
  const identity = useApprovalIdentity();
  const user = useApprovalUser();
  const intent = useCommandIntent("approval-detail");
  const instance = useAsyncData(
    (signal) => client.api.instance(id, signal),
    [id],
    !!id && !!user.me,
  );
  const task = useAsyncData(
    (signal) => client.api.myTask(id, signal),
    [id],
    !!id && !!user.me,
  );
  const definition = useAsyncData(
    (signal) => client.api.instanceDefinition(id, signal),
    [id, instance.data?.definitionId],
    !!instance.data,
  );
  const initiator = useAsyncData(
    (signal) => client.api.usersByIds([instance.data!.initiatorId], signal),
    [instance.data?.initiatorId],
    !!instance.data && user.can("approval.directory.read"),
  );
  const [values, setValues] = useState<FormValues>({});
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const lock = useRef(false);
  const formRef = useRef<DynamicFormHandle>(null);
  useEffect(() => {
    setValues(parseFormValues(instance.data?.formDataJson));
    setFieldErrors({});
  }, [instance.data?.formDataJson, instance.data?.id]);
  useEffect(() => {
    setValues({});
    setError(null);
    setFieldErrors({});
    setNotice("");
    setBusy(false);
    lock.current = false;
  }, [id, identity]);
  function refresh() {
    instance.reload();
    task.reload();
    definition.reload();
  }
  let flow: FlowDefinition | undefined;
  try {
    if (definition.data)
      flow = JSON.parse(definition.data.definitionJson) as FlowDefinition;
  } catch {}
  const current = instance.data;
  const BusinessPanel = current
    ? client.options().businessPanels?.[current.businessType]
    : undefined;
  const visibleForm =
    flow?.form && current
      ? withoutFields(flow.form, current.hiddenFields ?? [])
      : undefined;
  const editable = task.data?.editableFields ?? [];
  const changes = changedFields(
    visibleForm,
    parseFormValues(current?.formDataJson),
    values,
    editable,
  );
  const node = flow?.nodes[task.data?.task?.nodeId ?? ""];
  async function perform(input: ApprovalActionInput) {
    if (lock.current || !current) return false;
    if (
      input.kind === "approve" &&
      editable.length &&
      !BusinessPanel &&
      !formRef.current?.validate()
    )
      return false;
    const owner = client.generation;
    lock.current = true;
    setBusy(true);
    setError(null);
    setFieldErrors({});
    let reload = true;
    try {
      const taskId = input.taskId ?? task.data?.task?.id;
      const scope = `${input.kind}:${["withdraw", "terminate"].includes(input.kind) ? id : taskId}`;
      const body = {
        ...input,
        instanceId: id,
        taskId,
        ...(input.kind === "approve"
          ? {
              changes: changes.length
                ? pickValues(values, editable)
                : undefined,
            }
          : {}),
      };
      await intent.run(scope, body, (payload, key) => {
        switch (payload.kind) {
          case "approve":
            return client.api.approve(
              payload.taskId!,
              payload.comment,
              payload.changes,
              key,
            );
          case "reject":
            return client.api.reject(
              payload.taskId!,
              payload.comment,
              undefined,
              key,
            );
          case "return":
            return client.api.reject(
              payload.taskId!,
              payload.comment,
              payload.target,
              key,
            );
          case "transfer":
            return client.api.transfer(
              payload.taskId!,
              payload.users![0]!,
              payload.comment,
              key,
            );
          case "add-sign":
            return client.api.addSign(
              payload.taskId!,
              payload.users!,
              payload.mode!,
              payload.comment,
              key,
            );
          case "resubmit":
            return client.api.resubmit(
              payload.taskId!,
              payload.comment,
              payload.formData,
              key,
            );
          case "withdraw":
            return client.api.withdraw(
              payload.instanceId,
              payload.comment,
              key,
            );
          case "terminate":
            return client.api.terminate(
              payload.instanceId,
              payload.comment,
              key,
            );
          case "reassign":
            return client.api.reassign(
              payload.taskId!,
              payload.users![0]!,
              payload.comment,
              key,
            );
        }
      });
      if (client.generation !== owner) return false;
      setNotice("操作已完成");
      return true;
    } catch (cause) {
      if (
        client.generation === owner &&
        (cause as Error)?.name !== "AbortError"
      ) {
        setError(cause);
        if (cause instanceof ApiError && cause.errors) {
          setFieldErrors(cause.errors);
          reload = false;
        }
      }
      return false;
    } finally {
      if (client.generation === owner) {
        lock.current = false;
        setBusy(false);
        if (reload) refresh();
      }
    }
  }
  if (instance.error instanceof ApiError && instance.error.status === 404)
    return (
      <Card>
        <EmptyState text="审批不存在或无权查看" />
        <Link className="ar-text-link" to="../workbench">
          返回审批工作台
        </Link>
      </Card>
    );
  if (!current)
    return instance.error ? (
      <ErrorState error={instance.error} retry={instance.reload} />
    ) : (
      <Loading />
    );
  const initiatorName = initiator.data?.[0]?.name ?? current.initiatorId;
  const currentNode = current.currentNodeId
    ? (flow?.nodes[current.currentNodeId]?.name ?? current.currentNodeId)
    : "—";
  const sectionIds = {
    content: "approval-content-" + id,
    flow: "approval-flow-" + id,
    cc: "approval-cc-" + id,
    actions: "approval-actions-" + id,
  };
  return (
    <div className="ar-approval-detail">
      <div className="ar-detail-back">
        <UiButton asChild variant="ghost">
          <Link to="../workbench">
            <ArrowLeft data-icon="inline-start" />
            返回审批工作台
          </Link>
        </UiButton>
        <UiButton variant="outline" onClick={refresh} disabled={busy}>
          <RefreshCw data-icon="inline-start" />
          刷新详情
        </UiButton>
      </div>
      <CommandRecovery intent={intent} busy={busy} recovered={refresh} />
      <ErrorState
        error={error || task.error || definition.error}
        retry={refresh}
      />
      {notice && (
        <Alert>
          <AlertDescription role="status">{notice}</AlertDescription>
        </Alert>
      )}
      <Card className="ar-detail-header">
        <div className="ar-detail-title">
          <ItemMedia variant="icon" className="ar-page-icon">
            <FileText aria-hidden="true" />
          </ItemMedia>
          <div>
            <div className="ar-row">
              <h1>{current.title ?? definition.data?.name ?? "审批详情"}</h1>
              <Badge status={current.status}>
                {instanceStatusLabels[current.status]?.text}
              </Badge>
            </div>
            <p>
              {definition.data?.name ?? current.definitionKey} · v
              {current.definitionVersion}
            </p>
          </div>
        </div>
        <div className="ar-detail-meta">
          <div className="ar-task-person">
            <Avatar>
              <AvatarFallback>
                {initiatorName.charAt(0).toUpperCase()}
              </AvatarFallback>
            </Avatar>
            <span>{initiatorName} 发起</span>
          </div>
          <span>创建于 {formatDateTime(current.createdAt)}</span>
          <span>当前步骤：{currentNode}</span>
        </div>
        <div className="ar-detail-action-bar">
          <ApprovalActions
            instance={current}
            task={task.data?.task}
            capabilities={task.data?.capabilities ?? {}}
            node={node}
            form={visibleForm}
            data={values}
            changes={changes.map((field) => field.label)}
            userId={user.userId}
            busy={
              busy ||
              definition.loading ||
              task.loading ||
              (editable.length > 0 && !visibleForm)
            }
            onAction={perform}
          />
        </div>
      </Card>
      <nav className="ar-detail-section-nav" aria-label="详情章节">
        <UiButton asChild variant="outline" size="sm">
          <a href={"#" + sectionIds.content}>申请内容</a>
        </UiButton>
        {flow && (
          <UiButton asChild variant="outline" size="sm">
            <a href={"#" + sectionIds.flow}>流程图</a>
          </UiButton>
        )}
        <UiButton asChild variant="outline" size="sm">
          <a href={"#" + sectionIds.cc}>抄送历史</a>
        </UiButton>
        <UiButton asChild variant="outline" size="sm">
          <a href={"#" + sectionIds.actions}>Action 记录</a>
        </UiButton>
      </nav>
      <div className="ar-detail-layout">
        <div className="ar-detail-content">
          <section id={sectionIds.content}>
            {BusinessPanel ? (
              <BusinessPanel
                key={`${current.id}:${current.status}`}
                businessKey={current.businessKey}
              />
            ) : visibleForm ? (
              <Card
                title={
                  <span className="ar-section-title">
                    <FileText aria-hidden="true" />
                    申请内容
                  </span>
                }
              >
                <DynamicForm
                  ref={formRef}
                  schema={visibleForm}
                  value={values}
                  onChange={setValues}
                  readonly
                  editableFields={editable}
                  errors={fieldErrors}
                />
              </Card>
            ) : (
              <Card title="业务内容">
                <pre style={{ whiteSpace: "pre-wrap" }}>
                  {current.formDataJson}
                </pre>
              </Card>
            )}
          </section>
          {flow && (
            <Card id={sectionIds.flow} title="流程图">
              <FlowViewer definition={flow} instance={current} />
            </Card>
          )}
          <section id={sectionIds.cc}>
            <CarbonCopyHistory instanceId={id} />
          </section>
          <section id={sectionIds.actions}>
            <ActionExecutionList instance={current} />
          </section>
        </div>
        <aside className="ar-detail-aside">
          <Card
            title={
              <span className="ar-section-title">
                <UserRound aria-hidden="true" />
                申请信息
              </span>
            }
          >
            <dl className="ar-detail-facts">
              {[
                ["发起人", initiatorName],
                ["当前节点", currentNode],
                ["创建时间", formatDateTime(current.createdAt)],
                ["完成时间", formatDateTime(current.completedAt)],
                ["业务类型", current.businessType],
                ["业务编号", current.businessKey],
                ["实例 ID", current.id],
              ].map(([label, value]) => (
                <div key={label}>
                  <dt>{label}</dt>
                  <dd>{value ?? "—"}</dd>
                </div>
              ))}
            </dl>
          </Card>
          <ApprovalTimeline instance={current} />
        </aside>
      </div>
    </div>
  );
}
