<script setup lang="ts">
import { computed, ref, shallowRef, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useCommandIntent } from '../composables/useCommandIntent'
import { useApprovalClient } from '../client'
import { ApiError, errorMessage } from '../api/http'
import type { AddSignMode, ApprovalTask } from '../contracts/api'
import { useApprovalUser } from '../composables/useApprovalUser'
import DynamicForm from '../features/forms/DynamicForm.vue'
import {
  changedFields,
  formOfDefinition,
  initialValues,
  parseFormValues,
  pickValues,
  withoutFields,
  type FormValues,
} from '../features/forms/formModel'
import ApprovalActions from '../features/approval/ApprovalActions.vue'
import ApprovalSummary from '../features/approval/ApprovalSummary.vue'
import ApprovalTimeline from '../features/approval/ApprovalTimeline.vue'
import ReassignDialog from '../features/approval/ReassignDialog.vue'
import { businessPanelOf } from '../features/approval/businessPanels'
import { useApprovalDetail } from '../features/approval/useApprovalDetail'
import FlowViewer from '../features/flow/FlowViewer.vue'

const { api: approvalApi, config: approvalConfig } = useApprovalClient()

const intent = useCommandIntent('approval-detail')
const route = useRoute()
const router = useRouter()
const { userId, formBusinessType } = useApprovalUser()

const instanceId = computed(() => route.params.id as string)
const { instance, loading, error, notFound, definition, myTask, editableFields, taskCapabilities, canWithdraw, nodeOptions, reload } =
  useApprovalDetail(instanceId)

const businessPanel = computed(() => (instance.value ? businessPanelOf(instance.value.businessType, approvalConfig().businessPanels) : undefined))
// 定义带表单时按实例绑定版本的表单结构展示，去掉当前用户看不到的隐藏字段；表单审批被退回时可修改后重新提交
const formSchema = computed(() => {
  const form = formOfDefinition(definition.value?.definitionJson).form
  return form && withoutFields(form, instance.value?.hiddenFields ?? [])
})
const formValues = computed(() => parseFormValues(instance.value?.formDataJson))
const isFormApproval = computed(() => !!instance.value && instance.value.businessType === formBusinessType.value)
const resubmitForm = computed(() => (isFormApproval.value ? formSchema.value : undefined))

const formData = computed(() => {
  try {
    return JSON.stringify(JSON.parse(instance.value?.formDataJson ?? '{}'), null, 2)
  } catch {
    return instance.value?.formDataJson ?? ''
  }
})

// ───── 审批人修改本节点的可编辑字段：在“申请内容”中直接修改，同意时一并提交 ─────
const editing = computed(() => !!formSchema.value && isFormApproval.value && editableFields.value.length > 0 && taskCapabilities.value['approval.task.approve'] === true)
// 被 DynamicForm 就地修改，需要深层响应
const editData = ref<FormValues>({})
const editErrors = shallowRef<Record<string, string[]>>()
const contentForm = useTemplateRef<InstanceType<typeof DynamicForm>>('contentForm')
watch([formValues, formSchema, editing], () => {
  editData.value = editing.value ? initialValues(formSchema.value, formValues.value) : {}
  editErrors.value = undefined
}, { immediate: true })
const pendingChanges = computed(() =>
  editing.value ? changedFields(formSchema.value, formValues.value, editData.value, editableFields.value).map((f) => f.label) : [],
)

const busy = shallowRef(false)
async function restartIntent() {
  const scope = intent.pending.value
  if (!scope) return
  try {
    await ElMessageBox.confirm('请先刷新并核对审批轨迹中的上次操作结果。确认后，将允许提交新的内容。', '开始新操作', { type: 'warning' })
    intent.reset(scope)
  } catch { /* Preserve the original intent for retry. */ }
}

async function run(action: () => Promise<unknown>, successText: string) {
  busy.value = true
  let refresh = true
  try {
    await action()
    ElMessage.success(successText)
  } catch (e) {
    // 修改的字段校验失败：错误显示在对应字段下，保留已填写的内容
    if (e instanceof ApiError && e.errors) {
      editErrors.value = e.errors
      refresh = false
    }
    ElMessage.error(errorMessage(e))
  } finally {
    busy.value = false
    // 其他情况无论成功失败都刷新：失败可能是并发冲突，需要看到最新状态
    if (refresh) await reload()
  }
}

const taskId = () => myTask.value!.id

async function onApprove(comment: string) {
  if (editing.value && !(await contentForm.value?.validate())) {
    ElMessage.warning('申请内容中有填写错误，请修改后再同意')
    return
  }
  const changes = pendingChanges.value.length ? pickValues(editData.value, editableFields.value) : undefined
  await run(() => intent.run(`approve:${taskId()}`, { taskId: taskId(), comment, changes }, (body, id) => approvalApi.approve(body.taskId, body.comment, body.changes, id)), '已同意')
}

const onReject = (comment: string, target: string | null) =>
  run(() => intent.run(`reject:${taskId()}`, { taskId: taskId(), comment, target }, (body, id) => approvalApi.reject(body.taskId, body.comment, body.target ?? undefined, id)), target ? '已退回' : '已驳回')
const onAddSign = (userIds: string[], mode: AddSignMode, comment: string) =>
  run(() => intent.run(`add-sign:${taskId()}`, { taskId: taskId(), userIds, mode, comment }, (body, id) => approvalApi.addSign(body.taskId, body.userIds, body.mode, body.comment, id)), `已加签 ${userIds.join('、')}`)
