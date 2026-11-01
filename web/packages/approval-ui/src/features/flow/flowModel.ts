import dagre from '@dagrejs/dagre'
import type { Edge, Node } from '@vue-flow/core'
import type { ApprovalInstance } from '../../api/types'
import type { FlowSettings, FormSchema } from '../forms/formModel'

// ───── 后端流程定义（与 Approval.Core.Definitions.FlowDefinition 对应） ─────

export type NodeType = 'Start' | 'Approval' | 'Cc' | 'Condition' | 'Action' | 'Parallel' | 'Join' | 'End'
export type ApproveMode = 'Any' | 'All' | 'Sequential'
export type EmptyApproverPolicy = 'Error' | 'AutoApprove'
export type NotifyEvent = 'Enter' | 'Approved' | 'Rejected' | 'Transferred' | 'Timeout' | 'Completed'
export type TimeoutAction = 'Remind' | 'AutoApprove' | 'AutoReject'
export type ActionMode = 'Sync' | 'Async'
/** 表单字段在节点的权限：可编辑（审批节点，表单审批的审批人同意时可修改）/ 隐藏；未设置为只读 */
export type FieldAccess = 'Editable' | 'Hidden'

export interface ApproverRule {
  type: string
  value: string
}

export interface ConditionBranch {
  priority: number
  expression?: string | null
  next: string
}

/** 通知规则：events 中任一事件发生时，按模板给 recipients 通过 channels 发送消息 */
export interface NotificationRule {
  events: NotifyEvent[]
  recipients: ApproverRule[]
  channels: string[]
  title?: string | null
  content?: string | null
}

/** 审批节点时限 */
export interface TimeoutPolicy {
  minutes: number
  action: TimeoutAction
  /** 提醒后仍未处理时，每隔多少分钟再催办一次；为空只提醒一次 */
  repeatMinutes?: number | null
  maxReminders?: number
}

/** 动作节点中的一个步骤 */
export interface ActionStep {
  type: string
  name?: string | null
  mode: ActionMode
  parameters: Record<string, string>
  /** 同步动作的输出写入该表单变量，供后续条件分支、模板使用 */
  outputVariable?: string | null
  continueOnError?: boolean
}

export interface FlowNodeDef {
  id: string
  name: string
  type: NodeType
  mode?: ApproveMode
  approvers?: ApproverRule[]
  branches?: ConditionBranch[]
  next?: string | null
  emptyApproverPolicy?: EmptyApproverPolicy
  /** 审批节点：驳回时可退回到之前的节点或发起人，默认 true */
  allowReturn?: boolean
  /** 审批节点：允许加签，默认 true */
  allowAddSign?: boolean
  allowTransfer?: boolean
  requireQualifiedAssignee?: boolean
  actions?: ActionStep[]
  notifications?: NotificationRule[]
  timeout?: TimeoutPolicy | null
  /** 表单字段权限（审批节点：可编辑 / 隐藏；抄送节点：隐藏），字段标识 → 权限 */
  fieldPermissions?: Record<string, FieldAccess>
  position?: { x: number; y: number } | null
}

export interface FlowDefinition {
  startNodeId: string
  /** 发起设置（发起中心、发起角色、标题模板） */
  settings?: FlowSettings
  /** 表单结构 */
  form?: FormSchema
  /** 流程级通知：对所有审批节点生效，以及流程结束 */
  notifications?: NotificationRule[]
  nodes: Record<string, FlowNodeDef>
}

// ───── 画布模型 ─────

/** 节点在流程实例中的状态（只读展示时使用） */
export type NodeRunStatus = 'done' | 'active' | 'rejected' | 'skipped'

export interface FlowNodeData {
  type: NodeType
  name: string
  mode: ApproveMode
  approvers: ApproverRule[]
  emptyApproverPolicy: EmptyApproverPolicy
  allowReturn: boolean
  allowAddSign: boolean
  allowTransfer: boolean
  requireQualifiedAssignee: boolean
  actions: ActionStep[]
  notifications: NotificationRule[]
  timeout: TimeoutPolicy | null
  fieldPermissions: Record<string, FieldAccess>
  status?: NodeRunStatus
}

