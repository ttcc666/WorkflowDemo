import type { ApprovalSimulationResult } from "@approval/shared";

export function simulationPresentation(
  step: Pick<
    ApprovalSimulationResult["steps"][number],
    "status" | "blockReason"
  >,
) {
  switch (step.status) {
    case "Visited":
      return { status: "done" as const, label: "已预演" };
    case "RequiresDecision":
      return { status: "active" as const, label: "等待人工决策" };
    case "Waiting":
      return { status: "active" as const, label: "预演等待中" };
    case "Blocked":
      return {
        status: "active" as const,
        label: step.blockReason?.includes("计划")
          ? "仅展示执行计划"
          : "预演受阻",
      };
    case "Failed":
      return { status: "active" as const, label: "预演失败" };
    default:
      return { status: "skipped" as const, label: "未预演" };
  }
}
