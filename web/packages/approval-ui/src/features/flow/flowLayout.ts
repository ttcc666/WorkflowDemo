import dagre from '@dagrejs/dagre'
import { NODE_WIDTH, NODE_HEIGHT, type FlowNode, type FlowEdge } from './flowElements'

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
