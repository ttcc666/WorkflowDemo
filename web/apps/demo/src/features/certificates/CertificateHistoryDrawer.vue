<script setup lang="ts">
import { certificateApi } from '@/api/certificates'
import type { Certificate } from '@/api/types'
import { usePagedList, approvalUserKey } from '@approval/ui'
import { formatDateTime } from '@approval/ui'
import { operationLabels, requestStatusLabels } from '@/utils/labels'

/** 证书的申请记录（新增、变更、注销及其审核结果）。 */
const props = defineProps<{ certificate: Certificate }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ openRequest: [requestId: string] }>()

const { items: requests, loading, page, pageSize, total } = usePagedList((query, signal) => certificateApi.requests(props.certificate.id, query, signal), {
  resetOn: [() => props.certificate.id, approvalUserKey],
})
</script>

<template>
  <el-drawer v-model="visible" :title="`申请记录 · ${certificate.certNo}`" size="520px">
    <el-timeline v-loading="loading">
      <el-timeline-item v-for="r in requests" :key="r.id" :timestamp="formatDateTime(r.createdAt)" placement="top">
        <div class="request-row">
          <strong>{{ operationLabels[r.operation] }}</strong>
          <el-tag size="small" :type="requestStatusLabels[r.status].type">{{ requestStatusLabels[r.status].text }}</el-tag>
          <span class="request-applicant">申请人 {{ r.applicantId }}</span>
          <el-button link type="primary" @click="emit('openRequest', r.id)">审批详情</el-button>
        </div>
        <div v-if="r.reason" class="request-reason">{{ r.reason }}</div>
      </el-timeline-item>
    </el-timeline>
    <el-empty v-if="!loading && !requests?.length" description="暂无记录" />
    <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" layout="total, prev, pager, next" />
  </el-drawer>
</template>

<style scoped>
.request-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.request-applicant {
  margin-left: auto;
  color: #909399;
}

.request-reason {
  margin-top: 4px;
  color: #606266;
}
</style>
