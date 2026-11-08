import { Link } from "react-router";
import {
  Badge,
  Card,
  DataTable,
  ErrorState,
  Loading,
  useAsyncData,
  formatDateTime,
} from "@approval/react-ui";
import type { CertificateData } from "@demo-shared/types";
import {
  certificateFieldLabels,
  certTypeLabels,
  operationLabels,
  requestStatusLabels,
} from "@demo-shared/labels";
import { useCertificateApi } from "../api";

export function CertificateRequestPanel({
  businessKey,
}: {
  businessKey: string;
}) {
  const api = useCertificateApi();
  const detail = useAsyncData(
    (signal) => api.requestDetail(businessKey, signal),
    [businessKey],
  );
  if (!detail.data)
    return detail.error ? (
      <ErrorState error={detail.error} retry={detail.reload} />
    ) : (
      <Loading />
    );
  const request = detail.data.request;
  const rows = Object.entries(certificateFieldLabels).map(([field, label]) => {
    const key = (field[0]!.toLowerCase() +
      field.slice(1)) as keyof CertificateData;
    return {
      key,
      label,
      before:
        request.operation === "Create"
          ? "—"
          : !request.hasHistoricalBaseline
            ? "历史基线未记录"
            : (request.beforeSnapshot?.[key] ?? "—"),
      after: request.snapshot[key],
      changed: detail.data?.changes.some((change) => change.field === field),
    };
  });
  return (
    <Card
      title="证书申请内容"
      description="查看提交时的申请内容及对应的历史基线。"
    >
      <div className="ar-row">
        <Badge>{operationLabels[request.operation]}</Badge>
        <Badge status={request.status}>
          {requestStatusLabels[request.status]?.text}
        </Badge>
        <small className="ar-cell-muted">
          {formatDateTime(request.createdAt)}
        </small>
      </div>
      {request.reason && <p>原因：{request.reason}</p>}
      {!request.hasHistoricalBaseline && (
        <p>历史申请未记录提交前基线，不能还原当时差异。</p>
      )}
      <DataTable
        rows={rows}
        rowKey={(row) => row.key}
        columns={[
          { key: "field", label: "字段", render: (row) => row.label },
          {
            key: "before",
            label: "提交前",
            render: (row) =>
              row.key === "certType"
                ? (certTypeLabels[row.before] ?? row.before)
                : row.before,
          },
          {
            key: "after",
            label: "申请内容",
            render: (row) => (
              <strong
                style={{ color: row.changed ? "var(--primary)" : undefined }}
              >
                {row.key === "certType"
                  ? (certTypeLabels[row.after] ?? row.after)
                  : row.after}
              </strong>
            ),
          },
        ]}
      />
      <div className="ar-row">
        {detail.data.approval && (
          <Link
            className="ar-text-link"
            to={`../approvals/${detail.data.approval.id}`}
          >
            查看审批详情
          </Link>
        )}
        {request.canReapply && (
          <Link
            className="ar-text-link"
            to={`../certificates?reapply=${request.id}`}
          >
            修改后重新申请
          </Link>
        )}
      </div>
    </Card>
  );
}
