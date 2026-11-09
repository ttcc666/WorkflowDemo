import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { FileText, Plus, RefreshCw, Upload, Search } from "lucide-react";
import { PageHeader } from "../components/ui/PageHeader";
import {
  InputGroup,
  InputGroupInput,
  InputGroupAddon,
} from "../components/shadcn/input-group";
import {
  formatDateTime,
  type DefinitionGovernanceSummary,
} from "@approval/shared";
import { useApprovalClient, useApprovalUser } from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "../components/CommandRecovery";
import {
  Badge,
  Button,
  Card,
  Dialog,
  ErrorState,
  Field,
  Input,
  Select,
} from "../components/ui/primitives";
import { DataTable } from "../components/ui/DataTable";
import { ImportDefinitionDialog } from "../features/definitions/ImportDefinitionDialog";
import { DefinitionVersionsDialog } from "../features/definitions/DefinitionVersionsDialog";

export default function DefinitionsPage() {
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const navigate = useNavigate();
  const [category, setCategory] = useState("");
  const [keyword, setKeyword] = useState("");
  const definitions = useAsyncData(
    (signal) =>
      api.governanceDefinitions(
        signal,
        category === "__none" ? undefined : category || undefined,
        category === "__none",
      ),
    [category],
    user.canViewDefinitions,
  );
  const categories = useAsyncData(
    (signal) => api.definitionCategories(signal),
    [],
    user.canViewDefinitions,
  );
  const [dialog, setDialog] = useState<"new" | "import" | "copy" | null>(null);
  const [copy, setCopy] = useState<DefinitionGovernanceSummary | null>(null);
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [versions, setVersions] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const intent = useCommandIntent("copy-definition", { permission: "approval.definition.design" });
  const filtered = (definitions.data ?? []).filter((item) =>
    (item.name + " " + item.key)
      .toLowerCase()
      .includes(keyword.trim().toLowerCase()),
  );
  function refresh() {
    definitions.reload();
    categories.reload();
  }
  function create() {
    if (!user.can("approval.definition.design")) return;
    if (
      !key.trim() ||
      !name.trim() ||
      definitions.data?.some((item) => item.key === key.trim())
    ) {
      setError(new Error("请填写名称和未使用的流程 Key"));
      return;
    }
    setDialog(null);
    navigate(
      `../designer/${encodeURIComponent(key.trim())}?name=${encodeURIComponent(name.trim())}`,
    );
  }
  async function copyDefinition() {
    if (!copy || !key.trim() || !name.trim() || busy || !user.can("approval.definition.design")) return;
    setBusy(true);
    setError(null);
    try {
      const result = await intent.run(
        copy.key,
        {
          targetKey: key.trim(),
          name: name.trim(),
          sourceDefinitionId: copy.activeDefinitionId ?? undefined,
        },
        (body, id) => api.copyDefinition(copy.key, body, id),
      );
      if (!intent.isCurrent()) return;
      setDialog(null);
      refresh();
      navigate("../designer/" + result.key);
    } catch (cause) {
      setError(cause);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="ar-product-page ar-definitions-page">
      <PageHeader
        title="流程定义"
        eyebrow="流程管理"
        description="管理流程草稿、发布版本与启用状态。已发布版本绑定运行中的审批。"
        actions={
          <div className="ar-row">
            <Button onClick={refresh}>
              <RefreshCw data-icon="inline-start" />
              刷新
            </Button>
            {user.can("approval.definition.design") && (
              <>
                <Button onClick={() => setDialog("import")}>
                  <Upload data-icon="inline-start" />
                  导入流程定义
                </Button>
                <Button
                  variant="primary"
                  onClick={() => {
                    setKey("");
                    setName("");
                    setDialog("new");
                  }}
                >
                  <Plus data-icon="inline-start" />
                  新建流程
                </Button>
              </>
            )}
          </div>
        }
      />
      <CommandRecovery intent={intent} busy={busy} recovered={refresh} />
      <ErrorState
        error={error || definitions.error || categories.error}
        retry={refresh}
      />
      <div className="ar-page-filter-toolbar">
        <Field label="搜索流程">
          <InputGroup>
            <InputGroupAddon>
              <Search />
            </InputGroupAddon>
            <InputGroupInput
              aria-label="流程关键词"
              placeholder="搜索流程名称或 Key"
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
            />
          </InputGroup>
        </Field>
        <Field label="流程分类">
          <Select
            aria-label="分类筛选"
            value={category}
            placeholder="全部分类"
            options={[
              { value: "__none", label: "未分类" },
              ...(categories.data ?? []).map((value) => ({
                value,
                label: value,
              })),
            ]}
            onValueChange={(nextValue) => setCategory(nextValue)}
          />
        </Field>
      </div>
      <Card
        title="流程列表"
        description={
          definitions.loading
            ? "正在加载流程定义…"
            : definitions.error
              ? "流程列表暂不可用"
              : "当前列表 " + filtered.length + " 个流程"
        }
      >
        <DataTable
          rows={filtered}
          rowKey={(item) => item.key}
          loading={definitions.loading}
          error={definitions.error}
          empty={
            keyword ? "没有匹配的流程，请调整名称或 Key" : "此分类暂无流程定义"
          }
          columns={[
            {
              key: "name",
              label: "流程名称 / Key",
              render: (item) => (
                <div className="ar-cell-title">
                  <FileText aria-hidden="true" />
                  <div>
                    <strong>{item.name}</strong>
                    <small>{item.key}</small>
                  </div>
                </div>
              ),
            },
            {
              key: "category",
              label: "分类",
              render: (item) => item.category || "未分类",
            },
            {
              key: "version",
              label: "版本",
              render: (item) => `v${item.lastVersion}`,
            },
            {
              key: "status",
              label: "状态",
              render: (item) => (
                <div className="ar-row">
                  <Badge status={item.isEnabled ? "Enabled" : "Disabled"}>
                    {item.isEnabled ? "启用" : "停用"}
                  </Badge>
                  {item.hasDraftChanges && <Badge status="Draft">有草稿</Badge>}
                </div>
              ),
            },
            {
              key: "time",
              label: "最后更新",
              render: (item) => formatDateTime(item.updatedAt),
            },
            {
              key: "action",
              label: "操作",
              render: (item) => (
                <div className="ar-row">
                  {user.can("approval.definition.design") && (
                    <Link
                      className="ar-text-link"
                      to={`../designer/${item.key}`}
                    >
                      设计草稿
                    </Link>
                  )}
                  <Button size="sm" onClick={() => setVersions(item.key)}>
                    版本 / 治理
                  </Button>
                  {user.can("approval.definition.design") && (
                    <Button
                      size="sm"
                      disabled={!item.activeDefinitionId}
                      onClick={() => {
                        setCopy(item);
                        setKey(item.key + "-copy");
                        setName(item.name + "（副本）");
                        setDialog("copy");
                      }}
                    >
                      复制
                    </Button>
                  )}
                </div>
              ),
            },
          ]}
        />
      </Card>
      {(dialog === "new" || dialog === "copy") && (
        <Dialog
          open
          onOpenChange={(open) => {
            if (!open) setDialog(null);
          }}
          title={dialog === "new" ? "新建流程" : "复制流程"}
          footer={
            <Button
              variant="primary"
              busy={busy}
              disabled={!name.trim() || !key.trim()}
              onClick={() =>
                dialog === "new" ? create() : void copyDefinition()
              }
            >
              确认{dialog === "new" ? "新建" : "复制"}
            </Button>
          }
        >
          <Field
            label="流程名称"
            required
            help="为申请人显示的名称，例如费用报销。"
          >
            <Input
              aria-label="流程名称"
              placeholder="例如：费用报销"
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          </Field>
          <Field
            label="流程 Key"
            required
            help="填写未使用的标识，例如 expense；创建后用于定位流程。"
          >
            <Input
              aria-label="流程 Key"
              placeholder="例如：expense"
              value={key}
              onChange={(event) => setKey(event.target.value)}
            />
          </Field>
          <ErrorState error={error} />
        </Dialog>
      )}
      {dialog === "import" && (
        <ImportDefinitionDialog
          close={() => setDialog(null)}
          imported={(key) => {
            setDialog(null);
            refresh();
            navigate("../designer/" + key);
          }}
        />
      )}
      {versions && (
        <DefinitionVersionsDialog
          flowKey={versions}
          close={() => setVersions(null)}
          changed={refresh}
        />
      )}
    </div>
  );
}
