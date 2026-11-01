<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { certificateApi } from '@/api/certificates'
import type { CertificateData } from '@/api/types'
import { useAsyncData, approvalUserKey } from '@approval/ui'
import { formatDateTime } from '@approval/ui'
import { certificateFieldLabels, certTypeLabels, operationLabels, requestStatusLabels } from '@/utils/labels'

/** 审批详情中的证书申请内容：变更前后对比。由 businessPanels 按 BusinessType=Certificate 加载。 */
const props = defineProps<{ businessKey: string }>()
const router = useRouter()

const { data: detail, loading } = useAsyncData((signal) => certificateApi.requestDetail(props.businessKey, signal), {
  watch: [() => props.businessKey, approvalUserKey],
  resetOnReload: true,
})

const fieldOrder = Object.keys(certificateFieldLabels)

function display(field: string, value: unknown) {
  if (value === null || value === undefined || value === '') return '—'
  return field === 'CertType' ? (certTypeLabels[String(value)] ?? String(value)) : String(value)
}

/** 新增、注销时展示完整快照；变更时高亮有变化的字段。 */
const rows = computed(() => {
  const d = detail.value
  if (!d) return []
  const changed = new Map(d.changes.map((c) => [c.field, c]))
  return fieldOrder.map((field) => {
    const camel = (field.charAt(0).toLowerCase() + field.slice(1)) as keyof CertificateData
    const change = changed.get(field)
    return {
      field,
      label: certificateFieldLabels[field],
      before: d.request.operation === 'Create' ? '—' : !d.request.hasHistoricalBaseline ? '历史基线未记录' : display(field, d.request.beforeSnapshot?.[camel]),
      after: display(field, d.request.snapshot[camel]),
      changed: d.request.operation === 'Update' && !!change,
    }
  })
})

const status = computed(() => (detail.value ? requestStatusLabels[detail.value.request.status] : undefined))
</script>

<template>
  <div v-loading="loading">
    <template v-if="detail">
      <el-alert v-if="!detail.request.hasHistoricalBaseline" type="info" :closable="false" title="该历史申请未记录提交前基线，无法还原当时的变更对比。" />
      <el-button v-if="detail.request.canReapply" type="primary" plain class="request-alert"
        @click="router.push({ name: 'certificates', query: { reapply: detail.request.id } })">修改后重新申请</el-button>
      <el-descriptions :column="3" size="small" class="request-meta">
        <el-descriptions-item label="申请类型">
          <el-tag size="small" effect="plain">{{ operationLabels[detail.request.operation] }}证书</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="申请单状态">
          <el-tag v-if="status" size="small" :type="status.type">{{ status.text }}</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="提交时间">{{ formatDateTime(detail.request.createdAt) }}</el-descriptions-item>
        <el-descriptions-item v-if="detail.request.reason" label="原因" :span="3">{{ detail.request.reason }}</el-descriptions-item>
      </el-descriptions>

      <el-alert v-if="detail.request.operation === 'Revoke'" type="warning" :closable="false" show-icon
        title="审批通过后该证书将被注销，不可恢复" class="request-alert" />

      <el-table :data="rows" size="small" border :row-class-name="({ row }) => (row.changed ? 'row-changed' : '')">
        <el-table-column prop="label" label="字段" width="110" />
        <el-table-column v-if="detail.request.operation !== 'Create'" prop="before" label="提交前" />
        <el-table-column prop="after" :label="detail.request.operation === 'Update' ? '变更后' : '申请内容'">
          <template #default="{ row }">
            <span :class="{ 'value-changed': row.changed }">{{ row.after }}</span>
          </template>
        </el-table-column>
      </el-table>
    </template>
  </div>
</template>

<style scoped>
.request-meta {
  margin-bottom: 12px;
}

.request-alert {
  margin-bottom: 12px;
}

.value-changed {
  color: #e6a23c;
  font-weight: 600;
}

:deep(.row-changed) {
  --el-table-tr-bg-color: #fdf6ec;
}
</style>
