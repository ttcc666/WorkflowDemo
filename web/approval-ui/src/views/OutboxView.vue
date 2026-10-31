<script setup lang="ts">
import { onBeforeUnmount, shallowRef } from 'vue'
import { ElMessage } from 'element-plus'
import { approvalApi, demoApi } from '@/api/approval'
import { errorMessage } from '@/api/http'
import type { OutboxMessage, OutboxStatus } from '@/api/types'
import { useAsyncData } from '@/composables/useAsyncData'
import { useCurrentUser } from '@/composables/useCurrentUser'
import { currentUserId } from '@/stores/session'
import { formatDateTime, outboxStatusLabels } from '@/utils/labels'

/** 投递记录：外部渠道通知与异步动作的投递状态、重试；以及演示用 Webhook 接收端。 */
const { isAdmin } = useCurrentUser()
const status = shallowRef<OutboxStatus | ''>('')

const outbox = useAsyncData((signal) => approvalApi.outbox(status.value, signal), {
  watch: [status, currentUserId],
  initial: [],
  notifyError: false,
})
const webhook = useAsyncData((signal) => demoApi.webhook(signal), { initial: { failNext: 0, received: [] }, notifyError: false })
const failNext = shallowRef(2)

function refresh() {
  void outbox.reload()
  void webhook.reload()
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

async function simulateFailure() {
  await demoApi.webhookFailNext(failNext.value)
  ElMessage.success(`接收端接下来 ${failNext.value} 次请求将返回 503`)
  refresh()
}

const msg = (r: unknown) => r as OutboxMessage
</script>

<template>
  <el-alert v-if="!isAdmin" type="warning" :closable="false" show-icon title="投递记录仅 Admin 可查看，请在右上角切换到 admin" />
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

    <el-card shadow="never">
      <template #header>
        <div class="outbox__header">
          <span>演示：模拟外部系统（/api/demo/webhook）</span>
          <span class="outbox__actions">
            <span v-if="webhook.data.value?.failNext" class="outbox__warn">还将失败 {{ webhook.data.value.failNext }} 次</span>
            接下来
            <el-input-number v-model="failNext" :min="1" :max="10" size="small" controls-position="right" />
            次返回 503
            <el-button size="small" type="warning" plain @click="simulateFailure">模拟故障</el-button>
          </span>
        </div>
      </template>
      <el-table :data="webhook.data.value?.received ?? []" size="small" empty-text="尚未收到请求">
        <el-table-column label="时间" width="150">
          <template #default="{ row }">{{ formatDateTime(row.receivedAt) }}</template>
        </el-table-column>
        <el-table-column prop="path" label="路径" width="100" />
        <el-table-column prop="idempotencyKey" label="Idempotency-Key" width="300" />
        <el-table-column prop="body" label="内容" min-width="300" show-overflow-tooltip />
      </el-table>
    </el-card>
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

.outbox__actions {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
}

.outbox__warn {
  color: #e6a23c;
}
</style>
