<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import type { ApprovalDefinition } from '@ui/contracts/api'
import { errorMessage } from '@ui/api/http'
import FlowCanvas from '@ui/features/flow/FlowCanvas.vue'
import { toFlowElements } from '@ui/features/flow/flowSerialization'
import { formatDateTime } from '@ui/utils/labels'
import { compareDefinitionVersions, type ChangeKind, type DefinitionChange } from './definitionDiff'

const props = defineProps<{ versions: ApprovalDefinition[] }>()
const baseId = shallowRef('')
const targetId = shallowRef('')
const kind = shallowRef<ChangeKind>()
const nodeFilter = shallowRef<string>()
const focus = shallowRef<string>()
const graphSide = shallowRef<'before' | 'after'>('after')
const result = shallowRef<ReturnType<typeof compareDefinitionVersions>>()
const error = shallowRef('')
const nodes = shallowRef<ReturnType<typeof toFlowElements>['nodes']>([])
const edges = shallowRef<ReturnType<typeof toFlowElements>['edges']>([])
const candidates = computed(() => props.versions.filter(v => v.isPublished).sort((a, b) => b.version - a.version))
const base = computed(() => candidates.value.find(v => v.id === baseId.value))
const target = computed(() => candidates.value.find(v => v.id === targetId.value))
const canCompare = computed(() => base.value && target.value && base.value.id !== target.value.id && base.value.key === target.value.key)
const labels = { added: '新增', removed: '删除', modified: '修改' } as const
const areas = { flow: '流程', node: '节点', rule: '规则', route: '路由', form: '表单', layout: '布局' } as const
const tags = { added: 'success', removed: 'danger', modified: 'warning' } as const
const filtered = computed(() => result.value?.changes.filter(change =>
  (!kind.value || change.kind === kind.value) && (!nodeFilter.value || change.nodeId === nodeFilter.value)) ?? [])
const changedNodes = computed(() => [...new Set(result.value?.changes.flatMap(change => change.nodeId ? [change.nodeId] : []))])
const rulesChanged = computed(() => result.value?.changes.filter(change => change.area === 'rule').length ?? 0)
const formatVersion = (v: ApprovalDefinition) => `v${v.version} · ${v.name} · ${formatDateTime(v.createdAt)}`
const formatValue = (value: unknown) => value === undefined ? '—' : typeof value === 'string' ? value : JSON.stringify(value, null, 2)
const nodeName = (id: string) => result.value?.after.nodes[id]?.name || result.value?.before.nodes[id]?.name || id

watch([base, target], () => {
  result.value = undefined
  error.value = ''
  focus.value = undefined
  kind.value = undefined
  nodeFilter.value = undefined
  graphSide.value = 'after'
}, { flush: 'sync' })
watch([result, graphSide], () => {
  if (!result.value) return
  const graph = toFlowElements(result.value[graphSide.value])
  nodes.value = graph.nodes
  edges.value = graph.edges
})

function compare() {
  if (!canCompare.value || !base.value || !target.value) return
  try {
    result.value = compareDefinitionVersions(base.value, target.value)
  } catch (cause) { error.value = errorMessage(cause) }
}

function swap() { [baseId.value, targetId.value] = [targetId.value, baseId.value] }
function locate(id: string) {
  if (!result.value) return
  graphSide.value = Object.hasOwn(result.value.after.nodes, id) ? 'after' : 'before'
  focus.value = id
}
</script>

<template>
  <section class="comparison" aria-label="流程版本对比">
    <div class="comparison__toolbar">
      <el-select v-model="baseId" aria-label="基准版本" placeholder="选择基准版本">
        <el-option v-for="v in candidates" :key="v.id" :label="formatVersion(v)" :value="v.id" />
      </el-select>
      <el-select v-model="targetId" aria-label="目标版本" placeholder="选择目标版本">
        <el-option v-for="v in candidates" :key="v.id" :label="formatVersion(v)" :value="v.id" />
      </el-select>
      <el-button @click="swap">交换</el-button>
      <el-button :disabled="!candidates.length" @click="targetId = candidates[0]!.id">目标选最新</el-button>
      <el-button type="primary" :disabled="!canCompare" @click="compare">比较版本</el-button>
    </div>
    <el-alert v-if="error" type="error" :closable="false" :title="error" />
    <template v-if="result">
      <p aria-live="polite">新增节点 {{ result.nodes.added }} · 删除节点 {{ result.nodes.removed }} · 业务修改节点 {{ result.nodes.modified }} · 布局变化节点 {{ result.nodes.moved }} · 规则变化 {{ rulesChanged }}</p>
      <el-empty v-if="!result.changes.length" description="两个版本的配置一致" :image-size="72" />
      <template v-else>
        <div class="comparison__toolbar">
          <el-select v-model="kind" clearable aria-label="变化类型" placeholder="全部变化类型">
            <el-option v-for="(label, value) in labels" :key="value" :label="label" :value="value" />
          </el-select>
          <el-select v-model="nodeFilter" clearable aria-label="差异节点" placeholder="全部节点">
            <el-option v-for="id in changedNodes" :key="id" :label="`${nodeName(id)} (${id})`" :value="id" />
          </el-select>
        </div>
        <el-table :data="filtered" size="small" max-height="360" empty-text="没有符合筛选条件的变化">
          <el-table-column label="变化" min-width="170">
            <template #default="{ row }">
              <el-tag :type="tags[row.kind as ChangeKind]">{{ labels[row.kind as ChangeKind] }}</el-tag>
              {{ areas[row.area as DefinitionChange['area']] }}
              <el-button v-if="row.nodeId" link type="primary" @click="locate(row.nodeId)">{{ nodeName(row.nodeId) }}</el-button>
              <div class="comparison__value">{{ row.path }}</div>
            </template>
          </el-table-column>
          <el-table-column label="基准值" min-width="110"><template #default="{ row }"><pre class="comparison__value">{{ formatValue(row.before) }}</pre></template></el-table-column>
          <el-table-column label="目标值" min-width="110"><template #default="{ row }"><pre class="comparison__value">{{ formatValue(row.after) }}</pre></template></el-table-column>
        </el-table>
        <el-radio-group v-model="graphSide" aria-label="版本流程图" @change="focus = undefined">
          <el-radio-button value="before">基准流程图</el-radio-button>
          <el-radio-button value="after">目标流程图</el-radio-button>
        </el-radio-group>
        <FlowCanvas :key="graphSide" v-model:nodes="nodes" v-model:edges="edges" readonly height="320px"
          :highlighted-node-id="focus" @node-click="focus = $event" />
      </template>
    </template>
  </section>
</template>

<style scoped>
.comparison { display: grid; gap: 12px; margin: 16px 0; }
.comparison__toolbar { display: flex; flex-wrap: wrap; gap: 8px; }
.comparison__toolbar :deep(.el-select) { width: min(100%, 260px); }
.comparison__toolbar :deep(.el-button) { margin-left: 0; }
.comparison__value { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
