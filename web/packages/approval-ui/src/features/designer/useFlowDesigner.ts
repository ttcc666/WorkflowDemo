import { computed, nextTick, ref, shallowRef, type Ref } from 'vue'
import { approvalApi } from '../../api/approval'
import {
  applyConnection,
  branchLabel,
  checkConnection,
  createNodeData,
  insertNodeOnEdge,
  layoutNodes,
  makeRoomBelow,
  newNodeId,
  toDefinition,
  toFlowElements,
  FLOW_NODE_TYPE,
  NODE_HEIGHT,
  NODE_WIDTH,
  type BranchData,
  type FlowDefinition,
  type FlowEdge,
  type FlowNode,
  type FlowNodeData,
  type NodeType,
  type NotificationRule,
} from '../flow/flowModel'
import { createHistory } from './history'

type Point = { x: number; y: number }

/**
 * 流程设计器状态。画布（Vue Flow）通过 v-model 绑定 nodes/edges，负责拖拽、选择和删除；
 * 其余修改都通过这里的方法完成。每次修改都会记录一个快照，用于撤销 / 重做和判断是否有未发布的修改。
 */
export function useFlowDesigner() {
  // 运行时保持深层响应（属性面板会就地修改 node.data）；
  // 显式标注 Ref<T> 以跳过 UnwrapRef 对 Vue Flow 复杂泛型的推导（否则 TS2589 类型过深）
  const nodes = ref([]) as Ref<FlowNode[]>
  const edges = ref([]) as Ref<FlowEdge[]>
  /** 流程级通知规则（对所有审批节点生效，以及流程结束） */
  const flowNotifications = ref<NotificationRule[]>([])
  const selectedNodeId = shallowRef<string | null>(null)
  const selectedEdgeId = shallowRef<string | null>(null)
  const errors = shallowRef<string[] | null>(null)

  const history = createHistory()
  let savedSnapshot: string | null = null
  const dirty = shallowRef(false)
  const canUndo = shallowRef(false)
  const canRedo = shallowRef(false)

  const selectedNode = computed(() => nodes.value.find((n) => n.id === selectedNodeId.value))
  const selectedEdge = computed(() => edges.value.find((e) => e.id === selectedEdgeId.value))
  const nodeName = (id: string) => nodes.value.find((n) => n.id === id)?.data?.name ?? id

  // ───── 历史记录 ─────

  const snapshot = () => JSON.stringify(toDefinition(nodes.value, edges.value, flowNotifications.value))

  function syncHistoryState() {
    canUndo.value = history.canUndo
    canRedo.value = history.canRedo
    dirty.value = history.present !== savedSnapshot
  }

  let recordScheduled = false
  let pendingKey: string | undefined

  /**
   * 标记有修改。同一轮事件中的多次修改（如删除节点时连带删除连线）合并为一步，
   * 并等 Vue Flow 把画布内部的修改同步到 v-model 之后再记录快照。
   * coalesceKey 相同的连续修改（如在名称输入框里打字）合并为一步。
   */
  function markChanged(coalesceKey?: string) {
    errors.value = null // 结构变了，上次校验结果失效
    pendingKey = recordScheduled && pendingKey !== coalesceKey ? undefined : coalesceKey
    if (recordScheduled) return
    recordScheduled = true
    void nextTick(() => {
      recordScheduled = false
      history.record(snapshot(), pendingKey)
      pendingKey = undefined
      syncHistoryState()
    })
  }

  function restore(state: string) {
    const definition = JSON.parse(state) as FlowDefinition
    const elements = toFlowElements(definition)
    nodes.value = elements.nodes
    edges.value = elements.edges
    flowNotifications.value = definition.notifications ?? []
    if (selectedNodeId.value && !nodes.value.some((n) => n.id === selectedNodeId.value)) selectedNodeId.value = null
    if (selectedEdgeId.value && !edges.value.some((e) => e.id === selectedEdgeId.value)) selectedEdgeId.value = null
    errors.value = null
    syncHistoryState()
  }

  function undo() {
    const state = history.undo()
    if (state !== null) restore(state)
  }

  function redo() {
    const state = history.redo()
    if (state !== null) restore(state)
  }

  function load(definition: FlowDefinition) {
    const elements = toFlowElements(definition)
    nodes.value = elements.nodes
    edges.value = elements.edges
    flowNotifications.value = JSON.parse(JSON.stringify(definition.notifications ?? [])) as NotificationRule[]
    clearSelection()
    errors.value = null
    history.reset(snapshot())
    savedSnapshot = history.present
    syncHistoryState()
  }

  function markSaved() {
    savedSnapshot = history.present
    syncHistoryState()
  }

  // ───── 选择 ─────

  function selectNode(id: string) {
    selectedNodeId.value = id
    selectedEdgeId.value = null
  }

  function selectEdge(id: string) {
    selectedEdgeId.value = id
    selectedNodeId.value = null
  }

  function clearSelection() {
    selectedNodeId.value = null
    selectedEdgeId.value = null
  }

  // ───── 节点与连线 ─────

  /** 在指定位置（节点左上角）添加节点；stagger 用于点击添加时与已有节点错开。 */
  function addNode(type: NodeType, position: Point, stagger = true) {
    const id = newNodeId(type, nodes.value.map((n) => n.id))
    const offset = stagger ? (nodes.value.length % 5) * 24 : 0
    nodes.value.push({ id, type: FLOW_NODE_TYPE, position: { x: position.x + offset, y: position.y + offset }, data: createNodeData(type) })
    selectNode(id)
    markChanged()
    return id
  }

  /** 把已有的孤立节点插入连线；成功时下游节点自动下移留出空间。 */
  function insertOnEdge(nodeId: string, edgeId: string) {
    const sourceId = edges.value.find((e) => e.id === edgeId)?.source
    const next = insertNodeOnEdge(nodes.value, edges.value, edgeId, nodeId)
    if (!next) return false
    edges.value = next
    // 新节点与上游节点对齐并放在其下方，再把下游节点整体下移留出空间
    const source = nodes.value.find((n) => n.id === sourceId)
    const inserted = nodes.value.find((n) => n.id === nodeId)
    if (source && inserted) {
      const sourceHeight = (source as { dimensions?: { height: number } }).dimensions?.height || NODE_HEIGHT
      inserted.position = { x: source.position.x, y: Math.max(inserted.position.y, source.position.y + sourceHeight + 50) }
    }
    nodes.value = makeRoomBelow(nodes.value, nodeId)
    markChanged()
    return true
  }

  /** 从节点面板拖放：节点中心对齐到落点；落在连线上时插入该连线。 */
  function dropNode(type: NodeType, center: Point, edgeId: string | null) {
    const id = addNode(type, { x: center.x - NODE_WIDTH / 2, y: center.y - NODE_HEIGHT / 2 }, false)
    if (edgeId) insertOnEdge(id, edgeId)
    return id
  }

  /** 新建连线；不合法时返回错误提示。 */
  function connect(source: string, target: string): string | null {
    const error = checkConnection(nodes.value, edges.value, source, target)
    if (error) return error
    edges.value = applyConnection(nodes.value, edges.value, source, target)
    markChanged()
    return null
  }

  function updateNode(id: string, patch: Partial<FlowNodeData>) {
    const node = nodes.value.find((n) => n.id === id)
    if (!node?.data) return
    Object.assign(node.data, patch)
    markChanged(`node:${id}:${Object.keys(patch).join(',')}`)
  }

  function updateFlowNotifications(rules: NotificationRule[]) {
    flowNotifications.value = rules
    markChanged('flow:notifications')
  }

  function updateBranch(edgeId: string, patch: Partial<BranchData>) {
    const edge = edges.value.find((e) => e.id === edgeId)
    if (!edge?.data) return

    // 每个条件节点只能有一个默认分支：设为默认时，原来的默认分支改为需要填写条件
    if (patch.expression === null) {
      for (const sibling of edges.value) {
        if (sibling !== edge && sibling.source === edge.source && sibling.data?.expression === null) {
          sibling.data = { ...sibling.data, expression: '' }
          sibling.label = branchLabel(sibling.data)
        }
      }
    }
    edge.data = { ...edge.data, ...patch }
    edge.label = branchLabel(edge.data)
    markChanged(`edge:${edgeId}:${Object.keys(patch).join(',')}`)
  }

  function removeNode(id: string) {
    if (nodes.value.find((n) => n.id === id)?.data?.type === 'Start') return
    nodes.value = nodes.value.filter((n) => n.id !== id)
    edges.value = edges.value.filter((e) => e.source !== id && e.target !== id)
    clearSelection()
    markChanged()
  }

  function removeEdge(id: string) {
    edges.value = edges.value.filter((e) => e.id !== id)
    clearSelection()
    markChanged()
  }

  function autoLayout() {
    nodes.value = layoutNodes(nodes.value, edges.value)
    markChanged()
  }

  /** 前端补充检查（后端不校验的展示类问题）+ 后端结构校验。 */
  async function validate() {
    const definition = toDefinition(nodes.value, edges.value, flowNotifications.value)
    const local = nodes.value.filter((n) => !n.data?.name.trim()).map((n) => `节点 '${n.id}' 未填写名称`)
    const { errors: server } = await approvalApi.validate(JSON.stringify(definition))
    errors.value = [...local, ...server]
    return { definition, ok: errors.value.length === 0 }
  }

  return {
    nodes,
    edges,
    flowNotifications,
    selectedNode,
    selectedEdge,
    dirty,
    errors,
    canUndo,
    canRedo,
    nodeName,
    load,
    selectNode,
    selectEdge,
    clearSelection,
    addNode,
    dropNode,
    insertOnEdge,
    connect,
    updateNode,
    updateFlowNotifications,
    updateBranch,
    removeNode,
    removeEdge,
    autoLayout,
    validate,
    markChanged,
    markSaved,
    undo,
    redo,
  }
}
