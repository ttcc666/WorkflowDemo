import { useEffect, useLayoutEffect, useRef, useState } from "react";
import {
  Breadcrumb,
  BreadcrumbList,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "../components/shadcn/breadcrumb";
import { Badge as UiBadge } from "../components/shadcn/badge";
import {
  ItemGroup,
  Item,
  ItemMedia,
  ItemContent,
  ItemTitle,
  ItemDescription,
} from "../components/shadcn/item";
import { ArrowRight, CheckCircle2, FileText, Save, Clock3 } from "lucide-react";
import {
  Link,
  useBlocker,
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router";
import {
  ApiError,
  applyFormulas,
  initialValues,
  parseFormValues,
  formatDateTime,
  type FormValues,
} from "@approval/shared";
import {
  useApprovalClient,
  useApprovalIdentity,
  useApprovalIdentityChange,
  useApprovalUser,
} from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "../components/CommandRecovery";
import {
  Alert,
  AlertTitle,
  AlertDescription,
} from "../components/shadcn/alert";
import {
  Button,
  Badge,
  Card,
  ConfirmDialog,
  EmptyState,
  ErrorState,
  Field,
  Loading,
  Textarea,
} from "../components/ui/primitives";
import {
  DynamicForm,
  type DynamicFormHandle,
  type FieldErrors,
} from "../features/forms/DynamicForm";

function startResult(value: unknown) {
  if (!value || typeof value !== "object" || !("instanceId" in value) ||
    typeof value.instanceId !== "string" || !value.instanceId.trim())
    throw new Error("申请恢复结果缺少有效的 instanceId，请核对原操作结果");
  return { instanceId: value.instanceId };
}

function draftResult(value: unknown) {
  if (!value || typeof value !== "object" || !("draftId" in value) ||
    typeof value.draftId !== "string" || !value.draftId.trim() ||
    !("updatedAt" in value) || typeof value.updatedAt !== "string" ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(value.updatedAt) ||
    !Number.isFinite(Date.parse(value.updatedAt)))
    throw new Error("草稿恢复结果缺少有效的 draftId 或 updatedAt，请核对原操作结果");
  return { draftId: value.draftId, updatedAt: value.updatedAt };
}

export default function StartFormPage() {
  const { key = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const routeDraft = params.get("draft");
  const navigate = useNavigate();
  const client = useApprovalClient();
  const identity = useApprovalIdentity();
  const identityChanged = useApprovalIdentityChange();
  const user = useApprovalUser();
  const [draftId, setDraftId] = useState(params.get("draft"));
  const [openedDraft, setOpenedDraft] = useState(params.get("draft"));
  const start = useAsyncData(
    (signal) => client.api.startForm(key, signal),
    [key],
    user.formsEnabled && user.can("approval.instance.start"),
  );
  const draft = useAsyncData(
    (signal) =>
      openedDraft
        ? client.api.draft(openedDraft, signal)
        : Promise.resolve(null),
    [openedDraft],
    !!start.data,
  );
  const intent = useCommandIntent("form-start", {
    canExecute: (scope) =>
      (scope === `start:${key}` && user.can("approval.instance.start")) ||
      (scope === `draft:${key}` && user.can("approval.draft.write")),
  });
  const pageVersion = useRef(0);
  const discardedDraft = useRef<string | null>(null);
  const realm = JSON.stringify([client.identity.value, client.config().apiBase]);
  const previousIdentity = useRef({ identity, realm });
  // Query navigation must wait for RouterProvider to subscribe after a remount.
  useEffect(() => {
    const previous = previousIdentity.current;
    previousIdentity.current = { identity, realm };
    // StrictMode disposal changes version but keeps the initial draft's realm.
    if (!identityChanged && (previous.identity === identity || previous.realm === realm)) return;
    if (routeDraft) discardedDraft.current = routeDraft;
    setDraftId(null);
    setOpenedDraft(null);
    setParams((previous) => {
      const next = new URLSearchParams(previous);
      next.delete("draft");
      return next;
    }, { replace: true });
  }, [identity, realm, identityChanged, routeDraft, setParams]);
  useLayoutEffect(() => {
    const version = ++pageVersion.current;
    return () => { pageVersion.current = version + 1; };
  }, [key, identity]);
  const recoveredSnapshot = useRef("");
  const [values, setValues] = useState<FormValues>({});
  const [comment, setComment] = useState("");
  const [saved, setSaved] = useState("");
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const lock = useRef(false);
  const formRef = useRef<DynamicFormHandle>(null);
  const snapshot = JSON.stringify([values, comment.trim()]);
  const dirty = !!start.data && snapshot !== saved;
  const current = useRef({ values, comment, snapshot, draftId, dirty });
  current.current = { values, comment, snapshot, draftId, dirty };
  useEffect(() => {
    // Ignore the old identity's query until the router finishes replacing it.
    if (routeDraft && routeDraft === discardedDraft.current) return;
    discardedDraft.current = null;
    if (routeDraft === null || routeDraft !== current.current.draftId) {
      setDraftId(routeDraft);
      setOpenedDraft(routeDraft);
    }
  }, [routeDraft]);
  useEffect(() => {
    recoveredSnapshot.current = "";
    setValues({});
    setComment("");
    setSaved("");
    setSavedAt(null);
    setFieldErrors({});
    setError(null);
    setBusy(false);
    lock.current = false;
    setNotice("");
  }, [key, identity]);
  useEffect(() => {
    if (!start.data || (openedDraft && draft.data === undefined)) return;
    const next = initialValues(
      start.data.form,
      draft.data ? parseFormValues(draft.data.formDataJson) : {},
    );
    applyFormulas(start.data.form, next);
    setValues(next);
    setComment(draft.data?.comment ?? "");
    setSavedAt(draft.data?.updatedAt ?? null);
    setSaved(JSON.stringify([next, (draft.data?.comment ?? "").trim()]));
    if (draft.data?.adjusted)
      setNotice("流程已更新，草稿已按最新表单调整，请检查后提交");
  }, [start.data, draft.data, openedDraft]);
  async function save(silent = false) {
    if (!start.data || !user.can("approval.draft.write") || lock.current)
      return;
    const owner = client.generation;
    const version = pageVersion.current;
    const body = current.current;
    lock.current = true;
    setBusy(true);
    try {
      const result = await intent.run(
        `draft:${key}`,
        {
          draftId: body.draftId,
          formData: body.values,
          comment: body.comment.trim(),
        },
        async (payload, id) => {
          const value = await client.api.saveDraft(
            key,
            payload.draftId,
            payload.formData,
            payload.comment,
            id,
          );
          if (owner !== client.generation || version !== pageVersion.current)
            throw new DOMException("页面或身份已变化", "AbortError");
          const result = draftResult(value);
          recoveredSnapshot.current = JSON.stringify([payload.formData, payload.comment]);
          return result;
        },
      );
      if (owner !== client.generation || version !== pageVersion.current || !intent.isCurrent()) return;
      setDraftId(result.draftId);
      setSavedAt(result.updatedAt);
      setSaved(body.snapshot);
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          next.set("draft", result.draftId);
          return next;
        },
        { replace: true },
      );
      if (!silent) setNotice("草稿已保存");
    } catch (cause) {
      if (
        owner === client.generation && version === pageVersion.current &&
        (cause as Error)?.name !== "AbortError"
      )
        setError(cause);
    } finally {
      if (owner === client.generation && version === pageVersion.current) {
        lock.current = false;
        setBusy(false);
      }
    }
  }
  const saveRef = useRef(save);
  saveRef.current = save;
  useEffect(() => {
    const timer = window.setInterval(() => {
      if (current.current.dirty && !intent.pending) void saveRef.current(true);
    }, 30000);
    return () => window.clearInterval(timer);
  }, [key, identity, intent.pending]);
  const blocker = useBlocker(dirty && !busy);
  useEffect(() => {
    function unload(event: BeforeUnloadEvent) {
      if (current.current.dirty) {
        event.preventDefault();
        event.returnValue = "";
      }
    }
    window.addEventListener("beforeunload", unload);
    return () => window.removeEventListener("beforeunload", unload);
  }, []);
  async function submit() {
    if (!start.data || lock.current || !formRef.current?.validate()) return;
    const owner = client.generation;
    const version = pageVersion.current;
    lock.current = true;
    setBusy(true);
    setError(null);
    setFieldErrors({});
    try {
      const result = await intent.run(
        `start:${key}`,
        { formData: values, comment: comment.trim(), draftId },
        async (body, id) => {
          const value = await client.api.submitForm(
            key,
            body.formData,
            body.comment,
            body.draftId,
            id,
          );
          if (owner !== client.generation || version !== pageVersion.current)
            throw new DOMException("页面或身份已变化", "AbortError");
          return startResult(value);
        },
      );
      if (owner === client.generation && version === pageVersion.current && intent.isCurrent()) {
        setSaved(snapshot);
        navigate("../approvals/" + encodeURIComponent(result.instanceId));
      }
    } catch (cause) {
      if (
        owner === client.generation && version === pageVersion.current &&
        (cause as Error)?.name !== "AbortError"
      ) {
        setError(cause);
        if (cause instanceof ApiError && cause.errors)
          setFieldErrors(cause.errors);
      }
    } finally {
      if (owner === client.generation && version === pageVersion.current) {
        lock.current = false;
        setBusy(false);
      }
    }
  }
  if (!user.me || start.loading || (openedDraft && draft.loading))
    return <Loading />;
  if (!user.formsEnabled || !user.can("approval.instance.start"))
    return <EmptyState text="动态表单未启用或没有发起权限" />;
  if (!start.data)
    return (
      <ErrorState
        error={start.error ?? new Error("流程不存在或不可发起")}
        retry={start.reload}
      />
    );
  return (
    <div className="ar-stack ar-application-page">
      <Breadcrumb aria-label="页面位置">
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink asChild>
              <Link to="../start">发起中心</Link>
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>新建申请</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <header className="ar-page-header">
        <div className="ar-page-title-group">
          <ItemMedia variant="icon" className="ar-page-icon">
            <FileText size={24} strokeWidth={1.7} />
          </ItemMedia>
          <div>
            <span className="ar-eyebrow">申请中心</span>
            <h1>{start.data.name}</h1>
          </div>
        </div>
        <Badge status={draftId ? "Draft" : undefined}>
          {draftId ? "申请草稿" : "新建申请"}
        </Badge>
      </header>
      {start.data.settings.description && (
        <p className="ar-page-description">{start.data.settings.description}</p>
      )}
      <CommandRecovery
        key={key}
        intent={intent}
        busy={busy}
        onRecoveringChange={setBusy}
        recovered={(value, scope) => {
          if (!intent.isCurrent()) return;
          if (scope === `start:${key}`) {
            const result = startResult(value);
            setError(null);
            setSaved(current.current.snapshot);
            navigate("../approvals/" + encodeURIComponent(result.instanceId));
          } else if (scope === `draft:${key}`) {
            const result = draftResult(value);
            setError(null);
            setDraftId(result.draftId);
            setSavedAt(result.updatedAt);
            setSaved(recoveredSnapshot.current);
            setParams((previous) => {
              const next = new URLSearchParams(previous);
              next.set("draft", result.draftId);
              return next;
            }, { replace: true });
            setNotice("草稿已保存");
          }
        }}
      />
      <ErrorState error={error || draft.error} retry={draft.reload} />
      {notice && (
        <Alert>
          <CheckCircle2 />
          <AlertDescription role="status">{notice}</AlertDescription>
        </Alert>
      )}
      <div className="ar-application-layout">
        <div className="ar-application-content ar-stack">
          <Card
            title={
              <>
                <UiBadge variant="secondary">01</UiBadge>申请信息
              </>
            }
            description="按要求填写申请内容，必填字段以星号标注。"
          >
            <DynamicForm
              ref={formRef}
              schema={start.data.form}
              value={values}
              onChange={setValues}
              errors={fieldErrors}
            />
          </Card>
          <Card
            title={
              <>
                <UiBadge variant="secondary">02</UiBadge>补充说明
              </>
            }
          >
            <Field
              label="申请说明"
              help="可补充申请背景或需要审批人关注的事项。"
            >
              <Textarea
                aria-label="申请说明"
                rows={3}
                placeholder="填写补充说明（选填）"
                value={comment}
                onChange={(event) => setComment(event.target.value)}
              />
            </Field>
          </Card>
        </div>
        <aside className="ar-application-aside ar-stack">
          <Card title="申请步骤">
            <ItemGroup>
              {[
                ["填写申请", "完整填写表单与附件"],
                ["确认提交", "检查内容后发起申请"],
                ["查看状态", "在工作台跟踪处理结果"],
              ].map(([title, description], index) => (
                <Item
                  key={title}
                  size="sm"
                  variant={index === 0 ? "muted" : "default"}
                >
                  <ItemMedia variant="icon">{index + 1}</ItemMedia>
                  <ItemContent>
                    <ItemTitle>{title}</ItemTitle>
                    <ItemDescription>{description}</ItemDescription>
                  </ItemContent>
                </Item>
              ))}
            </ItemGroup>
          </Card>
          <Alert>
            <Clock3 />
            <AlertTitle>稍后继续填写</AlertTitle>
            <AlertDescription>
              {user.can("approval.draft.write")
                ? "保存草稿后，可从发起中心继续编辑。"
                : "提交申请后，可在工作台查看申请状态。"}
            </AlertDescription>
          </Alert>
        </aside>
      </div>
      <footer className="ar-application-actions">
        <div className="ar-save-state">
          {savedAt ? (
            <>
              <CheckCircle2 size={16} />
              <span>
                {dirty
                  ? "有未保存的修改"
                  : `最后保存 ${formatDateTime(savedAt)}`}
              </span>
            </>
          ) : (
            <>
              <span className="ar-save-dot" />
              <span>请检查必填信息后提交</span>
            </>
          )}
        </div>
        <div className="ar-row">
          <Button
            disabled={busy || !user.can("approval.draft.write")}
            onClick={() => void save()}
          >
            <Save data-icon="inline-start" aria-hidden="true" /> 保存草稿
          </Button>
          <Button variant="primary" busy={busy} onClick={() => void submit()}>
            提交申请 <ArrowRight data-icon="inline-end" aria-hidden="true" />
          </Button>
        </div>
      </footer>
      <ConfirmDialog
        open={blocker.state === "blocked"}
        onOpenChange={(open) => {
          if (!open && blocker.state === "blocked") blocker.reset();
        }}
        title="离开申请"
        confirm={() => {
          if (blocker.state === "blocked") blocker.proceed();
        }}
      >
        有未保存的内容，确认离开？
      </ConfirmDialog>
    </div>
  );
}
