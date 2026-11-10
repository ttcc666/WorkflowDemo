import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";
import { useState } from "react";
import { Link } from "react-router";
import {
  compareDefinitionVersions,
  definitionFileName,
  formatDateTime,
  type ApprovalDefinition,
  type FlowDefinition,
} from "@approval/shared";
import { useApprovalClient, useApprovalUser } from "../../client";
import { useAsyncData } from "../../hooks/useAsyncData";
import { useCommandIntent } from "../../hooks/useCommandIntent";
import { CommandRecovery } from "../../components/CommandRecovery";
import {
  Button,
  ConfirmDialog,
  Dialog,
  ErrorState,
  Field,
  Pagination,
  Select,
  Tabs,
  Textarea,
} from "../../components/ui/primitives";
import { DataTable } from "../../components/ui/DataTable";
import { FlowViewer } from "../flow/FlowViewer";
const areaLabels: Record<string, string> = { form: "表单", node: "流程节点", nodes: "流程节点", flow: "流程配置", settings: "发起设置", notifications: "通知规则" };

export function DefinitionVersionsDialog({
  flowKey,
  close,
  changed,
}: {
  flowKey: string;
  close: () => void;
  changed: () => void;
}) {
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const intent = useCommandIntent("definition-governance", { canExecute: scope => {
    const permission = scope.startsWith("enabled:") ? "approval.definition.disable" : "approval.definition.publish";
    return can(permission) && user.can(permission);
  } });
  const governance = useAsyncData(
    (signal) => api.definitionGovernance(flowKey, signal),
    [flowKey],
  );
  const versions = useAsyncData(
    (signal) => api.versions(flowKey, signal),
    [flowKey],
  );
  const [page, setPage] = useState(1);
  const audit = useAsyncData(
    (signal) => api.definitionAudit(flowKey, page, signal),
    [flowKey, page],
  );
  const [tab, setTab] = useState("versions");
  const [version, setVersion] = useState("");
  const [before, setBefore] = useState("");
  const [after, setAfter] = useState("");
  const [note, setNote] = useState("");
  const [confirm, setConfirm] = useState<"enabled" | "select" | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [preview, setPreview] = useState<FlowDefinition | null>(null);
  const [highlighted, setHighlighted] = useState<string>();
  const selected = (versions.data ?? []).find((item) => item.id === version);
  const can = (permission: string) =>
    governance.data?.capabilities[permission] === true;
  function refresh() {
    governance.reload();
    versions.reload();
    audit.reload();
    changed();
  }
  async function govern() {
    const current = governance.data;
    const permission = confirm === "enabled" ? "approval.definition.disable" : "approval.definition.publish";
    if (!current || !confirm || busy || !can(permission) || !user.can(permission) || (confirm === "select" && !selected)) return;
    setBusy(true);
    setError(null);
    try {
      if (confirm === "enabled")
        await intent.run(
          `enabled:${flowKey}`,
          {
            enabled: !current.isEnabled,
            expectedRevision: current.revision,
            changeNote: note.trim(),
          },
          (body, id) => api.setDefinitionEnabled(flowKey, body, id),
        );
      else
        await intent.run(
          `select:${flowKey}`,
          {
            definitionId: version,
            expectedRevision: current.revision,
            changeNote: note.trim(),
          },
          (body, id) => api.selectDefinitionVersion(flowKey, body, id),
        );
      setConfirm(null);
      refresh();
    } catch (cause) {
      setError(cause);
      refresh();
    } finally {
      setBusy(false);
    }
  }
  async function download(item: ApprovalDefinition) {
    try {
      const blob = await api.exportDefinition(item.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = definitionFileName(flowKey, item.version);
      link.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (cause) {
      setError(cause);
    }
  }
  const base = (versions.data ?? []).find((item) => item.id === before);
  const target = (versions.data ?? []).find((item) => item.id === after);
  let comparison: ReturnType<typeof compareDefinitionVersions> | undefined;
  try {
    if (base && target) comparison = compareDefinitionVersions(base, target);
  } catch {}
  const options = (versions.data ?? []).map((item) => ({
    value: item.id,
    label: `v${item.version} · ${formatDateTime(item.createdAt)}`,
  }));
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busy) close();
      }}
      title={`${flowKey} · 版本与治理`}
      wide
    >
      <CommandRecovery intent={intent} busy={busy} recovered={refresh} />
      <ErrorState
        error={error || governance.error || versions.error || audit.error}
        retry={refresh}
      />
      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          { value: "versions", label: "版本" },
          { value: "compare", label: "版本对比" },
          { value: "governance", label: "治理" },
          { value: "audit", label: "审计" },
        ]}
      />
      {tab === "versions" && (
        <DataTable
          rows={versions.data ?? []}
          rowKey={(item) => item.id}
          loading={versions.loading}
          columns={[
            {
              key: "version",
              label: "发布版本",
              render: (item) => `v${item.version}`,
            },
            {
              key: "time",
              label: "发布时间",
              render: (item) => formatDateTime(item.createdAt),
            },
            {
              key: "action",
              label: "操作",
              render: (item) => (
                <div className="ar-row">
                  <Button
                    onClick={() => {
                      try {
                        setPreview(JSON.parse(item.definitionJson));
                      } catch (cause) {
                        setError(cause);
                      }
                    }}
                  >
                    查看详情
                  </Button>
                  {user.can("approval.definition.design") && (
                    <Link to={`../designer/${flowKey}?from=${item.id}`}>
                      基于此版编辑
                    </Link>
                  )}
                  <Button onClick={() => void download(item)}>导出</Button>
                </div>
              ),
            },
          ]}
          error={versions.error}
        />
      )}
      {tab === "compare" && (
        <div className="ar-stack">
          <div className="ar-row">
            <Select
              aria-label="基准版本"
              value={before}
              options={options}
              onValueChange={(nextValue) => setBefore(nextValue)}
            />
            <Button
              onClick={() => {
                setBefore(after);
                setAfter(before);
              }}
            >
              交换版本
            </Button>
            <Select
              aria-label="目标版本"
              value={after}
              options={options}
              onValueChange={(nextValue) => setAfter(nextValue)}
            />
          </div>
          {comparison && (
            <>
              <p>
                节点新增 {comparison.nodes.added} · 删除{" "}
                {comparison.nodes.removed} · 修改 {comparison.nodes.modified} ·
                移动 {comparison.nodes.moved}
              </p>
              <DataTable
                rows={comparison.changes}
                rowKey={(item) => `${item.area}:${item.path}`}
                columns={[
                  {
                    key: "path",
                    label: "变化路径",
                    render: (item) => <div className="ar-cell-stack"><strong>{areaLabels[item.area] ?? item.area}变更</strong><code>{item.path}</code></div>,
                  },
                  {
                    key: "before",
                    label: "修改前",
                    render: (item) => (
                      <code>{JSON.stringify(item.before)}</code>
                    ),
                  },
                  {
                    key: "after",
                    label: "修改后",
                    render: (item) => <code>{JSON.stringify(item.after)}</code>,
                  },
                  {
                    key: "locate",
                    label: "定位",
                    render: (item) =>
                      item.nodeId ? (
                        <Button
                          onClick={() => {
                            setHighlighted(item.nodeId);
                            setPreview(comparison!.after);
                          }}
                        >
                          查看节点 {item.nodeId}
                        </Button>
                      ) : (
                        areaLabels[item.area] ?? item.area
                      ),
                  },
                ]}
              />
            </>
          )}
        </div>
      )}
      {tab === "governance" && (
        <div className="ar-stack">
          <p>
            当前{governance.data?.isEnabled ? "已启用" : "已停用"} · 最新 v
            {governance.data?.lastVersion}
          </p>
          <Field label="治理变更说明">
            <Textarea
              aria-label="治理变更说明"
              value={note}
              maxLength={1000}
              onChange={(event) => setNote(event.target.value)}
            />
          </Field>
          {can("approval.definition.disable") && (
            <Button
              disabled={busy || !governance.data?.lastVersion}
              onClick={() => setConfirm("enabled")}
            >
              {governance.data?.isEnabled ? "停用流程" : "启用流程"}
            </Button>
          )}
          {can("approval.definition.publish") && (
            <>
              <Select
                aria-label="选用历史版本"
                value={version}
                options={options}
                onValueChange={(nextValue) => setVersion(nextValue)}
              />
              <Button
                disabled={!selected || busy}
                onClick={() => setConfirm("select")}
              >
                选用此版本
              </Button>
            </>
          )}
        </div>
      )}
      {tab === "audit" && (
        <>
          <DataTable
            rows={audit.data?.items ?? []}
            rowKey={(item) => item.id}
            columns={[
              { key: "action", label: "动作", render: (item) => item.action },
              { key: "actor", label: "操作人", render: (item) => item.actorId },
              {
                key: "time",
                label: "时间",
                render: (item) => formatDateTime(item.createdAt),
              },
              {
                key: "note",
                label: "变更说明",
                render: (item) => item.changeNote,
              },
            ]}
            loading={audit.loading}
            error={audit.error}
          />
          <Pagination
            page={page}
            pageSize={20}
            total={audit.data?.total}
            onChange={setPage}
            loading={audit.loading}
          />
        </>
      )}
      <ConfirmDialog
        open={!!confirm}
        onOpenChange={(open) => {
          if (!open) setConfirm(null);
        }}
        title="确认治理操作"
        confirm={() => void govern()}
        busy={busy}
        confirmDisabled={!can(confirm === "enabled" ? "approval.definition.disable" : "approval.definition.publish") || !user.can(confirm === "enabled" ? "approval.definition.disable" : "approval.definition.publish") || (confirm === "select" && !selected)}
      >
        仅影响新申请，进行中的审批保持原发布版本。
      </ConfirmDialog>
      <Dialog
        open={!!preview}
        onOpenChange={(open) => {
          if (!open) setPreview(null);
        }}
        title="版本详情"
        wide
      >
        {preview && (
          <>
            <FlowViewer definition={preview} highlightedNodeId={highlighted} />
            <Accordion type="single" collapsible>
              <AccordionItem
                value="content"
                className="approval:group/disclosure"
              >
                <AccordionTrigger>定义 JSON</AccordionTrigger>
                <AccordionContent
                  forceMount
                  className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
                >
                  <div className="approval:flex approval:flex-col approval:gap-4">
                    <pre>{JSON.stringify(preview, null, 2)}</pre>
                  </div>
                </AccordionContent>
              </AccordionItem>
            </Accordion>
          </>
        )}
      </Dialog>
    </Dialog>
  );
}
