<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import type { ApprovalInstance } from '../../contracts/api'
import FlowCanvas from './FlowCanvas.vue'
import { computeRunState, toFlowElements, NODE_HEIGHT, type FlowDefinition, type FlowEdge, type FlowNode } from './flowModel'

/** 只读流程图：按实例的任务与轨迹高亮已完成、进行中、被驳回的节点及走过的路径。 */
const props = defineProps<{
  definitionJson: string
  instance: ApprovalInstance
}>()

// 只读展示只整体替换，不需要深层响应
const nodes = shallowRef<FlowNode[]>([])
const edges = shallowRef<FlowEdge[]>([])

watch(
  () => [props.definitionJson, props.instance] as const,
  ([json, instance]) => {
    const definition = JSON.parse(json) as FlowDefinition
    const elements = toFlowElements(definition)
    const { status, visitedEdges } = computeRunState(definition, instance)

    nodes.value = elements.nodes.map((n) => ({ ...n, data: { ...n.data!, status: status.get(n.id) } }))
    edges.value = elements.edges.map((e) => {
      const visited = visitedEdges.has(`${e.source}->${e.target}`)
      return { ...e, animated: visited && instance.status === 'Running', style: visited ? { stroke: '#67c23a', strokeWidth: 2 } : undefined }
    })
  },
  { immediate: true },
)

// 画布高度随流程图高度自适应（约 0.75 倍缩放），避免长流程被压缩到看不清
const height = computed(() => {
  const ys = nodes.value.map((n) => n.position.y)
  const graphHeight = ys.length ? Math.max(...ys) - Math.min(...ys) + NODE_HEIGHT : 0
  return `${Math.min(Math.max(graphHeight * 0.75 + 80, 320), 1000)}px`
})
</script>

<template>
  <FlowCanvas v-model:nodes="nodes" v-model:edges="edges" readonly :height="height" />
</template>