/** 条件分支信息挂在从条件节点出发的连线上 */
export interface BranchData {
  priority: number
  /** null 表示默认分支 */
  expression: string | null
}

export type FlowNode = Node<FlowNodeData>
export type FlowEdge = Edge<BranchData | undefined>

export const FLOW_NODE_TYPE = 'flow-node'
export const NODE_WIDTH = 220
export const NODE_HEIGHT = 72
/** 从节点面板拖到画布时 dataTransfer 使用的类型 */
export const PALETTE_DRAG_TYPE = 'application/x-approval-node'

export const nodeTypeLabels: Record<NodeType, string> = {
  Start: '开始',
  Approval: '审批',
  Cc: '抄送',
  Condition: '条件分支',
  Action: '自动动作',
  Parallel: '并行分支',
  Join: '合并',
  End: '结束',
}

/** 条件节点、并行节点可以有多条出线 */
export const isMultiOutput = (type: NodeType | undefined) => type === 'Condition' || type === 'Parallel'

export const notifyEventLabels: Record<NotifyEvent, string> = {
  Enter: '到达节点',
  Approved: '同意',
  Rejected: '驳回',
  Transferred: '转交',
  Timeout: '超时提醒',
  Completed: '流程结束',
}

export const timeoutActionLabels: Record<TimeoutAction, string> = {
  Remind: '提醒 / 催办',
  AutoApprove: '自动同意',
  AutoReject: '自动驳回',
}

/** 各类节点可配置的通知事件；流程级规则对所有审批节点生效 */
export function notifyEventsFor(type: NodeType | 'Flow'): NotifyEvent[] {
  switch (type) {
    case 'Approval':
    case 'Flow':
      return ['Enter', 'Approved', 'Rejected', 'Transferred', 'Timeout', 'Completed']
    case 'Cc':
    case 'Action':
      return ['Enter']
    case 'End':
      return ['Completed']
    default:
      return []
  }
}

export const modeLabels: Record<ApproveMode, string> = {
  Any: '或签',
  All: '会签',
  Sequential: '依次审批',
}

/** 内置审批人类型的名称（未加载元数据时的兜底，如只读流程图）；设计器以后端元数据 approverTypes 为准 */
export const approverTypeLabels: Record<string, string> = {
  Role: '角色',
  User: '指定用户',
  FormField: '表单人员字段',
  Initiator: '发起人',
  DeptLeader: '部门负责人',
  Manager: '上级',
  ManagerChain: '逐级上级',
  // 仅通知接收人可用
  Assignees: '当前审批人',
  Approvers: '已审批过的人',
}

/** 不需要填写参数的接收人 / 审批人类型（兜底，元数据中 valueKind 为 None） */
export const valuelessApproverTypes = new Set(['Initiator', 'Assignees', 'Approvers'])

/** 组织架构类（参数为级数）的内置类型（兜底，元数据中 valueKind 为 Level） */
export const levelApproverTypes = new Set(['DeptLeader', 'Manager', 'ManagerChain'])

/** 解析级数参数：`2` 或 `2@payee`（以表单人员字段 payee 为基准），无效时返回 null */
export function parseLevel(value: string | null | undefined): { level: number; field: string | null } | null {
  const match = /^\s*(\d{1,2})\s*(?:@\s*([A-Za-z_][A-Za-z0-9_]*)\s*)?$/.exec(value ?? '')
  if (!match) return null
  const level = Number(match[1])
  return level >= 1 && level <= 10 ? { level, field: match[2] ?? null } : null
}

export function formatLevel(level: number, field: string | null) {
  return field ? `${level}@${field}` : String(level)
}