const onResubmit = (comment: string, data?: Record<string, unknown>) =>
  run(() => intent.run(`resubmit:${taskId()}`, { taskId: taskId(), comment, data }, (body, id) => approvalApi.resubmit(body.taskId, body.comment, body.data, id)), '已重新提交')
const onTransfer = (to: string, comment: string) => run(() => intent.run(`transfer:${taskId()}`, { taskId: taskId(), to, comment }, (body, id) => approvalApi.transfer(body.taskId, body.to, body.comment, id)), `已转交给 ${to}`)
const onWithdraw = (comment: string) => run(() => intent.run(`withdraw:${instanceId.value}`, { instanceId: instanceId.value, comment }, (body, id) => approvalApi.withdraw(body.instanceId, body.comment, id)), '已撤回')

// ───── 管理员：终止流程、改派待办 ─────
const canTerminate = computed(() => instance.value?.status === 'Running' && instance.value.capabilities?.['approval.instance.terminate'] === true)
const canReassign = computed(() => instance.value?.status === 'Running')
const onTerminate = (reason: string) => run(() => intent.run(`terminate:${instanceId.value}`, { instanceId: instanceId.value, reason }, (body, id) => approvalApi.terminate(body.instanceId, body.reason, id)), '已终止流程')
const reassignVisible = shallowRef(false)
const reassignTask = shallowRef<ApprovalTask | null>(null)
function openReassign(task: ApprovalTask) {
  reassignTask.value = task
  reassignVisible.value = true
}
const onReassign = (task: ApprovalTask, to: string, reason: string) =>
  run(() => intent.run(`reassign:${task.id}`, { taskId: task.id, to, reason }, (body, id) => approvalApi.reassign(body.taskId, body.to, body.reason, id)), `已改派给 ${to}`)
</script>

<template>
  <div v-loading="loading && !instance" class="detail-page">
    <el-page-header class="detail-header" title="返回" @back="router.back()">
      <template #content>审批详情</template>
    </el-page-header>

    <el-result v-if="notFound" icon="warning" title="无权查看或不存在"
      sub-title="只有发起人、审批人、抄送人和管理员可以查看该审批" />
    <el-result v-else-if="error && !instance" icon="error" title="加载失败" :sub-title="error" />

    <template v-if="instance">
      <el-button v-if="intent.pending.value" :disabled="busy" @click="restartIntent">确认上次结果后开始新操作</el-button>
      <ApprovalActions
        :my-task="myTask"
        :can-withdraw="canWithdraw"
        :can-terminate="canTerminate"
        :capabilities="taskCapabilities"
        :current-user-id="userId"
        :initiator-id="instance.initiatorId"
        :allow-return="nodeOptions.allowReturn"
        :allow-add-sign="nodeOptions.allowAddSign"
        :allow-transfer="nodeOptions.allowTransfer"
        :busy="busy"
        :resubmit-form="resubmitForm"
        :form-data="formValues"
        :pending-changes="pendingChanges"
        @approve="onApprove"
        @reject="onReject"
        @transfer="onTransfer"
        @withdraw="onWithdraw"
        @add-sign="onAddSign"
        @resubmit="onResubmit"
        @terminate="onTerminate"
      />

      <el-card shadow="never">
        <ApprovalSummary :instance="instance" :definition-name="definition?.name" />
      </el-card>

      <el-row :gutter="16">
        <el-col :span="14" class="detail-col">
          <el-card shadow="never" header="申请内容">
            <!-- 审批状态变化后重新挂载，刷新业务单据状态 -->
            <component :is="businessPanel" v-if="businessPanel" :key="instance.status" :business-key="instance.businessKey" />
            <template v-else-if="formSchema">
              <template v-if="editing">
                <el-alert type="info" :closable="false" show-icon class="detail-edit-hint"
                  title="标有「可修改」的字段可直接修改，同意时一并提交；驳回、转交等操作不保存修改" />
                <DynamicForm ref="contentForm" v-model="editData" :schema="formSchema" readonly :editable-fields="editableFields"
                  :errors="editErrors" />
              </template>
              <DynamicForm v-else :key="instance.formDataJson" :model-value="formValues" :schema="formSchema" readonly />
            </template>
            <pre v-else class="form-data">{{ formData }}</pre>
          </el-card>
          <el-card shadow="never" header="流程图">
            <FlowViewer v-if="definition" :definition-json="definition.definitionJson" :instance="instance" />
            <el-skeleton v-else :rows="6" animated />
          </el-card>
        </el-col>
        <el-col :span="10" class="detail-col">
          <el-card shadow="never" header="审批轨迹">
            <ApprovalTimeline :logs="instance.logs" :tasks="instance.tasks" :form="formSchema" :can-reassign="canReassign"
              @reassign="openReassign" />
          </el-card>
        </el-col>
      </el-row>

      <ReassignDialog v-model:visible="reassignVisible" :task="reassignTask" :initiator-id="instance.initiatorId" @confirm="onReassign" />
    </template>
  </div>
</template>

<style scoped>
.detail-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.detail-col {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.detail-edit-hint {
  margin-bottom: 12px;
}

.form-data {
  margin: 0;
  white-space: pre-wrap;
}
</style>
