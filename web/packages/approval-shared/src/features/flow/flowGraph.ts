import { createEdge, NODE_WIDTH, NODE_HEIGHT, type FlowNode, type FlowEdge } from './flowElements.js'
import { isMultiOutput } from './flowPresentation.js'

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
