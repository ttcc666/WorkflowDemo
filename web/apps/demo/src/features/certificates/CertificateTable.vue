<script setup lang="ts">
import type { Certificate } from '@/api/types'
import { certTypeLabels } from '@/utils/labels'

defineProps<{ certificates: Certificate[]; loading: boolean }>()

const emit = defineEmits<{
  edit: [certificate: Certificate]
  revoke: [certificate: Certificate]
  history: [certificate: Certificate]
  openPending: [requestId: string]
}>()

const statuses = {
  Valid: { text: '有效', type: 'success' }, NotYetValid: { text: '尚未生效', type: 'warning' },
  Expired: { text: '已过期', type: 'danger' }, Revoked: { text: '已注销', type: 'info' },
} as const

// el-table 插槽中的 row 类型为通用对象，这里显式转换
const cert = (row: unknown) => row as Certificate
</script>

<template>
  <el-table v-loading="loading" :data="certificates" row-key="id" empty-text="暂无证书，点击右上角“申请新增”">
    <el-table-column prop="certNo" label="证书编号" width="130" />
    <el-table-column prop="name" label="证书名称" min-width="160" />
    <el-table-column prop="holder" label="持有人" width="110" />
    <el-table-column label="类型" width="100">
      <template #default="{ row }">{{ certTypeLabels[row.certType] ?? row.certType }}</template>
    </el-table-column>
    <el-table-column label="有效期" width="210">
      <template #default="{ row }">
        <span :class="{ expired: row.effectiveStatus === 'Expired' }">{{ row.issueDate }} ~ {{ row.expireDate }}</span>
      </template>
    </el-table-column>
    <el-table-column label="状态" width="150">
      <template #default="{ row }">
        <el-tag :type="statuses[cert(row).effectiveStatus].type" size="small">
          {{ statuses[cert(row).effectiveStatus].text }}
        </el-tag>
        <el-tag v-if="row.pendingRequestId" type="warning" size="small" effect="dark" class="pending-tag"
          @click="emit('openPending', row.pendingRequestId)">
          审核中
        </el-tag>
      </template>
    </el-table-column>
    <el-table-column label="操作" width="200" fixed="right">
      <template #default="{ row }">
        <!-- 审核中的证书加了编辑锁，不能再提交变更 -->
        <el-tooltip :disabled="!row.pendingRequestId" content="已有审核中的申请" placement="top">
          <span class="action-group">
            <el-button link type="primary" :disabled="!row.canManage || row.status !== 'Active' || !!row.pendingRequestId" @click="emit('edit', cert(row))">变更</el-button>
            <el-button link type="danger" :disabled="!row.canManage || row.status !== 'Active' || !!row.pendingRequestId" @click="emit('revoke', cert(row))">注销</el-button>
          </span>
        </el-tooltip>
        <el-button link @click="emit('history', cert(row))">记录</el-button>
      </template>
    </el-table-column>
  </el-table>
</template>

<style scoped>
.pending-tag {
  margin-left: 6px;
  cursor: pointer;
}

.expired {
  color: #f56c6c;
}

.action-group {
  display: inline-flex;
  margin-right: 12px;
}
</style>
