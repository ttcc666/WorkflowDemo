import { FieldError } from "../components/shadcn/field";
import { Card } from "../components/ui/primitives";
import {
  Undo2,
  Redo2,
  LayoutGrid,
  Save,
  ShieldCheck,
  Eye,
  Play,
  Rocket,
  UserRound,
  Mail,
  GitBranch,
  Split,
  Merge,
  Zap,
  Flag,
  CircleDot,
} from "lucide-react";
import { PageHeader } from "../components/ui/PageHeader";
import { Separator } from "../components/shadcn/separator";
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
} from "react";
import { Link, useBlocker, useParams, useSearchParams } from "react-router";
import {
  ApiError,
  newFlowDefinition,
  nodeTypeLabels,
  notifyEventsFor,
  PALETTE_DRAG_TYPE,
  upstreamActions,
  type ApprovalMeta,
  type DefinitionGovernance,
  type FlowDefinition,
  type NodeType,
} from "@approval/shared";
import {
  useApprovalClient,
  useApprovalIdentity,
  useApprovalUser,
  useDirectory,
} from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "../components/CommandRecovery";
import {
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  Dialog,
  ErrorState,
  Field,
  Input,
  Loading,
  Tabs,
  Textarea,
} from "../components/ui/primitives";
import { createDesignerModel } from "../features/designer/designerModel";
import {
  NodeBasicEditor,
  NotificationRulesEditor,
} from "../features/designer/RuleEditors";
import { ActionStepsEditor } from "../features/designer/ActionStepsEditor";
import { DesignerSimulation } from "../features/designer/DesignerSimulation";
import { FlowCanvas, type FlowCanvasHandle } from "../features/flow/FlowCanvas";
import { FlowViewer } from "../features/flow/FlowViewer";
import { FormDesigner } from "../features/forms/FormDesigner";
import { useIsMobile } from "../hooks/use-mobile";
import { ResponsiveInspector } from "../components/ui/ResponsiveInspector";
import { FlowSettingsPanel } from "../features/forms/FlowSettingsPanel";

const emptyMeta: ApprovalMeta = {
  approverTypes: [],
  recipientTypes: [],
  channels: [],
  templateVariables: [],
  defaultTemplates: {},
};
const nodeIcons = {
  Start: CircleDot,
  Approval: UserRound,
  Cc: Mail,
  Condition: GitBranch,
  Parallel: Split,
  Join: Merge,
  Action: Zap,
  End: Flag,
};
export default function DesignerPage() {
  const { key = "" } = useParams();
  const identity = useApprovalIdentity();
  return <DesignerEditor key={JSON.stringify([key, identity])} />;
}