/** 组织架构类审批人的说明，如“直属上级”“第 2 级上级”“逐级审批至第 2 级上级（基准：payee）” */
export function levelRuleText(type: string, value: string, fieldLabel?: (key: string) => string) {
  const parsed = parseLevel(value)
  if (!parsed) return `${approverTypeLabels[type] ?? type}：${value || '未设置'}`
  const { level, field } = parsed
  const text =
    type === 'DeptLeader'
      ? level === 1 ? '所在部门负责人' : `上 ${level - 1} 级部门负责人`
      : type === 'ManagerChain'
        ? `逐级审批至第 ${level} 级上级`
        : level === 1 ? '直属上级' : `第 ${level} 级上级`
  return field ? `${text}（基准：${fieldLabel?.(field) ?? field}）` : text
}

/** 审批人规则的简短说明（节点卡片、摘要） */
export function approverRuleText(rule: ApproverRule, types?: { type: string; displayName: string; valueKind: string }[]) {
  const meta = types?.find((t) => t.type === rule.type)
  const label = meta?.displayName ?? approverTypeLabels[rule.type] ?? rule.type
  const kind = meta?.valueKind ?? (valuelessApproverTypes.has(rule.type) ? 'None' : levelApproverTypes.has(rule.type) ? 'Level' : 'Text')
  if (kind === 'None') return label
  if (kind === 'Level') return levelRuleText(rule.type, rule.value)
  return `${label}：${rule.value || '未设置'}`
}

export function branchLabel(data: BranchData | undefined) {
  if (!data) return ''
  if (data.expression === null) return '默认'
  return data.expression.trim() ? `${data.priority}. ${data.expression}` : `${data.priority}. (未填写条件)`
}

export function approverSummary(data: FlowNodeData) {
  const rules = data.approvers.map((r) => approverRuleText(r)).join('，')
  if (data.type === 'Approval') return `${modeLabels[data.mode]} · ${rules || '未设置审批人'}`
  if (data.type === 'Cc') return rules || '未设置抄送人'
  if (data.type === 'Action') return data.actions.map((a) => a.name || a.type).join('，') || '未配置动作'
  if (data.type === 'Parallel') return '所有分支同时进行'
  if (data.type === 'Join') return '等待全部分支完成'
  return ''
}

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T

// ───── 定义 → 画布 ─────

export function createEdge(source: string, target: string, data?: BranchData): FlowEdge {
  return {
    id: `${source}->${target}`,
    source,
    target,
    data,
    label: branchLabel(data),
    markerEnd: 'arrowclosed',
  }
}

export function toFlowElements(definition: FlowDefinition): { nodes: FlowNode[]; edges: FlowEdge[] } {
  const defs = Object.entries(definition.nodes).map(([key, node]) => ({ ...node, id: node.id || key }))

  const nodes: FlowNode[] = defs.map((def) => ({
    id: def.id,
    type: FLOW_NODE_TYPE,
    position: def.position ? { x: def.position.x, y: def.position.y } : { x: 0, y: 0 },
    deletable: def.type !== 'Start',
    data: {
      type: def.type,
      name: def.name,
      mode: def.mode ?? 'Any',
      approvers: (def.approvers ?? []).map((r) => ({ ...r })),
      emptyApproverPolicy: def.emptyApproverPolicy ?? 'Error',
      allowReturn: def.allowReturn ?? true,
      allowAddSign: def.allowAddSign ?? true,
      allowTransfer: def.allowTransfer ?? true,
      requireQualifiedAssignee: def.requireQualifiedAssignee ?? false,
      actions: clone(def.actions ?? []),
      notifications: clone(def.notifications ?? []),
      timeout: def.timeout ? { ...def.timeout } : null,
      fieldPermissions: { ...def.fieldPermissions },
    },
  }))

  const edges: FlowEdge[] = defs.flatMap((def) => {
    if (def.type === 'Condition')
      return (def.branches ?? []).map((b) =>
        createEdge(def.id, b.next, { priority: b.priority, expression: b.expression?.trim() ? b.expression : null }),
      )
    // 并行分支没有条件，连线不带分支数据
    if (def.type === 'Parallel') return (def.branches ?? []).map((b) => createEdge(def.id, b.next))
    return def.next ? [createEdge(def.id, def.next)] : []
  })

  // 旧定义（没有保存坐标）自动排版
  const needsLayout = defs.some((d) => !d.position)
  return { nodes: needsLayout ? layoutNodes(nodes, edges) : nodes, edges }
}

