<script setup lang="ts">
import { computed, ref, shallowRef, useTemplateRef, watch } from 'vue'
import { useApprovalUser } from '@ui/composables/useApprovalUser'
import type { FlowDefinition } from '@ui/contracts/flow'
import { initialValues, type FormValues } from '@ui/features/forms/formModel'
import DynamicForm from '@ui/features/forms/DynamicForm.vue'
import FlowViewer from '@ui/features/flow/FlowViewer.vue'
import { useDefinitionSimulation } from './useDefinitionSimulation'

const props = defineProps<{ definition: FlowDefinition; nodeId?: string; ruleIndex?: number }>()
const visible = defineModel<boolean>({ required: true })
const { can } = useApprovalUser()
const values = ref<FormValues>({})
const raw = shallowRef('{}')
const live = shallowRef(false)
const inputForm = useTemplateRef<InstanceType<typeof DynamicForm>>('inputForm')
const json = computed(() => JSON.stringify(props.definition))
const isActionPreview = computed(() => !!props.nodeId && props.definition.nodes[props.nodeId]?.type === 'Action')
const hasActions = computed(() => Object.values(props.definition.nodes).some(node => node.type === 'Action'))
const inputJson = computed(() => props.definition.form ? JSON.stringify(values.value) : raw.value)
watch(() => props.definition, definition => { values.value = initialValues(definition.form ?? { fields: [] }); raw.value = '{}' }, { immediate: true })
const { result, error, loading, simulate } = useDefinitionSimulation(() => ({
  definitionJson: json.value, formDataJson: inputJson.value,
  nodeId: props.nodeId, ruleIndex: props.ruleIndex,
}), () => visible.value)
const statusLabels: Record<string, string> = { Visited: '已预演', AutoApproved: '按规则自动通过', RequiresDecision: '等待人工决策', Waiting: '等待分支汇聚', Blocked: '处理受阻', Failed: '预演失败' }
const statusType = (status: string) => ['Blocked', 'Failed'].includes(status) ? 'danger' : status === 'RequiresDecision' ? 'warning' : status === 'Waiting' ? 'info' : 'success'
const summary = computed(() => !result.value?.isValid ? '发现需要处理的问题' : result.value.isComplete ? '自动路径已到结束节点' : props.nodeId ? '节点预演完成' : '已完成可确定路径的预演')
async function execute() {
  if (props.definition.form && !(await inputForm.value?.validate())) {
    error.value = '请检查标红的模拟字段后重新执行'
    return
  }
  await simulate()
}
watch([inputJson, live, visible], (_, __, onCleanup) => {
  if (!live.value || !visible.value || !hasActions.value || !can('approval.definition.simulate')) return
  const timer = window.setTimeout(() => { void execute() }, 450)
  onCleanup(() => window.clearTimeout(timer))
})
</script>

