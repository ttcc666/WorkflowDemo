<script setup lang="ts">
import { nodeTypeLabels, PALETTE_DRAG_TYPE, type NodeType } from '../flow/flowModel'

const emit = defineEmits<{ add: [type: NodeType] }>()

const items: { type: NodeType; desc: string }[] = [
  { type: 'Approval', desc: '指定角色或人员审批' },
  { type: 'Cc', desc: '通知相关人员，不阻塞流程' },
  { type: 'Condition', desc: '按表单数据走不同分支' },
  { type: 'Parallel', desc: '多个分支同时进行' },
  { type: 'Join', desc: '等待并行分支全部完成' },
  { type: 'Action', desc: '调用 HTTP、执行脚本或注册动作' },
  { type: 'End', desc: '流程结束' },
]

function onDragStart(e: DragEvent, type: NodeType) {
  e.dataTransfer?.setData(PALETTE_DRAG_TYPE, type)
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'copy'
}
</script>

<template>
  <div class="palette">
    <div class="palette__title">添加节点</div>
    <button v-for="item in items" :key="item.type" type="button" draggable="true"
      :class="['palette__item', `palette__item--${item.type.toLowerCase()}`]" :title="`拖到画布或点击添加${nodeTypeLabels[item.type]}节点`"
      @dragstart="onDragStart($event, item.type)" @click="emit('add', item.type)">
      <span class="palette__name">{{ nodeTypeLabels[item.type] }}</span>
      <span class="palette__desc">{{ item.desc }}</span>
    </button>
    <div class="palette__help">
      <p>把节点拖到画布任意位置；拖到连线上会自动插入到两节点之间。</p>
      <p>从节点底部圆点拖到另一节点即可连线。</p>
      <p>选中节点或连线后按 Delete 删除；Ctrl+Z 撤销，Ctrl+Y 重做。</p>
      <p>条件节点的每条连线是一个分支，点击连线设置条件。</p>
      <p>并行分支：从“并行分支”节点连出多条线，各分支最后都连到同一个“合并”节点。</p>
      <p>点击画布空白处可设置流程级通知。</p>
    </div>
  </div>
</template>

<style scoped>
.palette {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.palette__title {
  color: #303133;
  font-weight: 600;
}

.palette__item {
  --accent: #909399;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 10px;
  border: 1px solid #dcdfe6;
  border-left: 4px solid var(--accent);
  border-radius: 6px;
  background: #fff;
  text-align: left;
  cursor: grab;
  transition: box-shadow 0.15s;
}

.palette__item:hover {
  box-shadow: 0 2px 8px rgb(0 0 0 / 10%);
}

.palette__item:active {
  cursor: grabbing;
}

.palette__item--approval { --accent: #e6a23c; }
.palette__item--cc { --accent: #6f7ad3; }
.palette__item--condition { --accent: #13c2c2; }
.palette__item--action { --accent: #b37feb; }
.palette__item--parallel,
.palette__item--join { --accent: #36cfc9; }

.palette__name {
  color: #303133;
  font-weight: 600;
}

.palette__desc {
  color: #909399;
  font-size: 12px;
}

.palette__help {
  margin-top: 8px;
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}

.palette__help p {
  margin: 0 0 4px;
}
</style>