// ───── 画布 → 定义 ─────

export function toDefinition(nodes: FlowNode[], edges: FlowEdge[], flowNotifications: NotificationRule[] = []): FlowDefinition {
  const start = nodes.find((n) => n.data?.type === 'Start')
  const result: FlowDefinition = { startNodeId: start?.id ?? '', nodes: {} }
  if (flowNotifications.length) result.notifications = clone(flowNotifications)

  for (const node of nodes) {
    const data = node.data!
    const outgoing = edges.filter((e) => e.source === node.id)
    const def: FlowNodeDef = {
      id: node.id,
      name: data.name.trim(),
      type: data.type,
      position: { x: Math.round(node.position.x), y: Math.round(node.position.y) },
    }

    if (data.type === 'Approval' || data.type === 'Cc') {
      def.approvers = data.approvers.map((r) => ({ type: r.type, value: r.value.trim() }))
      if (Object.keys(data.fieldPermissions).length) def.fieldPermissions = { ...data.fieldPermissions }
    }
    if (data.type === 'Approval') {
      def.mode = data.mode
      def.emptyApproverPolicy = data.emptyApproverPolicy
      // 只保存非默认值，保持定义简洁
      if (!data.allowReturn) def.allowReturn = false
      if (!data.allowAddSign) def.allowAddSign = false
      if (!data.allowTransfer) def.allowTransfer = false
      if (data.requireQualifiedAssignee) def.requireQualifiedAssignee = true
      if (data.timeout) def.timeout = { ...data.timeout }
    }
    if (data.type === 'Action') def.actions = clone(data.actions)
    if (notifyEventsFor(data.type).length && data.notifications.length) def.notifications = clone(data.notifications)
    if (data.type === 'Condition') {
      def.branches = outgoing.map((e) => ({
        priority: e.data?.priority ?? 1,
        expression: e.data?.expression?.trim() ? e.data.expression.trim() : null,
        next: e.target,
      }))
    } else if (data.type === 'Parallel') {
      def.branches = outgoing.map((e, i) => ({ priority: i + 1, expression: null, next: e.target }))
    } else if (data.type !== 'End') {
      def.next = outgoing[0]?.target ?? null
    }

    result.nodes[node.id] = def
  }
  return result
}

// ───── 自动排版 ─────

export function layoutNodes(nodes: FlowNode[], edges: FlowEdge[]): FlowNode[] {
  const graph = new dagre.graphlib.Graph()
  // align: 'UL' 让主干左对齐成一条直线、分支节点排到右侧。
  // Vue Flow 按节点位置直接绘制连线（不使用 dagre 的折线路径），居中对齐时跨层连线（如默认分支）会穿过分支节点
  graph.setGraph({ rankdir: 'TB', align: 'UL', nodesep: 60, edgesep: 60, ranksep: 70 })
  graph.setDefaultEdgeLabel(() => ({}))
  nodes.forEach((n) => graph.setNode(n.id, { width: NODE_WIDTH, height: NODE_HEIGHT }))
  edges.forEach((e) => graph.setEdge(e.source, e.target))
  dagre.layout(graph)

  return nodes.map((n) => {
    const { x, y } = graph.node(n.id)
    // dagre 返回中心点，Vue Flow 使用左上角
    return { ...n, position: { x: x - NODE_WIDTH / 2, y: y - NODE_HEIGHT / 2 } }
  })
}

// ───── 连线规则 ─────

