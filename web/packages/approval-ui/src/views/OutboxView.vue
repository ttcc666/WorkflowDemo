<script setup lang="ts">
import { onBeforeUnmount, shallowRef } from 'vue'
import { ElMessage } from 'element-plus'
import { approvalApi } from '../api/approval'
import { errorMessage } from '../api/http'
import type { OutboxMessage, OutboxStatus } from '../api/types'
import { useAsyncData } from '../composables/useAsyncData'
import { useApprovalUser } from '../composables/useApprovalUser'
import { approvalConfig, approvalUserKey } from '../config'
import { formatDateTime, outboxStatusLabels } from '../utils/labels'

/** 投递记录：外部渠道通知与异步动作的投递状态、重试。宿主可通过 outboxPanel 选项在下方追加面板。 */
const { isAdmin } = useApprovalUser()
const outboxPanel = approvalConfig().outboxPanel
const status = shallowRef<OutboxStatus | ''>('')

const outbox = useAsyncData((signal) => approvalApi.outbox(status.value, signal), {
  watch: [status, approvalUserKey],
  initial: [],
  notifyError: false,
})

function refresh() {
  void outbox.reload()
}
const timer = window.setInterval(refresh, 3000)
onBeforeUnmount(() => window.clearInterval(timer))

async function retry(message: OutboxMessage) {
  try {
    await approvalApi.retryOutbox(message.id)
    ElMessage.success('已重新投递')
    refresh()
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
}

const msg = (r: unknown) => r as OutboxMessage
</script>

<template>
  <el-alert v-if="!isAdmin" type="warning" :closable="false" show-icon title="投递记录仅管理员可查看" />
  <div v-else class="outbox">
    <el-card shadow="never">
      <template #header>
        <div class="outbox__header">
          <span>投递记录（Outbox）</span>
          <el-radio-group v-model="status" size="small">
            <el-radio-button value="">全部</el-radio-button>
            <el-radio-button v-for="(l, s) in outboxStatusLabels" :key="s" :value="s">{{ l.text }}</el-radio-button>
          </el-radio-group>
        </div>
      </template>
      <el-table :data="outbox.data.value" row-key="id" empty-text="暂无记录" size="small">
        <el-table-column label="类型" width="90">
          <template #default="{ row }">{{ msg(row).kind === 'Action' ? '异步动作' : '通知' }}</template>
        </el-table-column>
        <el-table-column prop="target" label="渠道 / 动作" width="110" />
        <el-table-column prop="description" label="说明" min-width="220" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag size="small" :type="outboxStatusLabels[msg(row).status].type">{{ outboxStatusLabels[msg(row).status].text }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="次数" width="70">
          <template #default="{ row }">{{ msg(row).attempts }}/{{ msg(row).maxAttempts }}</template>
        </el-table-column>
        <el-table-column label="下次重试" width="150">
          <template #default="{ row }">
            {{ msg(row).status === 'Pending' && msg(row).attempts ? formatDateTime(msg(row).nextAttemptAt) : '-' }}
          </template>
        </el-table-column>
        <el-table-column prop="lastError" label="最后错误" min-width="200" show-overflow-tooltip />
        <el-table-column label="创建时间" width="150">
          <template #default="{ row }">{{ formatDateTime(msg(row).createdAt) }}</template>
        </el-table-column>
        <el-table-column label="" width="70">
          <template #default="{ row }">
            <el-button v-if="msg(row).status !== 'Succeeded'" link type="primary" size="small" @click="retry(msg(row))">重试</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <component :is="outboxPanel" v-if="outboxPanel" />
  </div>
</template>

<style scoped>
.outbox {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.outbox__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

</style>