function DesignerEditor() {
  const { key = "" } = useParams();
  const [query, setQuery] = useSearchParams();
  const from = query.get("from");
  const client = useApprovalClient();
  const identity = useApprovalIdentity();
  const user = useApprovalUser();
  const directory = useDirectory();
  const model = useMemo(() => createDesignerModel(), []);
  const canvas = useRef<FlowCanvasHandle>(null);
  const state = useSyncExternalStore(
    model.state.subscribe,
    model.state.getSnapshot,
  );
  const [name, setName] = useState(query.get("name") ?? key);
  const [savedName, setSavedName] = useState(query.get("name") ?? key);
  const [governance, setGovernance] = useState<DefinitionGovernance | null>(
    null,
  );
  const [tab, setTab] = useState("flow");
  const mobile = useIsMobile(701);
  const [inspectorOpen, setInspectorOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState("");
  const [note, setNote] = useState("");
  const [publishOpen, setPublishOpen] = useState(false);
  const [preview, setPreview] = useState<FlowDefinition | null>(null);
  const [simulation, setSimulation] = useState<{
    definition: FlowDefinition;
    nodeId?: string;
    ruleIndex?: number;
    actionId?: string;
  } | null>(null);
  const intent = useCommandIntent("definition-publish", { permission: "approval.definition.design" });
  const can = (permission: string) =>
    governance?.capabilities?.[permission] ?? user.can(permission);
  const loaded = useAsyncData(
    async (signal) => {
      let governance: DefinitionGovernance | null = null;
      try {
        governance = await client.api.definitionGovernance(key, signal);
      } catch (cause) {
        if (!(cause instanceof ApiError && cause.status === 404)) throw cause;
      }
      if (!from && governance?.draftJson)
        return {
          governance,
          name: governance.name,
          definition: JSON.parse(governance.draftJson) as FlowDefinition,
        };
      try {
        const definition = from
          ? await client.api.definitionById(from, signal)
          : await client.api.latestDefinition(key);
        return {
          governance,
          name: definition.name,
          definition: JSON.parse(definition.definitionJson) as FlowDefinition,
        };
      } catch (cause) {
        if (!(cause instanceof ApiError && cause.status === 404) || from || (governance?.lastVersion ?? 0) > 0) throw cause;
        return {
          governance,
          name: query.get("name") ?? key,
          definition: newFlowDefinition(),
        };
      }
    },
    [key, from, user.canViewDefinitions],
    user.canViewDefinitions,
  );
  const meta = useAsyncData(
    () => client.api.meta(),
    [],
    user.canViewDefinitions,
  );
  const loadRoles = directory.loadRoles;
  useEffect(() => { void loadRoles(); }, [identity, loadRoles]);
  useEffect(() => {
    if (loaded.data) {
      setGovernance(loaded.data.governance);
      setName(loaded.data.name);
      setSavedName(loaded.data.name);
      model.load(loaded.data.definition);
    }
  }, [loaded.data, model]);
  const dirty = state.dirty || name.trim() !== savedName;
  const ready = !!loaded.data && !loaded.loading && !loaded.error;
  const blocker = useBlocker(dirty);
  useEffect(() => {
    function unload(event: BeforeUnloadEvent) {
      if (dirty) {
        event.preventDefault();
        event.returnValue = "";
      }
    }
    function shortcut(event: KeyboardEvent) {
      if (
        !(event.ctrlKey || event.metaKey) ||
        !["z", "y"].includes(event.key.toLowerCase())
      )
        return;
      const target = event.target as HTMLElement;
      if (
        target.closest(".cm-editor") ||
        target.matches("input,textarea,[contenteditable]")
      )
        return;
      event.preventDefault();
      if (event.shiftKey || event.key.toLowerCase() === "y") model.redo();
      else model.undo();
    }
    window.addEventListener("beforeunload", unload);
    window.addEventListener("keydown", shortcut);
    return () => {
      window.removeEventListener("beforeunload", unload);
      window.removeEventListener("keydown", shortcut);
    };
  }, [model, dirty]);
  async function save() {
    if (!ready) throw new Error("流程尚未加载成功，请重试后编辑");
    if (!can("approval.definition.design")) throw new ApiError("没有流程设计权限", 403);
    if (!name.trim()) throw new Error("请填写流程名称");
    const body = {
      name: name.trim(),
      definitionJson: JSON.stringify(model.definition()),
      expectedRevision: governance?.revision ?? 0,
      sourceDefinitionId:
        from ??
        governance?.draftSourceDefinitionId ??
        governance?.activeDefinitionId ??
        null,
    };
    const result = await intent.run(`draft:${key}`, body, (payload, id) =>
      client.api.saveDesignDraft(key, payload, id),
    );
    if (!intent.isCurrent()) return result;
    setGovernance(result);
    setSavedName(body.name);
    model.markSaved(JSON.parse(body.definitionJson) as FlowDefinition);
    return result;
  }
  async function run(action: () => Promise<unknown>, text: string) {
    if (busy) return;
    const owner = client.generation;
    setBusy(true);
    setError(null);
    try {
      await action();
      if (owner === client.generation && intent.isCurrent()) setNotice(text);
    } catch (cause) {
      if (owner === client.generation && intent.isCurrent()) {
        setError(cause);
        if (cause instanceof ApiError && cause.status === 409) {
          try {
            setGovernance(await client.api.definitionGovernance(key));
          } catch {}
        }
      }
    } finally {
      if (owner === client.generation && intent.isCurrent()) setBusy(false);
    }
  }
  async function validate() {
    if (!ready) throw new Error("流程尚未加载成功，请重试后编辑");
    const local = model.localErrors();
    const result = user.can("approval.definition.validate")
      ? await client.api.validate(JSON.stringify(model.definition()))
      : { errors: [], warnings: [] };
    model.validation([...local, ...result.errors], result.warnings ?? []);
    return local.length + result.errors.length === 0;
  }
  async function publish() {
    if (!can("approval.definition.publish")) throw new ApiError("没有流程发布权限", 403);
    if (!(await validate())) throw new Error("校验未通过，请修正后发布");
    const current = intent.pending === key ? governance : await save();
    if (!current?.draftJson) throw new Error("请先保存草稿");
    const result = await intent.run(
      key,
      { expectedRevision: current.revision, changeNote: note.trim() },
      (body, id) => {
        if (client.me.value?.capabilities?.["approval.definition.publish"] !== true) throw new ApiError("没有流程发布权限", 403);
        return client.api.publishDesignDraft(key, body, id);
      },
    );
    if (!intent.isCurrent()) return;
    setGovernance({
      ...current,
      revision: current.revision + 1,
      lastVersion: result.version,
      activeDefinitionId: result.id,
      draftSourceDefinitionId: result.id,
      hasDraftChanges: false,
    });
    model.markSaved(JSON.parse(current.draftJson) as FlowDefinition);
    setPublishOpen(false);
    if (from) setQuery({});
  }
  const selected = state.nodes.find((node) => node.id === state.selectedNodeId);
  const edge = state.edges.find((item) => item.id === state.selectedEdgeId);
  const metadata = meta.data ?? emptyMeta;
  if (!user.me) return <Loading />;
  if (!user.canViewDefinitions || !user.can("approval.definition.design"))
    return <ErrorState error={new Error("没有流程设计或定义读取权限")} />;
  return (
    <div className="ar-product-page ar-studio-page">
      <CommandRecovery intent={intent} busy={busy} recovered={(_value, scope) => { if (scope === key || scope === `draft:${key}`) loaded.reload(); }} />
      <PageHeader
        title="流程设计器"
        eyebrow="流程管理"
        description="组织审批节点与分支，配置表单和动作，校验后发布新版本。"
      />
      <Card className="ar-studio-toolbar">
        <div className="ar-studio-identity">
          <Link to="../definitions">← 流程定义</Link>
          <Field label="流程名称">
            <Input
              aria-label="流程名称"
              value={name}
              disabled={!ready}
              onChange={(event) => setName(event.target.value)}
            />
          </Field>
          <Badge>
            {key} · v{governance?.lastVersion ?? "草稿"}
          </Badge>
          {dirty && <Badge>未保存</Badge>}
        </div>
        <div className="ar-studio-actions">
          {mobile && tab === "flow" && (
            <Button onClick={() => setInspectorOpen(true)}>
              编辑节点 / 通知
            </Button>
          )}
          <Button disabled={!ready || !state.canUndo} onClick={model.undo}>
            <Undo2 data-icon="inline-start" aria-hidden="true" />
            撤销
          </Button>
          <Button disabled={!ready || !state.canRedo} onClick={model.redo}>
            <Redo2 data-icon="inline-start" aria-hidden="true" />
            重做
          </Button>
          <Button
            disabled={!ready}
            onClick={() => {
              model.autoLayout();
              canvas.current?.fit();
            }}
          >
            <LayoutGrid data-icon="inline-start" aria-hidden="true" />
            自动布局
          </Button>
          <Button
            disabled={busy || !ready || !can("approval.definition.design")}
            onClick={() => void run(save, "设计草稿已保存")}
          >
            <Save data-icon="inline-start" aria-hidden="true" />
            保存草稿
          </Button>
          <Button
            disabled={busy || !ready || !user.can("approval.definition.validate")}
            onClick={() => void run(validate, "校验完成")}
          >
            <ShieldCheck data-icon="inline-start" aria-hidden="true" />
            校验
          </Button>
          <Separator orientation="vertical" />
          <Button disabled={!ready} onClick={() => setPreview(model.definition())}>
            <Eye data-icon="inline-start" aria-hidden="true" />
            预览
          </Button>
          <Button
            disabled={!ready || !user.can("approval.definition.simulate")}
            onClick={() => setSimulation({ definition: model.definition() })}
          >
            <Play data-icon="inline-start" aria-hidden="true" />
            模拟
          </Button>
          <Button
            variant="primary"
            disabled={busy || !ready || !can("approval.definition.publish")}
            onClick={() => setPublishOpen(true)}
          >
            <Rocket data-icon="inline-start" aria-hidden="true" />
            发布
          </Button>
        </div>
      </Card>
      <ErrorState
        error={error || loaded.error || meta.error}
        retry={loaded.reload}
      />
      {notice && <p role="status">{notice}</p>}
      {state.errors.map((message) => (
        <FieldError role="alert" key={message}>
          {message}
        </FieldError>
      ))}
      {state.warnings.map((message) => (
        <p key={message}>警告：{message}</p>
      ))}
      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          { value: "flow", label: "流程" },
          { value: "form", label: `表单（${state.form.fields.length}）` },
          { value: "settings", label: "发起设置" },
        ]}
      />
      {!ready ? (
        loaded.loading ?
        <Loading />
        : null
      ) : (
        <>
          <div
            style={{ display: tab === "flow" ? undefined : "none" }}
            className="ar-designer-layout"
          >
            <aside className="ar-node-library">
              <Card title="节点库" description="拖入画布，或点击添加节点。">
                {Object.entries(nodeTypeLabels)
                  .filter(([type]) => type !== "Start")
                  .map(([type, label]) => {
                    const Icon = nodeIcons[type as NodeType];
                    return (
                      <Button
                        key={type}
                        draggable
                        onDragStart={(event) =>
                          event.dataTransfer.setData(PALETTE_DRAG_TYPE, type)
                        }
                        onClick={() => {
                          model.addNode(
                            type as NodeType,
                            canvas.current?.viewportCenter(),
                          );
                          if (mobile) setInspectorOpen(true);
                        }}
                      >
                        <Icon data-icon="inline-start" aria-hidden="true" />
                        添加{label}
                      </Button>
                    );
                  })}
                <Button
                  onClick={() => {
                    model.select(null);
                    if (mobile) setInspectorOpen(true);
                  }}
                >
                  流程通知
                </Button>
              </Card>
            </aside>
            <FlowCanvas
              ref={canvas}
              model={model}
              onInspect={() => {
                if (mobile) setInspectorOpen(true);
              }}
            />
            <ResponsiveInspector
              mobile={mobile}
              open={inspectorOpen && tab === "flow"}
              onOpenChange={setInspectorOpen}
              title={
                selected?.data ? "节点属性" : edge ? "连线设置" : "流程通知"
              }
              className="ar-studio-inspector"
            >
              <Card
                title={
                  selected?.data ? "节点属性" : edge ? "连线设置" : "流程通知"
                }
                description={
                  selected?.data
                    ? nodeTypeLabels[selected.data.type]
                    : edge
                      ? "调整分支条件与优先级。"
                      : "配置流程级通知规则。"
                }
              >
                {selected?.data ? (
                  <>
                    <NodeBasicEditor
                      previewRule={(index) =>
                        setSimulation({
                          definition: model.definition(),
                          nodeId: selected.id,
                          ruleIndex: index,
                        })
                      }
                      key={selected.id}
                      data={selected.data}
                      form={state.form}
                      meta={metadata}
                      onChange={(patch) => model.updateNode(selected.id, patch)}
                      actionEditor={
                        <ActionStepsEditor
                          value={selected.data.actions ?? []}
                          onChange={(actions) =>
                            model.updateNode(selected.id, { actions })
                          }
                          meta={metadata}
                          upstream={upstreamActions(
                            state.nodes,
                            state.edges,
                            selected.id,
                          )}
                          form={state.form}
                          preview={(index) =>
                            setSimulation({
                              definition: model.definition(),
                              nodeId: selected.id,
                              actionId: selected.data?.actions?.[index]?.id,
                            })
                          }
                        />
                      }
                    />
                    {user.can("approval.definition.simulate") && (
                      <Button
                        onClick={() =>
                          setSimulation({
                            definition: model.definition(),
                            nodeId: selected.id,
                          })
                        }
                      >
                        单节点预演
                      </Button>
                    )}
                    <Button
                      variant="danger"
                      disabled={selected.data.type === "Start"}
                      onClick={() => model.removeNode(selected.id)}
                    >
                      删除节点
                    </Button>
                  </>
                ) : edge ? (
                  <>
                    <strong>
                      {edge.source} → {edge.target}
                    </strong>
                    {edge.data && (
                      <>
                        <Field label="分支优先级">
                          <Input
                            aria-label="分支优先级"
                            type="number"
                            value={edge.data.priority}
                            onChange={(event) =>
                              model.updateBranch(edge.id, {
                                priority: Number(event.target.value),
                              })
                            }
                          />
                        </Field>
                        <Checkbox
                          checked={edge.data.expression === null}
                          onChange={(checked) =>
                            model.updateBranch(edge.id, {
                              expression: checked ? null : "",
                            })
                          }
                        >
                          默认分支
                        </Checkbox>
                        {edge.data.expression !== null && (
                          <Field label="条件表达式">
                            <Textarea
                              aria-label="条件表达式"
                              value={edge.data.expression ?? ""}
                              onChange={(event) =>
                                model.updateBranch(edge.id, {
                                  expression: event.target.value,
                                })
                              }
                            />
                          </Field>
                        )}
                      </>
                    )}
                    <Button
                      variant="danger"
                      onClick={() => model.removeEdge(edge.id)}
                    >
                      删除连线
                    </Button>
                  </>
                ) : (
                  <>
                    <h2>流程级通知</h2>
                    <NotificationRulesEditor
                      value={state.notifications}
                      onChange={model.updateNotifications}
                      events={notifyEventsFor("Flow")}
                      meta={metadata}
                      form={state.form}
                    />
                  </>
                )}
              </Card>
            </ResponsiveInspector>
          </div>
          {tab === "form" && (
            <FormDesigner value={state.form} onChange={model.updateForm} />
          )}
          {tab === "settings" && (
            <FlowSettingsPanel
              value={state.settings}
              onChange={model.updateSettings}
              form={state.form}
              roles={directory.roles}
            />
          )}
        </>
      )}
      <Dialog
        open={publishOpen}
        onOpenChange={setPublishOpen}
        title="发布新版本"
        footer={
          <Button
            variant="primary"
            busy={busy}
            onClick={() => void run(publish, "新版本已发布")}
          >
            确认发布
          </Button>
        }
      >
        <p>新申请使用新版本，进行中的审批保持绑定原版本。</p>
        <Field label="发布说明">
          <Textarea
            aria-label="发布说明"
            value={note}
            onChange={(event) => setNote(event.target.value)}
          />
        </Field>
      </Dialog>
      <Dialog
        open={!!preview}
        onOpenChange={(open) => {
          if (!open) setPreview(null);
        }}
        title="流程预览"
        wide
      >
        {preview && <FlowViewer definition={preview} />}
      </Dialog>
      {simulation && (
        <DesignerSimulation
          open
          onOpenChange={(open) => {
            if (!open) setSimulation(null);
          }}
          {...simulation}
        />
      )}
      <ConfirmDialog
        open={blocker.state === "blocked"}
        onOpenChange={(open) => {
          if (!open && blocker.state === "blocked") blocker.reset();
        }}
        title="离开设计器"
        confirm={() => {
          if (blocker.state === "blocked") blocker.proceed();
        }}
      >
        有未保存的修改，确认离开？
      </ConfirmDialog>
    </div>
  );
}