/** 校验一条新连线；返回错误提示，合法时返回 null。 */
export function checkConnection(nodes: FlowNode[], edges: FlowEdge[], source: string, target: string): string | null {
  if (source === target) return '不能连接到自身'
  const sourceType = nodes.find((n) => n.id === source)?.data?.type
  const targetType = nodes.find((n) => n.id === target)?.data?.type
  if (sourceType === 'End') return '结束节点不能有后续节点'
  if (targetType === 'Start') return '不能连接到开始节点'
  if (isMultiOutput(sourceType) && edges.some((e) => e.source === source && e.target === target))
    return '该分支已存在'
  return null
}

/**
 * 应用一条新连线：普通节点只有一个后续节点（新连线替换旧连线）；
 * 条件节点每条连线是一个分支，没有默认分支时新分支作为默认分支，否则需要填写条件。
 */
export function applyConnection(nodes: FlowNode[], edges: FlowEdge[], source: string, target: string): FlowEdge[] {
  const sourceType = nodes.find((n) => n.id === source)?.data?.type
  if (sourceType === 'Parallel') return [...edges, createEdge(source, target)]
  if (sourceType !== 'Condition') {
    return [...edges.filter((e) => e.source !== source), createEdge(source, target)]
  }

  const branches = edges.filter((e) => e.source === source)
  const hasDefault = branches.some((e) => e.data?.expression === null)
  const priority = Math.max(0, ...branches.filter((e) => e.data?.expression !== null).map((e) => e.data?.priority ?? 0)) + 1
  return [...edges, createEdge(source, target, { priority: hasDefault ? priority : 99, expression: hasDefault ? '' : null })]
}

// ───── 拖放插入 ─────

type Point = { x: number; y: number }

/** 节点实际尺寸：Vue Flow 测量后的 dimensions 优先，否则用默认尺寸。 */
function sizeOf(node: FlowNode) {
  const measured = (node as { dimensions?: { width: number; height: number } }).dimensions
  return measured?.width ? measured : { width: NODE_WIDTH, height: NODE_HEIGHT }
}

function distanceToSegment(p: Point, a: Point, b: Point) {
  const dx = b.x - a.x
  const dy = b.y - a.y
  const lengthSq = dx * dx + dy * dy
  const t = lengthSq === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / lengthSq))
  return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy))
}

/**
 * 查找经过 point（画布坐标）的连线。连线从源节点底部中点出发、到目标节点顶部中点，
 * 按 smoothstep 的走线近似为“竖 → 横 → 竖”三段折线。返回距离最近且在容差内的连线 ID。
 */
export function findEdgeAtPoint(
  nodes: FlowNode[],
  edges: FlowEdge[],
  point: Point,
  options: { tolerance?: number; excludeNodeId?: string } = {},
): string | null {
  const tolerance = options.tolerance ?? 24
  const byId = new Map(nodes.map((n) => [n.id, n]))
  let best: { id: string; distance: number } | null = null

  for (const edge of edges) {
    if (edge.source === options.excludeNodeId || edge.target === options.excludeNodeId) continue
    const source = byId.get(edge.source)
    const target = byId.get(edge.target)
    if (!source || !target) continue

    const s = sizeOf(source)
    const t = sizeOf(target)
    const from = { x: source.position.x + s.width / 2, y: source.position.y + s.height }
    const to = { x: target.position.x + t.width / 2, y: target.position.y }
    const midY = (from.y + to.y) / 2
    const path = to.y > from.y ? [from, { x: from.x, y: midY }, { x: to.x, y: midY }, to] : [from, to]

    let distance = Infinity
    for (let i = 0; i < path.length - 1; i++) distance = Math.min(distance, distanceToSegment(point, path[i]!, path[i + 1]!))
    if (distance <= tolerance && (!best || distance < best.distance)) best = { id: edge.id, distance }
  }
  return best?.id ?? null
}

