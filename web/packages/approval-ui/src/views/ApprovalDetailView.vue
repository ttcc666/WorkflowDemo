<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { approvalApi } from '../api/approval'
import { errorMessage } from '../api/http'
import type { AddSignMode, ApprovalTask } from '../api/types'
import { useApprovalUser } from '../composables/useApprovalUser'
import DynamicForm from '../features/forms/DynamicForm.vue'
import { formOfDefinition, parseFormValues } from '../features/forms/formModel'
import ApprovalActions from '../features/approval/ApprovalActions.vue'
import ApprovalSummary from '../features/approval/ApprovalSummary.vue'
import ApprovalTimeline from '../features/approval/ApprovalTimeline.vue'
import ReassignDialog from '../features/approval/ReassignDialog.vue'
import { businessPanelOf } from '../features/approval/businessPanels'
import { useApprovalDetail } from '../features/approval/useApprovalDetail'
import FlowViewer from '../features/flow/FlowViewer.vue'

const route = useRoute()
const router = useRouter()
const { userId, formBusinessType, isAdmin } = useApprovalUser()

const instanceId = computed(() => route.params.id as string)
const { instance, loading, error, notFound, definition, myTask, canWithdraw, nodeOptions, reload } = useApprovalDetail(instanceId)

const businessPanel = computed(() => (instance.value ? businessPanelOf(instance.value.businessType) : undefined))
// 定义带表单时按实例绑定版本的表单结构展示；表单审批被退回时可修改后重新提交
const formSchema = computed(() => formOfDefinition(definition.value?.definitionJson).form)
const formValues = computed(() => parseFormValues(instance.value?.formDataJson))
const resubmitForm = computed(() =>
  formSchema.value && instance.value?.businessType === formBusinessType.value ? formSchema.value : undefined,
)

const formData = computed(() => {
  try {
    return JSON.stringify(JSON.parse(instance.value?.formDataJson ?? '{}'), null, 2)
  } catch {
    return instance.value?.formDataJson ?? ''
  }
})

const busy = shallowRef(false)

async function run(action: () => Promise<unknown>, successText: string) {
  busy.value = true
  try {
    await action()
    ElMessage.success(successText)
  } catch (e) {
    ElMessage.error(errorMessage(e))
  } finally {
    busy.value = false
    // 无论成功失败都刷新：失败可能是并发冲突，需要看到最新状态
    await reload()
  }
}

const taskId = () => myTask.value!.id
const onApprove = (comment: string) => run(() => approvalApi.approve(taskId(), comment), '已同意')
const onReject = (comment: string, target: string | null) =>
  run(() => approvalApi.reject(taskId(), comment, target ?? undefined), target ? '已退回' : '已驳回')
const onAddSign = (userIds: string[], mode: AddSignMode, comment: string) =>
  run(() => approvalApi.addSign(taskId(), userIds, mode, comment), `已加签 ${userIds.join('、')}`)
const onResubmit = (comment: string, data?: Record<string, unknown>) =>
  run(() => approvalApi.resubmit(taskId(), comment, data), '已重新提交')
const onTransfer = (to: string, comment: string) => run(() => approvalApi.transfer(taskId(), to, comment), `已转交给 ${to}`)
const onWithdraw = (comment: string) => run(() => approvalApi.withdraw(instanceId.value, comment), '已撤回')

// ───── 管理员：终止流程、改派待办 ─────
const canAdminister = computed(() => isAdmin.value && instance.value?.status === 'Running')
const onTerminate = (reason: string) => run(() => approvalApi.terminate(instanceId.value, reason), '已终止流程')
const reassignVisible = shallowRef(false)
const reassignTask = shallowRef<ApprovalTask | null>(null)
function openReassign(task: ApprovalTask) {
  reassignTask.value = task
  reassignVisible.value = true
}
const onReassign = (task: ApprovalTask, to: string, reason: string) =>
  run(() => approvalApi.reassign(task.id, to, reason), `已改派给 ${to}`)
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
      <ApprovalActions
        :my-task="myTask"
        :can-withdraw="canWithdraw"
        :can-terminate="canAdminister"
        :current-user-id="userId"
        :initiator-id="instance.initiatorId"
        :allow-return="nodeOptions.allowReturn"
        :allow-add-sign="nodeOptions.allowAddSign"
        :busy="busy"
        :resubmit-form="resubmitForm"
        :form-data="formValues"
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
            <DynamicForm v-else-if="formSchema" :key="instance.formDataJson" :model-value="formValues" :schema="formSchema" readonly />
            <pre v-else class="form-data">{{ formData }}</pre>
          </el-card>
          <el-card shadow="never" header="流程图">
            <FlowViewer v-if="definition" :definition-json="definition.definitionJson" :instance="instance" />
            <el-skeleton v-else :rows="6" animated />
          </el-card>
        </el-col>
        <el-col :span="10" class="detail-col">
          <el-card shadow="never" header="审批轨迹">
            <ApprovalTimeline :logs="instance.logs" :tasks="instance.tasks" :can-reassign="canAdminister" @reassign="openReassign" />
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

.form-data {
  margin: 0;
  white-space: pre-wrap;
}
</style>
