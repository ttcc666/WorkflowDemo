import { ItemMedia } from "../components/shadcn/item";
import { useState } from "react";
import { Link } from "react-router";
import {
  ArrowUpRight,
  FileText,
  Wallet,
  CalendarDays,
  ShoppingCart,
  Search,
  RefreshCw,
  FilePenLine,
} from "lucide-react";
import { PageHeader } from "../components/ui/PageHeader";
import {
  Card as UiCard,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardAction,
} from "../components/shadcn/card";
import {
  InputGroup,
  InputGroupInput,
  InputGroupAddon,
} from "../components/shadcn/input-group";
import {
  formatDateTime,
  parseFormValues,
  type DraftSummary,
} from "@approval/shared";
import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../components/shadcn/accordion";
import { useApprovalClient, useApprovalUser } from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import { useCommandIntent } from "../hooks/useCommandIntent";
import { CommandRecovery } from "../components/CommandRecovery";
import {
  Button,
  Badge,
  Card,
  ConfirmDialog,
  Dialog,
  EmptyState,
  ErrorState,
  Select,
  Tabs,
  Field,
  Loading,
} from "../components/ui/primitives";
import { DataTable } from "../components/ui/DataTable";

export default function StartCenterPage() {
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const flows = useAsyncData(
    (signal) => api.startableForms(signal),
    [user.can("approval.instance.start")],
    user.formsEnabled && user.can("approval.instance.start"),
  );
  const drafts = useAsyncData(
    (signal) => api.drafts(signal),
    [user.can("approval.draft.read")],
    user.formsEnabled && user.can("approval.draft.read"),
  );
  const intent = useCommandIntent("draft-delete", { permission: "approval.draft.delete" });
  const [keyword, setKeyword] = useState("");
  const [category, setCategory] = useState("");
  const [view, setView] = useState("applications");
  const [selected, setSelected] = useState<DraftSummary | null>(null);
  const [previewDraft, setPreviewDraft] = useState<DraftSummary | null>(null);
  const previewData = useAsyncData(
    (signal) => api.draft(previewDraft!.id, signal),
    [previewDraft?.id],
    !!previewDraft && user.formsEnabled && user.can("approval.draft.read"),
  );
  const summaryValues = Object.values(
    parseFormValues(previewData.data?.formDataJson),
  )
    .filter(
      (value) =>
        (typeof value === "string" && value.trim()) ||
        typeof value === "number",
    )
    .slice(0, 6)
    .map((value) =>
      String(value).length > 120
        ? String(value).slice(0, 120) + "…"
        : String(value),
    );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const matched = (flows.data ?? []).filter(
    (flow) =>
      `${flow.name} ${flow.description ?? ""}`
        .toLowerCase()
        .includes(keyword.trim().toLowerCase()) &&
      (!category || (flow.category || "其他") === category),
  );
  async function remove() {
    if (!selected || busy || !user.can("approval.draft.delete")) return;
    setBusy(true);
    setError(null);
    try {
      await intent.run(selected.id, {}, (_, id) =>
        api.deleteDraft(selected.id, id),
      );
      setSelected(null);
      drafts.reload();
    } catch (cause) {
      setError(cause);
    } finally {
      setBusy(false);
    }
  }
  if (!user.me) return <Loading />;
  if (!user.formsEnabled) return <EmptyState text="宿主未启用动态表单" />;
  const views = [
    ...(user.can("approval.instance.start")
      ? [{ value: "applications", label: "选择申请" }]
      : []),
    ...(user.can("approval.draft.read")
      ? [
          {
            value: "drafts",
            label: "我的草稿（" + (drafts.data?.length ?? "—") + "）",
          },
        ]
      : []),
  ];
  const active = views.some((item) => item.value === view)
    ? view
    : views[0]?.value;
  const flowIcons: Record<string, typeof FileText> = {
    expense: Wallet,
    leave: CalendarDays,
    purchase: ShoppingCart,
  };
  return (
    <div className="ar-product-page ar-start-center">
      <PageHeader
        title="发起申请"
        eyebrow="我的工作空间"
        description="选择所需申请，或从已保存的草稿继续填写。"
        actions={
          <Button
            onClick={() => {
              flows.reload();
              drafts.reload();
            }}
          >
            <RefreshCw data-icon="inline-start" />
            刷新
          </Button>
        }
      />
      <CommandRecovery
        intent={intent}
        busy={busy}
        recovered={() => {
          setError(null);
          setSelected(null);
          drafts.reload();
        }}
      />
      <ErrorState
        error={error || flows.error || drafts.error}
        retry={() => {
          flows.reload();
          drafts.reload();
        }}
      />
      {active ? (
        <Tabs value={active} onChange={setView} tabs={views} />
      ) : (
        <EmptyState text="没有发起审批或查看草稿权限" />
      )}
      {active === "applications" && (
        <>
          <div className="ar-page-filter-toolbar">
            <Field label="搜索流程">
              <InputGroup>
                <InputGroupAddon>
                  <Search />
                </InputGroupAddon>
                <InputGroupInput
                  aria-label="搜索流程"
                  placeholder="按流程名称或说明搜索"
                  value={keyword}
                  onChange={(event) => setKeyword(event.target.value)}
                />
              </InputGroup>
            </Field>
            <Field label="申请分类">
              <Select
                aria-label="申请分类"
                placeholder="全部分类"
                value={category}
                options={[
                  ...new Set(
                    (flows.data ?? []).map((flow) => flow.category || "其他"),
                  ),
                ].map((value) => ({ value, label: value }))}
                onValueChange={(nextValue) => setCategory(nextValue)}
              />
            </Field>
          </div>
          {flows.loading ? (
            <Loading />
          ) : !matched.length ? (
            <EmptyState
              text={
                keyword || category
                  ? "没有匹配的申请，请调整搜索或分类"
                  : "没有可发起的流程"
              }
            />
          ) : (
            <section className="ar-start-category">
              <div className="ar-section-heading">
                <h2>{category || "可发起申请"}</h2>
                <small>{matched.length} 个申请</small>
              </div>
              <div className="ar-flow-cards">
                {matched.map((flow) => {
                  const Icon = flowIcons[flow.key] ?? FileText;
                  return (
                    <UiCard key={flow.key} className="ar-template-card">
                      <CardHeader>
                        <CardTitle>
                          <Link to={"../start/" + flow.key}>{flow.name}</Link>
                        </CardTitle>
                        <CardDescription>
                          {flow.description && flow.description.length > 80
                            ? flow.description.slice(0, 80) + "…"
                            : flow.description || "填写申请信息后提交审批。"}
                        </CardDescription>
                        <CardAction>
                          <ItemMedia
                            variant="icon"
                            className="ar-template-icon"
                          >
                            <Icon aria-hidden="true" />
                          </ItemMedia>
                        </CardAction>
                      </CardHeader>
                      <CardContent>
                        {flow.description && flow.description.length > 80 && (
                          <Accordion type="single" collapsible>
                            <AccordionItem value="description">
                              <AccordionTrigger>
                                查看完整申请说明
                              </AccordionTrigger>
                              <AccordionContent>
                                {flow.description}
                              </AccordionContent>
                            </AccordionItem>
                          </Accordion>
                        )}
                        <div className="ar-template-footer">
                          <Badge>{flow.category || "其他"}</Badge>
                          <Link
                            className="ar-template-entry"
                            aria-label={`填写${flow.name}申请`}
                            to={"../start/" + flow.key}
                          >
                            填写申请
                            <ArrowUpRight aria-hidden="true" />
                          </Link>
                        </div>
                      </CardContent>
                    </UiCard>
                  );
                })}
              </div>
            </section>
          )}
        </>
      )}
      {active === "drafts" && (
        <Card
          title="我的草稿"
          description="草稿仅你可见，可继续编辑后提交审批。"
        >
          <DataTable
            rows={drafts.data ?? []}
            rowKey={(draft) => draft.id}
            loading={drafts.loading}
            error={drafts.error}
            empty="暂无草稿，可先选择申请开始填写"
            columns={[
              {
                key: "flow",
                label: "申请类型",
                render: (draft) => (
                  <div className="ar-cell-title">
                    <FilePenLine aria-hidden="true" />
                    <strong>{draft.definitionName}</strong>
                  </div>
                ),
              },
              {
                key: "comment",
                label: "申请说明",
                render: (draft) => draft.comment || "—",
              },
              {
                key: "time",
                label: "最后保存",
                render: (draft) => formatDateTime(draft.updatedAt),
              },
              {
                key: "action",
                label: "操作",
                render: (draft) => (
                  <div className="ar-row">
                    <Button size="sm" onClick={() => setPreviewDraft(draft)}>
                      查看摘要
                    </Button>
                    <Link
                      className="ar-text-link"
                      to={
                        "../start/" + draft.definitionKey + "?draft=" + draft.id
                      }
                    >
                      继续编辑
                    </Link>
                    {user.can("approval.draft.delete") && (
                      <Button
                        size="sm"
                        variant="danger"
                        onClick={() => setSelected(draft)}
                      >
                        删除草稿
                      </Button>
                    )}
                  </div>
                ),
              },
            ]}
          />
        </Card>
      )}
      <Dialog
        open={!!previewDraft}
        onOpenChange={(open) => {
          if (!open) setPreviewDraft(null);
        }}
        title="草稿摘要"
        footer={
          previewDraft && (
            <Link
              className="ar-text-link"
              to={
                "../start/" +
                previewDraft.definitionKey +
                "?draft=" +
                previewDraft.id
              }
            >
              继续编辑完整表单
            </Link>
          )
        }
      >
        <ErrorState error={previewData.error} retry={previewData.reload} />
        {previewData.loading ? (
          <Loading />
        ) : (
          previewData.data && (
            <Card
              title={previewDraft?.definitionName}
              description={
                "最后保存：" + formatDateTime(previewData.data.updatedAt)
              }
            >
              {previewData.data.comment && <p>{previewData.data.comment}</p>}
              {summaryValues.length ? (
                summaryValues.map((value, index) => <p key={index}>{value}</p>)
              ) : (
                <p>暂无文本摘要，可继续编辑查看完整表单。</p>
              )}
            </Card>
          )
        )}
      </Dialog>
      <ConfirmDialog
        open={!!selected}
        onOpenChange={(open) => {
          if (!open) setSelected(null);
        }}
        title="删除草稿"
        busy={busy}
        confirm={() => void remove()}
      >
        删除「{selected?.definitionName}」的草稿？
      </ConfirmDialog>
    </div>
  );
}