/** 可以插入到连线中间的节点：有入口也有出口，且当前没有任何连线。 */
export function canInsertOnEdge(nodes: FlowNode[], edges: FlowEdge[], nodeId: string) {
  const type = nodes.find((n) => n.id === nodeId)?.data?.type
  return !!type && type !== 'Start' && type !== 'End' && !edges.some((e) => e.source === nodeId || e.target === nodeId)
}

/**
 * 把节点插入连线：A→B 变为 A→N→B。A→N 保留原连线的分支条件；
 * N 是条件节点时，N→B 作为它的默认分支。不能插入时返回 null。
 */
export function insertNodeOnEdge(nodes: FlowNode[], edges: FlowEdge[], edgeId: string, nodeId: string): FlowEdge[] | null {
  const edge = edges.find((e) => e.id === edgeId)
  if (!edge || !canInsertOnEdge(nodes, edges, nodeId)) return null

  const isCondition = nodes.find((n) => n.id === nodeId)?.data?.type === 'Condition'
  const index = edges.indexOf(edge)
  const replaced = [
    createEdge(edge.source, nodeId, edge.data ? { ...edge.data } : undefined),
    createEdge(nodeId, edge.target, isCondition ? { priority: 99, expression: null } : undefined),
  ]
  // 保持原位置，条件节点的分支顺序（即导出的 branches 顺序）不变
  return [...edges.slice(0, index), ...replaced, ...edges.slice(index + 1)]
}

/**
 * 插入节点后若与下游节点挤在一起，把位于其下方的节点整体下移，留出 gap 的间距。
 * 只移动纵向位置在插入节点之下的节点，不改变左右布局。
 */
export function makeRoomBelow(nodes: FlowNode[], insertedId: string, gap = 50): FlowNode[] {
  const inserted = nodes.find((n) => n.id === insertedId)
  if (!inserted) return nodes
  const bottom = inserted.position.y + sizeOf(inserted).height
  const below = nodes.filter((n) => n.id !== insertedId && n.position.y + sizeOf(n).height / 2 > inserted.position.y + sizeOf(inserted).height / 2)
  if (below.length === 0) return nodes

  const top = Math.min(...below.map((n) => n.position.y))
  const shift = bottom + gap - top
  if (shift <= 0) return nodes
  const ids = new Set(below.map((n) => n.id))
  return nodes.map((n) => (ids.has(n.id) ? { ...n, position: { x: n.position.x, y: n.position.y + shift } } : n))
}

// ───── 新建节点 ─────

let idSeed = Date.now() % 100000

export function newNodeId(type: NodeType, existing: Iterable<string>) {
  const ids = new Set(existing)
  let id: string
  do id = `${type.toLowerCase()}_${(idSeed++).toString(36)}`
  while (ids.has(id))
  return id
}

export function createNodeData(type: NodeType): FlowNodeData {
  const defaults: Record<NodeType, Pick<FlowNodeData, 'name' | 'approvers' | 'actions' | 'notifications'>> = {
    Start: { name: '提交申请', approvers: [], actions: [], notifications: [] },
    // 新审批节点默认发站内信给审批人，可在属性面板修改
    Approval: {
      name: '审批',
      approvers: [{ type: 'Role', value: '' }],
      actions: [],
      notifications: [defaultNotification('Enter')],
    },
    Cc: { name: '抄送', approvers: [{ type: 'Initiator', value: '' }], actions: [], notifications: [] },
    Condition: { name: '条件分支', approvers: [], actions: [], notifications: [] },
    Action: { name: '自动动作', approvers: [], actions: [], notifications: [] },
    Parallel: { name: '并行分支', approvers: [], actions: [], notifications: [] },
    Join: { name: '合并', approvers: [], actions: [], notifications: [] },
    End: { name: '结束', approvers: [], actions: [], notifications: [] },
  }
  return {
    type,
    mode: 'Any',
    emptyApproverPolicy: 'Error',
    allowReturn: true,
    allowAddSign: true,
    allowTransfer: true,
    requireQualifiedAssignee: false,
    timeout: null,
    fieldPermissions: {},
    ...defaults[type],
  }
}

