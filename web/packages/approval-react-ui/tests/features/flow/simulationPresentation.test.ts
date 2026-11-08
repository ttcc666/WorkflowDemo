import { expect, it } from "vitest";
import { simulationPresentation } from "../../../src/features/flow/simulationPresentation";

it.each([
  ["Blocked", "仅显示动作计划，未产生正式输出", "仅展示执行计划", "active"],
  ["Blocked", "没有可用审批人", "预演受阻", "active"],
  ["Failed", "脚本执行失败", "预演失败", "active"],
  ["RequiresDecision", null, "等待人工决策", "active"],
  ["Waiting", null, "预演等待中", "active"],
  ["Visited", null, "已预演", "done"],
  ["Skipped", null, "未预演", "skipped"],
])(
  "simulation %s with reason %s distinguishes preview status from an approval rejection",
  (status, blockReason, label, runStatus) => {
    expect(simulationPresentation({ status, blockReason })).toEqual({
      status: runStatus,
      label,
    });
  },
);
