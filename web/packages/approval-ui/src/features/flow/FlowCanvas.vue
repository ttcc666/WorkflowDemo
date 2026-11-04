<script setup lang="ts">
import { useId, useTemplateRef } from 'vue'
import { VueFlow, useVueFlow, type Connection } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import FlowNodeCard from './FlowNodeCard.vue'
import {
  canInsertOnEdge,
  findEdgeAtPoint,
  NODE_HEIGHT,
  NODE_WIDTH,
  PALETTE_DRAG_TYPE,
  type FlowEdge,
  type FlowNode,
  type NodeType,
} from './flowModel'

const props = withDefaults(defineProps<{ readonly?: boolean; height?: string }>(), {
  readonly: false,
  height: '100%',
})

const nodes = defineModel<FlowNode[]>('nodes', { required: true })
const edges = defineModel<FlowEdge[]>('edges', { required: true })

const emit = defineEmits<{
  connect: [source: string, target: string]
  nodeClick: [id: string]
  edgeClick: [id: string]
  paneClick: []
  /** 画布内部产生的修改：拖拽结束、键盘删除 */
  changed: []
  /** 从节点面板拖放到画布；center 为落点的画布坐标，落在连线上时 edgeId 为该连线 */
  dropNode: [type: NodeType, center: { x: number; y: number }, edgeId: string | null]
  /** 把画布上的孤立节点拖到连线上 */
  nodeDroppedOnEdge: [nodeId: string, edgeId: string]
}>()

// 每个画布使用独立的 Vue Flow 状态，避免同页多个画布互相干扰
const flowId = useId()
const {
  fitView,
  screenToFlowCoordinate,
  findEdge,
  onNodeDrag,
  onNodeDragStop,
  onNodesChange,
  onEdgesChange,
  onNodesInitialized,
  areNodesInitialized,
} = useVueFlow(flowId)
const container = useTemplateRef<HTMLDivElement>('container')

onNodesChange((changes) => changes.some((c) => c.type === 'remove') && emit('changed'))
onEdgesChange((changes) => changes.some((c) => c.type === 'remove') && emit('changed'))

// 节点尺寸测量完成后才能正确适配视口；只在首次加载或显式请求时适配，避免每次添加节点都跳动
let fitPending = true
const fit = () => fitView({ padding: 0.15, maxZoom: 1 })
onNodesInitialized(() => {
  if (!fitPending) return
  fitPending = false
  void fit()
})

/** 请求适配视口：节点已测量时立即生效，否则在测量完成后生效（如整体替换节点后）。 */
function requestFit() {
  fitPending = true
  requestAnimationFrame(() => {
    if (fitPending && areNodesInitialized.value) {
      fitPending = false
      void fit()
    }
  })
}

function onConnect(connection: Connection) {
  emit('connect', connection.source, connection.target)
}

/** 当前视口中心对应的画布坐标（点击添加节点时放在这里）。 */
function viewportCenter() {
  const rect = container.value?.getBoundingClientRect()
  if (!rect) return { x: 0, y: 0 }
  const center = screenToFlowCoordinate({ x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 })
  return { x: center.x - NODE_WIDTH / 2, y: center.y - NODE_HEIGHT / 2 }
}

// ───── 拖放：高亮将要插入的连线 ─────

const DROP_TARGET_CLASS = 'flow-edge--drop-target'
let highlightedEdgeId: string | null = null

/** 直接修改 Vue Flow 内部连线的样式类，不经过 v-model，因此不会被当作流程修改记录。 */
function highlightEdge(id: string | null) {
  if (id === highlightedEdgeId) return
  const previous = highlightedEdgeId ? findEdge(highlightedEdgeId) : undefined
  if (previous) previous.class = undefined
  const next = id ? findEdge(id) : undefined
  if (next) next.class = DROP_TARGET_CLASS
  highlightedEdgeId = id
}

const nodeCenter = (node: { position: { x: number; y: number }; dimensions?: { width: number; height: number } }) => ({
  x: node.position.x + (node.dimensions?.width || NODE_WIDTH) / 2,
  y: node.position.y + (node.dimensions?.height || NODE_HEIGHT) / 2,
})

const isPaletteDrag = (e: DragEvent) => !props.readonly && !!e.dataTransfer?.types.includes(PALETTE_DRAG_TYPE)

function onDragOver(e: DragEvent) {
  if (!isPaletteDrag(e)) return
  e.preventDefault() // 允许放置
  e.dataTransfer!.dropEffect = 'copy'
  const point = screenToFlowCoordinate({ x: e.clientX, y: e.clientY })
  highlightEdge(findEdgeAtPoint(nodes.value, edges.value, point))
}

function onDragLeave(e: DragEvent) {
  // 移到子元素上也会触发 dragleave，只在真正离开画布时清除高亮
  if (!container.value?.contains(e.relatedTarget as Node | null)) highlightEdge(null)
}

function onDrop(e: DragEvent) {
  if (!isPaletteDrag(e)) return
  e.preventDefault()
  const type = e.dataTransfer!.getData(PALETTE_DRAG_TYPE) as NodeType
  const point = screenToFlowCoordinate({ x: e.clientX, y: e.clientY })
  const edgeId = type === 'End' ? null : findEdgeAtPoint(nodes.value, edges.value, point)
  highlightEdge(null)
  emit('dropNode', type, point, edgeId)
}

// 拖动画布上尚未连线的节点经过连线时高亮，松开时插入
onNodeDrag(({ node }) => {
  if (!canInsertOnEdge(nodes.value, edges.value, node.id)) return
  highlightEdge(findEdgeAtPoint(nodes.value, edges.value, nodeCenter(node), { excludeNodeId: node.id }))
})

onNodeDragStop(({ node }) => {
  const edgeId = highlightedEdgeId
  highlightEdge(null)
  if (edgeId && canInsertOnEdge(nodes.value, edges.value, node.id)) emit('nodeDroppedOnEdge', node.id, edgeId)
  else emit('changed')
})

defineExpose({
  fitView: requestFit,
  viewportCenter,
})
</script>

<template>
  <div ref="container" class="flow-canvas" :style="{ height: props.height }" @dragover="onDragOver"
    @dragleave="onDragLeave" @drop="onDrop">
    <VueFlow
      :id="flowId"
      v-model:nodes="nodes"
      v-model:edges="edges"
      :nodes-draggable="!readonly"
      :nodes-connectable="!readonly"
      :elements-selectable="!readonly"
      :edges-updatable="false"
      :delete-key-code="readonly ? null : ['Delete', 'Backspace']"
      :zoom-on-double-click="false"
      :min-zoom="0.3"
      :max-zoom="1.5"
      :default-edge-options="{ type: 'smoothstep' }"
      @connect="onConnect"
      @node-click="({ node }) => emit('nodeClick', node.id)"
      @edge-click="({ edge }) => emit('edgeClick', edge.id)"
      @pane-click="emit('paneClick')"
    >
      <!-- 插槽名为 node-<节点类型>，节点类型见 flowModel.FLOW_NODE_TYPE -->
      <template #node-flow-node="nodeProps">
        <FlowNodeCard :data="nodeProps.data" :selected="nodeProps.selected" />
      </template>
      <Background :gap="16" />
      <Controls :show-interactive="false" />
    </VueFlow>
  </div>
</template>

<style scoped>
.flow-canvas {
  width: 100%;
  min-height: 320px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafbfc;
}

.flow-canvas :deep(.flow-edge--drop-target path.vue-flow__edge-path) {
  stroke: #409eff;
  stroke-width: 3;
  stroke-dasharray: 6 4;
}
</style>
