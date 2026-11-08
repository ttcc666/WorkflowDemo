const events: Record<string, string> = {
  Submit: "提交申请",
  TaskCreated: "创建审批任务",
  NodeEntered: "进入节点",
  Approved: "审批通过",
  Rejected: "审批驳回",
  Returned: "退回修改",
  Withdrawn: "撤回申请",
  Cancelled: "取消流程",
  Complete: "流程完成",
  Completed: "流程完成",
  NotificationRequested: "发送通知",
  TaskTransferred: "任务转交",
  TaskReassigned: "任务改派",
  Resubmitted: "重新提交",
  CcCreated: "创建抄送",
  TimeoutReminder: "超时提醒",
};
const targets: Record<string, string> = {
  Host: "宿主事件",
  Http: "HTTP 请求",
  Notification: "通知",
  Action: "流程动作",
};
const findings: Record<string, string> = {
  unavailable_assignee: "处理人不可用",
  missing_business_occupancy: "缺少业务占用",
  stale_business_occupancy: "业务占用失效",
  failed_outbox: "投递失败",
};
export const eventLabel = (code: string) => events[code] ?? code;
export const targetLabel = (code: string) => targets[code] ?? code;
export const findingLabel = (code: string) => findings[code] ?? code;