export const defaultTemplates: Record<NotifyEvent, string> = {
  Enter: '【待审批】{标题}，申请人 {申请人}，当前环节：{节点}',
  Approved: '【已同意】{标题}，{操作人} 同意了 {节点}',
  Rejected: '【已驳回】{标题}，{操作人} 在 {节点} 驳回：{意见}',
  Transferred: '【转交】{标题}，{操作人} 将 {节点} 转交给你',
  Timeout: '【催办】{标题} 在 {节点} 已超过处理时限（截止 {截止时间}），请尽快处理',
  Completed: '【{结果}】{标题}',
}

/** 新建通知规则：超时、到达默认发给当前审批人，其余默认发给申请人 */
export function defaultNotification(event: NotifyEvent): NotificationRule {
  const toAssignees = event === 'Enter' || event === 'Timeout' || event === 'Transferred'
  return {
    events: [event],
    recipients: [{ type: toAssignees ? 'Assignees' : 'Initiator', value: '' }],
    channels: ['InApp'],
    content: defaultTemplates[event],
  }
}

export function newFlowDefinition(): FlowDefinition {
  return {
    startNodeId: 'start',
    nodes: {
      start: { id: 'start', name: '提交申请', type: 'Start', next: 'end', position: { x: 0, y: 0 } },
      end: { id: 'end', name: '结束', type: 'End', position: { x: 0, y: 160 } },
    },
  }
}

// ───── 运行状态（审批详情的只读流程图） ─────

/** 根据实例的任务与轨迹，推算每个节点的状态以及走过的连线。 */
export function computeRunState(definition: FlowDefinition, instance: ApprovalInstance) {
  const status = new Map<string, NodeRunStatus>()
  const running = instance.status === 'Running'

  for (const node of Object.values(definition.nodes)) {
    const tasks = latestRound(instance.tasks.filter((t) => t.nodeId === node.id))
    const logged = instance.logs.some((l) => l.nodeId === node.id)

    if (node.type === 'Start') status.set(node.id, 'done')
    else if (tasks.some((t) => t.status === 'Rejected')) status.set(node.id, 'rejected')
    else if (running && tasks.some((t) => t.status === 'Pending' || t.status === 'Waiting' || t.status === 'Suspended'))
      status.set(node.id, 'active')
    else if (tasks.some((t) => t.status === 'Approved') || logged) status.set(node.id, 'done')
    else if (node.type === 'End' && instance.status === 'Approved') status.set(node.id, 'done')
  }

  // 条件、并行、合并节点本身不产生任务：只要某个后续节点被走到，就视为已经过。
  // 按拓扑逆序多次传播，处理连续的网关（如并行节点后紧跟条件节点）
  const reached = (id: string) => status.has(id)
  const gateways = Object.values(definition.nodes).filter((n) => n.type === 'Condition' || n.type === 'Parallel' || n.type === 'Join')
  for (let changed = true; changed; ) {
    changed = false
    for (const node of gateways) {
      if (!status.has(node.id) && successors(node).some(reached)) {
        status.set(node.id, 'done')
        changed = true
      }
    }
  }

  const visitedEdges = new Set<string>()
  for (const node of Object.values(definition.nodes)) {
    if (status.get(node.id) !== 'done') continue
    for (const target of successors(node)) if (reached(target)) visitedEdges.add(`${node.id}->${target}`)
  }

  return { status, visitedEdges }
}

const successors = (node: FlowNodeDef) =>
  node.type === 'Condition' || node.type === 'Parallel' ? (node.branches ?? []).map((b) => b.next) : node.next ? [node.next] : []

/** 节点被退回后会重新生成任务，只看最近一轮 */
function latestRound(tasks: ApprovalInstance['tasks']) {
  if (tasks.length === 0) return tasks
  const round = Math.max(...tasks.map((t) => t.round ?? 0))
  return tasks.filter((t) => (t.round ?? 0) === round)
}
