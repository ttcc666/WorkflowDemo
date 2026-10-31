<script setup lang="ts">
import { computed } from 'vue'
import { Handle, Position } from '@vue-flow/core'
import { approverSummary, nodeTypeLabels, type FlowNodeData, type NodeType } from './flowModel'

// Vue Flow 会向自定义节点传入大量内部属性，这里只声明用到的，其余不透传到 DOM
defineOptions({ inheritAttrs: false })

const props = defineProps<{
  data: FlowNodeData
  selected?: boolean
}>()

const typeIcons: Record<NodeType, string> = {
  Start: '▶',
  Approval: '✔',
  Cc: '✉',
  Condition: '◆',
  Action: '⚙',
  End: '■',
}

const statusText = { done: '已完成', active: '进行中', rejected: '已驳回', skipped: '未经过' } as const

const subtitle = computed(() => approverSummary(props.data))

/** 节点卡片右上角的配置提示：时限与通知数量 */
const badges = computed(() => {
  const result: { text: string; title: string }[] = []
  const timeout = props.data.timeout
  if (timeout) {
    const hours = timeout.minutes / 60
    result.push({ text: `⏱ ${hours >= 1 ? `${+hours.toFixed(1)}h` : `${timeout.minutes}m`}`, title: '已设置处理时限' })
  }
  const count = props.data.notifications?.length ?? 0
  if (count) result.push({ text: `🔔 ${count}`, title: `${count} 条通知规则` })
  return result
})

const cardClass = computed(() => [
  'flow-node',
  `flow-node--${props.data.type.toLowerCase()}`,
  props.data.status && `flow-node--${props.data.status}`,
  { 'flow-node--selected': props.selected },
])
</script>

<template>
  <div :class="cardClass">
    <Handle v-if="data.type !== 'Start'" type="target" :position="Position.Top" />

    <div class="flow-node__header">
      <span class="flow-node__icon">{{ typeIcons[data.type] }}</span>
      <span class="flow-node__type">{{ nodeTypeLabels[data.type] }}</span>
      <span v-if="data.status" class="flow-node__status">{{ statusText[data.status] }}</span>
      <span v-else-if="badges.length" class="flow-node__badges">
        <span v-for="b in badges" :key="b.text" :title="b.title">{{ b.text }}</span>
      </span>
    </div>
    <div class="flow-node__name" :title="data.name">{{ data.name || '(未命名)' }}</div>
    <div v-if="subtitle" class="flow-node__subtitle" :title="subtitle">{{ subtitle }}</div>

    <Handle v-if="data.type !== 'End'" type="source" :position="Position.Bottom" />
  </div>
</template>

<style scoped>
.flow-node {
  --accent: #909399;
  width: 220px;
  min-height: 72px;
  box-sizing: border-box;
  padding: 8px 12px;
  border: 1px solid #dcdfe6;
  border-top: 4px solid var(--accent);
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 2px 6px rgb(0 0 0 / 6%);
  font-size: 12px;
  transition: box-shadow 0.15s, border-color 0.15s;
}

.flow-node--start { --accent: #409eff; }
.flow-node--approval { --accent: #e6a23c; }
.flow-node--cc { --accent: #6f7ad3; }
.flow-node--condition { --accent: #13c2c2; }
.flow-node--action { --accent: #b37feb; }
.flow-node--end { --accent: #909399; }

.flow-node--selected {
  border-color: var(--accent);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--accent) 35%, transparent);
}

.flow-node--done { border-color: #67c23a; background: #f0f9eb; }
.flow-node--active { border-color: #e6a23c; background: #fdf6ec; animation: pulse 1.6s ease-in-out infinite; }
.flow-node--rejected { border-color: #f56c6c; background: #fef0f0; }

@keyframes pulse {
  50% { box-shadow: 0 0 0 4px rgb(230 162 60 / 25%); }
}

.flow-node__header {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--accent);
}

.flow-node__icon { font-size: 11px; }

.flow-node__status {
  margin-left: auto;
  color: #606266;
}

.flow-node__badges {
  display: flex;
  gap: 6px;
  margin-left: auto;
  color: #909399;
  font-size: 11px;
}

.flow-node--done .flow-node__status { color: #67c23a; }
.flow-node--active .flow-node__status { color: #e6a23c; }
.flow-node--rejected .flow-node__status { color: #f56c6c; }

.flow-node__name {
  margin-top: 4px;
  overflow: hidden;
  color: #303133;
  font-size: 14px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.flow-node__subtitle {
  margin-top: 2px;
  overflow: hidden;
  color: #909399;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