<template>
  <el-dialog v-model="visible" :title="isActionPreview ? '动作预演' : nodeId ? '审批人解析预览' : '流程模拟'" width="min(1280px, 94vw)" top="4vh" destroy-on-close>
    <el-alert type="info" :closable="false" :title="isActionPreview
      ? '从开始节点预演自动路径，累积上游输出，到达目标动作后停止。遇到人工审批时，该分支暂停。'
      : '使用当前账号预演草稿，不创建真实申请。遇到人工审批后暂停，后续路径尚未验证。'" />
    <div class="simulation">
      <FlowViewer class="simulation__graph" height="var(--simulation-graph-height)" :definition-json="json" :simulation-steps="result?.steps" />
      <section class="simulation__content" aria-label="模拟输入与结果">
        <h3>模拟输入</h3>
        <DynamicForm v-if="definition.form" ref="inputForm" v-model="values" :schema="definition.form" />
        <el-input v-else v-model="raw" type="textarea" :rows="8" aria-label="模拟数据 JSON" placeholder="输入模拟数据 JSON" />
        <div class="simulation__actions">
          <el-button type="primary" :loading="loading" :disabled="!can('approval.definition.simulate')" @click="execute">执行模拟</el-button>
          <el-checkbox v-if="hasActions" v-model="live">输入变化时自动预演</el-checkbox>
          <span v-else>修改数据后需重新执行</span>
        </div>
        <el-alert v-if="error" type="error" :closable="false" :title="error" />
        <template v-if="result">
          <el-alert :type="result.isValid ? result.isComplete ? 'success' : 'info' : 'error'" :closable="false" :title="summary" />
          <p v-for="item in result.errors" :key="item">{{ item }}</p>
          <details v-if="result.warnings.length" class="simulation__details"><summary>查看预演范围与提示（{{ result.warnings.length }}）</summary><p v-for="item in result.warnings" :key="item">{{ item }}</p></details>
          <h3>{{ isActionPreview ? '到目标动作的路径' : nodeId ? '解析结果' : '访问路径' }}</h3>
          <el-timeline class="simulation__steps">
            <el-timeline-item v-for="(step, index) in result.steps" :key="index" :type="statusType(step.status)">
              <div class="simulation__step-title"><strong>{{ step.nodeName || step.nodeId }}</strong><el-tag :type="statusType(step.status)" size="small">{{ statusLabels[step.status] ?? '未知状态' }}</el-tag></div>
              <p v-if="step.blockReason">{{ step.blockReason }}</p>
              <details v-for="action in step.actions ?? []" :key="action.id" class="simulation__details">
                <summary>{{ action.id }} · {{ action.type }} · {{ action.status === 'Skipped' ? '仅预览' : action.status === 'Succeeded' ? '预演成功' : '预演失败' }}</summary>
                <p v-if="action.reason">{{ action.reason }}</p>
                <pre v-if="action.output != null">{{ JSON.stringify(action.output, null, 2) }}</pre>
              </details>
              <p v-for="(evaluation, i) in step.evaluations" :key="i">{{ evaluation.expression || '默认分支' }}：{{ evaluation.matched ? '命中' : '不匹配' }} → {{ evaluation.nextNodeId }}</p>
              <p v-for="assignee in step.resolvedAssignees" :key="`${assignee.kind}:${assignee.value}`">任务 / 抄送对象：{{ assignee.kind }} · {{ assignee.value }}</p>
              <p v-for="assignee in step.excludedAssignees" :key="`${assignee.kind}:${assignee.value}`">已排除：{{ assignee.value }}（自审限制或宿主当前资格不满足）</p>
              <template v-for="assignment in step.assignments" :key="`${assignment.taskAssignee.kind}:${assignment.taskAssignee.value}`">
                <p v-for="member in assignment.members" :key="member.userId">{{ member.name || member.userId }}：{{ member.isEligible === true ? '有效成员' : member.isEligible === null ? '未验证' : '不可审批' }} {{ member.exclusionReason }}</p>
              </template>
              <details v-if="step.resolutionTrace.length" class="simulation__details"><summary>解析说明</summary><p v-for="(trace, i) in step.resolutionTrace" :key="i">{{ trace.type }}：{{ trace.description }}</p></details>
              <small class="simulation__node-id">节点：{{ step.nodeId }}</small>
            </el-timeline-item>
          </el-timeline>
        </template>
      </section>
    </div>
  </el-dialog>
</template>

<style scoped>
.simulation { --simulation-graph-height: 100%; display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 20px; margin-top: 16px; height: min(68vh, 740px); }
.simulation__graph, .simulation__content { min-width: 0; min-height: 0; }
.simulation__content { overflow-y: auto; padding-right: 12px; overflow-wrap: anywhere; }
.simulation__content h3 { font-size: 15px; margin: 0 0 12px; }
.simulation__content p { font-size: 13px; line-height: 1.7; margin: 8px 0; }
.simulation__actions { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin: 12px 0 16px; }
.simulation__actions span, .simulation__node-id { color: #73767a; font-size: 12px; }
.simulation__details { margin: 12px 0 18px; font-size: 13px; }
.simulation__details summary { cursor: pointer; color: #409eff; }
.simulation__steps { padding-left: 8px; }
.simulation__step-title { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
@media (max-width: 800px) { .simulation { --simulation-graph-height: 320px; grid-template-columns: minmax(0, 1fr); height: auto; max-height: 72vh; overflow-y: auto; } .simulation__content { overflow: visible; padding: 0; } }
</style>
