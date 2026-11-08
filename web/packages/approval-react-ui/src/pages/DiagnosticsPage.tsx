import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../components/shadcn/accordion";
import { Link } from "react-router";
import { CheckCircle2, AlertTriangle, RefreshCw } from "lucide-react";
import { PageHeader } from "../components/ui/PageHeader";
import {
  Alert,
  AlertTitle,
  AlertDescription,
} from "../components/shadcn/alert";
import { useApprovalClient, useApprovalUser } from "../client";
import { useAsyncData } from "../hooks/useAsyncData";
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  Loading,
} from "../components/ui/primitives";
import { DataTable } from "../components/ui/DataTable";
import { findingLabel } from "../features/monitor/codeLabels";
import { Metrics } from "../components/ui/Metrics";
export default function DiagnosticsPage() {
  const { api } = useApprovalClient();
  const user = useApprovalUser();
  const data = useAsyncData(
    (signal) => api.diagnostics(signal),
    [],
    user.can("approval.diagnostics.read"),
  );
  if (!user.can("approval.diagnostics.read"))
    return <EmptyState text="没有运行诊断权限" />;
  return (
    <div className="ar-product-page">
      <PageHeader
        title="运行诊断"
        eyebrow="流程管理"
        description="检查运行状态、投递队列与数据完整性，定位需要处理的问题。"
        actions={
          <Button onClick={data.reload}>
            <RefreshCw data-icon="inline-start" />
            刷新诊断
          </Button>
        }
      />
      <ErrorState error={data.error} retry={data.reload} />
      {data.loading && !data.data ? (
        <Loading />
      ) : (
        data.data && (
          <>
            <Card title={data.data.ready ? "运行就绪" : "发现运行风险"}>
              <Alert>
                {data.data.ready ? <CheckCircle2 /> : <AlertTriangle />}
                <AlertTitle>
                  {data.data.ready ? "当前运行检查通过" : "有检查项需要关注"}
                </AlertTitle>
                <AlertDescription>
                  以下数据来自最近一次诊断快照，可刷新重新检查。
                </AlertDescription>
              </Alert>
              <Metrics
                items={[
                  { label: "运行实例", value: data.data.runningInstances },
                  { label: "失败投递", value: data.data.failedOutbox },
                  { label: "等待投递", value: data.data.pendingOutbox },
                  { label: "超期待办", value: data.data.overdueTasks },
                  {
                    label: "缺失业务占用",
                    value: data.data.missingBusinessSlots,
                  },
                  {
                    label: "失效业务占用",
                    value: data.data.staleBusinessSlots,
                  },
                ]}
              />
              {!data.data.dataAvailable && (
                <Alert variant="destructive">
                  <AlertDescription>当前诊断数据不可用</AlertDescription>
                </Alert>
              )}
              {data.data.modelDrift && (
                <Alert variant="destructive">
                  <AlertDescription>数据库模型存在漂移</AlertDescription>
                </Alert>
              )}
              {data.data.pendingMigrations > 0 && (
                <Alert variant="destructive">
                  <AlertDescription>
                    待应用迁移：{data.data.pendingMigrations}
                  </AlertDescription>
                </Alert>
              )}
              {data.data.assignmentSampleTruncated && (
                <p>处理人检查样本已截断，请结合流程监控查询。</p>
              )}
            </Card>
            <Card
              title="风险发现"
              description="按资源定位具体问题，使用对应入口查看并处理。"
            >
              <DataTable
                rows={data.data.findings}
                empty="未发现运行风险"
                rowKey={(row) => row.code + row.resourceId}
                columns={[
                  {
                    key: "code",
                    label: "问题类型",
                    render: (row) => (
                      <div className="ar-cell-stack">
                        <strong>{findingLabel(row.code)}</strong>
                        <code>{row.code}</code>
                      </div>
                    ),
                  },
                  {
                    key: "resource",
                    label: "资源",
                    render: (row) => row.resourceId,
                  },
                  {
                    key: "flow",
                    label: "流程",
                    render: (row) => row.definitionKey,
                  },
                  {
                    key: "action",
                    label: "处理",
                    render: (row) => (
                      <div className="ar-row">
                        {row.instanceId && (
                          <Link to={`../approvals/${row.instanceId}`}>
                            查看审批
                          </Link>
                        )}
                        {row.code.toLowerCase().includes("outbox") &&
                          user.can("approval.outbox.read") && (
                            <Link to="../outbox?status=Failed">查看投递</Link>
                          )}
                      </div>
                    ),
                  },
                ]}
                loading={data.loading}
                error={data.error}
              />
            </Card>
            <Card title="运行检查详情">
              <Accordion type="single" collapsible>
                <AccordionItem
                  value="content"
                  className="approval:group/disclosure"
                >
                  <AccordionTrigger>Worker 心跳与宿主诊断</AccordionTrigger>
                  <AccordionContent
                    forceMount
                    className="approval:h-auto approval:group-data-[state=closed]/disclosure:hidden"
                  >
                    <div className="approval:flex approval:flex-col approval:gap-4">
                      <pre style={{ whiteSpace: "pre-wrap" }}>
                        {JSON.stringify(
                          {
                            heartbeats: data.data.heartbeats,
                            contributions: data.data.contributions,
                          },
                          null,
                          2,
                        )}
                      </pre>
                    </div>
                  </AccordionContent>
                </AccordionItem>
              </Accordion>
              <div className="ar-row">
                {user.can("approval.instance.read-all") && (
                  <Link to="../monitor">处理超期流程</Link>
                )}
                {user.can("approval.outbox.read") && (
                  <Link to="../outbox?status=Failed">查看失败投递</Link>
                )}
              </div>
            </Card>
          </>
        )
      )}
    </div>
  );
}
