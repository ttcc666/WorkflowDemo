import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import {
  Badge,
  PageHeader,
  Button,
  Card,
  CommandRecovery,
  ConfirmDialog,
  DataTable,
  Dialog,
  ErrorState,
  Field,
  Input,
  Pagination,
  Textarea,
  formatDateTime,
  useApprovalUser,
  useAsyncData,
  useCommandIntent,
  usePagedList,
} from "@approval/react-ui";
import { FileBadge, Plus, RefreshCw, Search } from "lucide-react";
import type { Certificate } from "@demo-shared/types";
import {
  certificateStatusLabels,
  certTypeLabels,
  operationLabels,
  requestStatusLabels,
} from "@demo-shared/labels";
import { useCertificateApi } from "../api";
import { CertificateFormDialog } from "../features/CertificateFormDialog";
import { CertificateRequestPanel } from "../features/CertificateRequestPanel";
import { CertificateDiagnosticsPanel } from "../features/CertificateDiagnosticsPanel";
const effectiveStatusLabels = {
  Valid: "有效",
  NotYetValid: "未生效",
  Expired: "已过期",
  Revoked: "已注销",
};

export default function CertificatesPage() {
  const api = useCertificateApi();
  const user = useApprovalUser();
  const [params, setParams] = useSearchParams();
  const list = usePagedList(
    (query, signal) => api.list(query, signal),
    "cert_",
  );
  const [form, setForm] = useState<{
    certificate?: Certificate;
    source?: string;
  } | null>(null);
  const [history, setHistory] = useState<Certificate | null>(null);
  const [detail, setDetail] = useState<string | null>(null);
  const [revoke, setRevoke] = useState<Certificate | null>(null);
  const [reason, setReason] = useState("");
  const [reasonError, setReasonError] = useState("");
  const reasonInput = useRef<HTMLTextAreaElement>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const intent = useCommandIntent("certificate-revoke");
  const reapply = useAsyncData(
    (signal) => api.requestDetail(params.get("reapply")!, signal),
    [params.get("reapply")],
    !!params.get("reapply"),
  );
  useEffect(() => {
    let active = true;
    const request = reapply.data?.request;
    if (request?.canReapply) {
      if (request.operation === "Revoke" && request.certificateId) {
        void api.get(request.certificateId).then((certificate) => {
          if (!active) return;
          setRevoke(certificate);
          setReason(request.reason ?? "");
        }).catch(cause => { if (active) setError(cause); });
      } else
        setForm({
          certificate: request.certificateId
            ? ({
                ...request.snapshot,
                id: request.certificateId,
              } as Certificate)
            : undefined,
          source: request.id,
        });
    }
    return () => { active = false; };
  }, [reapply.data, api]);
  async function submitRevoke() {
    if (!revoke || busy) return;
    if (!reason.trim()) {
      setReasonError("请填写注销原因");
      reasonInput.current?.focus();
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await intent.run(
        revoke.id,
        { id: revoke.id, reason, source: params.get("reapply") ?? undefined },
        (body, key) => api.submitRevoke(body.id, body.reason, key, body.source),
      );
      setRevoke(null);
      setDetail(result.requestId);
      list.reload();
    } catch (cause) {
      setError(cause);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="ar-product-page ar-certificates-page">
      <PageHeader
        title="注册证业务"
        eyebrow="业务管理"
        description="查询证书状态与历史，发起登记、变更或注销申请。"
        actions={
          <>
            <Button onClick={list.reload}>
              <RefreshCw data-icon="inline-start" />
              刷新
            </Button>
            {user.can("approval.instance.start") && (
              <Button variant="primary" onClick={() => setForm({})}>
                <Plus data-icon="inline-start" />
                申请登记注册证
              </Button>
            )}
          </>
        }
      />
      <CommandRecovery intent={intent} busy={busy} recovered={list.reload} />
      <ErrorState
        error={error || list.error || reapply.error}
        retry={list.reload}
      />
      <form
        className="ar-page-filter-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          list.search();
        }}
      >
        <Field label="搜索证书">
          <Input
            aria-label="证书关键词"
            placeholder="编号 / 名称 / 持有人"
            value={list.filters.keyword}
            onChange={(event) =>
              list.setFilters({ ...list.filters, keyword: event.target.value })
            }
          />
        </Field>
        <div className="ar-row">
          <Button type="submit" variant="primary">
            <Search data-icon="inline-start" />
            搜索证书
          </Button>
          <Button onClick={list.reset}>重置</Button>
        </div>
      </form>
      <Card
        title="证书列表"
        description="有效状态和可用操作由服务端提供，审核期间保留当前生效证书。"
      >
        <DataTable
          rows={list.items}
          rowKey={(row) => row.id}
          loading={list.loading}
          columns={[
            {
              key: "certificate",
              label: "注册证",
              render: (row) => (
                <div className="ar-cell-title">
                  <FileBadge aria-hidden="true" />
                  <div>
                    <strong>{row.name}</strong>
                    <small>{row.certNo}</small>
                  </div>
                </div>
              ),
            },
            { key: "holder", label: "持有人", render: (row) => row.holder },
            {
              key: "type",
              label: "类型",
              render: (row) => certTypeLabels[row.certType] ?? row.certType,
            },
            {
              key: "dates",
              label: "有效期",
              render: (row) => (
                <div className="ar-cell-stack">
                  <span>{row.issueDate}</span>
                  <small className="ar-cell-muted">至 {row.expireDate}</small>
                </div>
              ),
            },
            {
              key: "status",
              label: "状态",
              render: (row) => (
                <div className="ar-cell-stack">
                  <Badge
                    status={
                      row.effectiveStatus === "Expired" ||
                      row.effectiveStatus === "NotYetValid"
                        ? "Draft"
                        : row.status
                    }
                  >
                    {effectiveStatusLabels[row.effectiveStatus] ??
                      certificateStatusLabels[row.status]?.text}
                  </Badge>
                  {row.pendingRequestId && (
                    <Badge status="Reviewing">有审核中申请</Badge>
                  )}
                </div>
              ),
            },
            {
              key: "actions",
              label: "操作",
              render: (row) => (
                <div className="ar-row">
                  <Button size="sm" onClick={() => setHistory(row)}>
                    详情 / 历史
                  </Button>
                  {row.pendingRequestId && (
                    <Button
                      size="sm"
                      onClick={() => setDetail(row.pendingRequestId)}
                    >
                      当前申请
                    </Button>
                  )}
                  {row.canManage && !row.pendingRequestId && (
                    <>
                      <Button
                        size="sm"
                        onClick={() => setForm({ certificate: row })}
                      >
                        申请变更
                      </Button>
                      <Button
                        size="sm"
                        variant="danger"
                        disabled={row.status === "Revoked"}
                        onClick={() => {
                          setRevoke(row);
                          setReason("");
                          setReasonError("");
                        }}
                      >
                        申请注销
                      </Button>
                    </>
                  )}
                </div>
              ),
            },
          ]}
          error={list.error}
        />
        <Pagination
          page={list.page}
          pageSize={list.pageSize}
          total={list.total}
          onChange={list.paginate}
          loading={list.loading}
        />
      </Card>
      <CertificateDiagnosticsPanel />
      {form && (
        <CertificateFormDialog
          certificateId={form.certificate?.id}
          initial={form.certificate ?? reapply.data?.request.snapshot}
          sourceRequestId={form.source}
          close={() => setForm(null)}
          submitted={(id) => {
            setForm(null);
            setDetail(id);
            list.reload();
            setParams((previous) => {
              const next = new URLSearchParams(previous);
              next.delete("reapply");
              return next;
            });
          }}
        />
      )}
      {history && (
        <CertificateHistoryDialog
          open={!detail}
          certificate={history}
          close={() => setHistory(null)}
          select={setDetail}
        />
      )}
      <Dialog
        open={!!detail}
        onOpenChange={(open) => {
          if (!open) setDetail(null);
        }}
        title="证书申请详情"
        wide
      >
        {detail && <CertificateRequestPanel businessKey={detail} />}
      </Dialog>
      <ConfirmDialog
        open={!!revoke}
        onOpenChange={(open) => {
          if (!open && !busy) setRevoke(null);
        }}
        title="申请注销注册证"
        busy={busy}
        confirm={() => void submitRevoke()}
      >
        <Field label="注销原因" required error={reasonError || undefined}>
          <Textarea
            ref={reasonInput}
            aria-label="注销原因"
            value={reason}
            onChange={(event) => {
              setReason(event.target.value);
              setReasonError("");
            }}
          />
        </Field>
        <p>审批通过后注销，当前证书在审批期间保持原状态。</p>
        <ErrorState error={error} />
      </ConfirmDialog>
    </div>
  );
}
function CertificateHistoryDialog({
  open,
  certificate,
  close,
  select,
}: {
  open: boolean;
  certificate: Certificate;
  close: () => void;
  select: (id: string) => void;
}) {
  const api = useCertificateApi();
  const [page, setPage] = useState(1);
  const data = useAsyncData(
    (signal) => api.requests(certificate.id, { page, pageSize: 20 }, signal),
    [certificate.id, page],
  );
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) close();
      }}
      title={certificate.certNo + " · 证书详情 / 历史"}
      wide
    >
      <dl className="ar-summary">
        {(
          [
            ["certNo", "注册证编号"],
            ["name", "注册证名称"],
            ["holder", "持有人"],
            ["certType", "证书类型"],
            ["issueDate", "发证日期"],
            ["expireDate", "到期日期"],
          ] as const
        ).map(([key, label]) => (
          <div key={key}>
            <dt>{label}</dt>
            <dd>
              {key === "certType"
                ? (certTypeLabels[certificate[key]] ?? certificate[key])
                : certificate[key]}
            </dd>
          </div>
        ))}
      </dl>
      <ErrorState error={data.error} retry={data.reload} />
      <DataTable
        rows={data.data?.items ?? []}
        rowKey={(row) => row.id}
        loading={data.loading}
        columns={[
          {
            key: "operation",
            label: "申请类型",
            render: (row) => operationLabels[row.operation],
          },
          {
            key: "status",
            label: "状态",
            render: (row) => (
              <Badge status={row.status}>
                {requestStatusLabels[row.status]?.text}
              </Badge>
            ),
          },
          { key: "actor", label: "申请人", render: (row) => row.applicantId },
          {
            key: "time",
            label: "申请时间",
            render: (row) => formatDateTime(row.createdAt),
          },
          {
            key: "detail",
            label: "详情",
            render: (row) => (
              <Button size="sm" onClick={() => select(row.id)}>
                查看申请
              </Button>
            ),
          },
        ]}
        error={data.error}
      />
      <Pagination
        page={page}
        pageSize={20}
        total={data.data?.total}
        onChange={setPage}
        loading={data.loading}
      />
    </Dialog>
  );
}
