<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { approvalApi } from '@/api/approval'
import { errorMessage } from '@/api/http'
import type { AddSignMode } from '@/api/types'
import { useCurrentUser } from '@/composables/useCurrentUser'
import ApprovalActions from '@/features/approval/ApprovalActions.vue'
import ApprovalSummary from '@/features/approval/ApprovalSummary.vue'
import ApprovalTimeline from '@/features/approval/ApprovalTimeline.vue'
import { businessPanels } from '@/features/approval/businessPanels'
import { useApprovalDetail } from '@/features/approval/useApprovalDetail'
import FlowViewer from '@/features/flow/FlowViewer.vue'

const route = useRoute()
const router = useRouter()
const { userId, users } = useCurrentUser()

const instanceId = computed(() => route.params.id as string)
const { instance, loading, error, definition, myTask, canWithdraw, nodeOptions, reload } = useApprovalDetail(instanceId)

const businessPanel = computed(() => (instance.value ? businessPanels[instance.value.businessType] : undefined))
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
const onResubmit = (comment: string) => run(() => approvalApi.resubmit(taskId(), comment), '已重新提交')
const onTransfer = (to: string, comment: string) => run(() => approvalApi.transfer(taskId(), to, comment), `已转交给 ${to}`)
const onWithdraw = (comment: string) => run(() => approvalApi.withdraw(instanceId.value, comment), '已撤回')
</script>

<template>
  <div v-loading="loading && !instance" class="detail-page">
    <el-page-header class="detail-header" title="返回" @back="router.back()">
      <template #content>审批详情</template>
    </el-page-header>

    <el-result v-if="error && !instance" icon="error" title="加载失败" :sub-title="error" />

    <template v-if="instance">
      <ApprovalActions
        :my-task="myTask"
        :can-withdraw="canWithdraw"
        :users="users"
        :current-user-id="userId"
        :initiator-id="instance.initiatorId"
        :allow-return="nodeOptions.allowReturn"
        :allow-add-sign="nodeOptions.allowAddSign"
        :busy="busy"
        @approve="onApprove"
        @reject="onReject"
        @transfer="onTransfer"
        @withdraw="onWithdraw"
        @add-sign="onAddSign"
        @resubmit="onResubmit"
      />

      <el-card shadow="never">
        <ApprovalSummary :instance="instance" />
      </el-card>

      <el-row :gutter="16">
        <el-col :span="14" class="detail-col">
          <el-card shadow="never" header="申请内容">
            <!-- 审批状态变化后重新挂载，刷新业务单据状态 -->
            <component :is="businessPanel" v-if="businessPanel" :key="instance.status" :business-key="instance.businessKey" />
            <pre v-else class="form-data">{{ formData }}</pre>
          </el-card>
          <el-card shadow="never" header="流程图">
            <FlowViewer v-if="definition" :definition-json="definition.definitionJson" :instance="instance" />
            <el-skeleton v-else :rows="6" animated />
          </el-card>
        </el-col>
        <el-col :span="10" class="detail-col">
          <el-card shadow="never" header="审批轨迹">
            <ApprovalTimeline :logs="instance.logs" :tasks="instance.tasks" />
          </el-card>
        </el-col>
      </el-row>
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
