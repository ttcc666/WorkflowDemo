import { useEffect } from "react";
import { useSearchParams } from "react-router";
import { useApprovalUser } from "../client";
import { Card, EmptyState, Tabs } from "../components/ui/primitives";
import { PageHeader } from "../components/ui/PageHeader";
import {
  HandoverPanel,
  MonitorInstances,
  MonitorStatistics,
} from "../features/monitor/MonitorPanels";
export default function MonitorPage() {
  const user = useApprovalUser();
  const tabs = [
    {
      value: "instances",
      label: "全部流程",
      permission: "approval.instance.read-all",
    },
    {
      value: "statistics",
      label: "统计",
      permission: "approval.statistics.read",
    },
    {
      value: "handover",
      label: "离职交接",
      permission: "approval.task.reassign-bulk",
    },
  ].filter((tab) => user.can(tab.permission));
  const [params, setParams] = useSearchParams();
  const selected = params.get("tab");
  const tab = tabs.some((tab) => tab.value === selected)
    ? selected
    : tabs[0]?.value;
  useEffect(() => {
    if (!user.me || !tab || selected === tab) return;
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set("tab", tab);
        return next;
      },
      { replace: true },
    );
  }, [user.me, tab, selected, setParams]);
  if (!tab) return <EmptyState text="没有流程监控权限" />;
  return (
    <div className="ar-product-page">
      <PageHeader
        title="流程监控"
        eyebrow="流程管理"
        description="查询运行中的审批，查看处理统计与待办交接。"
      />
      <Tabs
        value={tab}
        onChange={(value) => {
          if (!tabs.some((item) => item.value === value)) return;
          setParams(
            (previous) => {
              const next = new URLSearchParams(previous);
              next.set("tab", value);
              return next;
            },
            { replace: true },
          );
        }}
        tabs={tabs}
      />
      {tab === "instances" ? (
        <Card
          title="实例列表"
          description="筛选审批状态、流程和日期，查看实例详情或进行授权管理。"
        >
          <MonitorInstances />
        </Card>
      ) : tab === "statistics" ? (
        <MonitorStatistics />
      ) : (
        <Card
          title="待办交接"
          description="先选择交出人员并核对待办，再指定接收人员与交接原因。"
        >
          <HandoverPanel />
        </Card>
      )}
    </div>
  );
}
