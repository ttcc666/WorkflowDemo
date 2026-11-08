import { Link } from "react-router";
import {
  Card,
  DataTable,
  ErrorState,
  Button,
  Metrics,
  FieldError,
  useApprovalClient,
  useApprovalUser,
  useAsyncData,
} from "@approval/react-ui";
import type { CertificateDiagnostics } from "@demo-shared/types";
export function CertificateDiagnosticsPanel() {
  const client = useApprovalClient();
  const user = useApprovalUser();
  const data = useAsyncData(
    (signal) =>
      client.http.get<CertificateDiagnostics>(
        "/api/approval/admin/certificates/diagnostics",
        signal,
      ),
    [],
    user.can("approval.diagnostics.read"),
  );
  if (!user.can("approval.diagnostics.read")) return null;
  return (
    <Card
      title="证书试点运行检查"
      description="检查审核任务、投递与岗位配置。"
      action={<Button onClick={data.reload}>刷新检查</Button>}
    >
      <ErrorState error={data.error} retry={data.reload} />
      {data.data && (
        <>
          <Metrics
            className="ar-metrics-four"
            items={[
              { label: "审核中", value: data.data.pendingApplications },
              { label: "投递失败", value: data.data.failedDeliveries },
              { label: "等待投递", value: data.data.dueDeliveries },
              { label: "超期待办", value: data.data.overdueTasks },
            ]}
          />
          {!data.data.database.ready && (
            <FieldError>{data.data.database.problems.join("；")}</FieldError>
          )}
          {data.data.truncated && <p>岗位检查仅展示最早的 100 条待办。</p>}
          <DataTable
            rows={data.data.unstaffedTasks}
            rowKey={(row) => row.taskId}
            empty="未发现缺少岗位处理人的任务"
            columns={[
              {
                key: "node",
                label: "节点",
                render: (row) => (
                  <Link
                    className="ar-text-link"
                    to={`../approvals/${row.instanceId}`}
                  >
                    {row.nodeName}
                  </Link>
                ),
              },
              { key: "problem", label: "问题", render: (row) => row.problem },
            ]} loading={data.loading} error={data.error}
          />
        </>
      )}
    </Card>
  );
}
