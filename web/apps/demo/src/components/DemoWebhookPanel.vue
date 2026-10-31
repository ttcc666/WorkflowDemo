<script setup lang="ts">
import { onBeforeUnmount, shallowRef } from 'vue'
import { ElMessage } from 'element-plus'
import { errorMessage, formatDateTime, useAsyncData } from '@approval/ui'
import { demoApi } from '@/api/demo'

/** 演示：模拟外部系统（Webhook 接收端），可让接下来的请求失败以观察投递重试。挂在投递记录页底部。 */
const webhook = useAsyncData((signal) => demoApi.webhook(signal), { initial: { failNext: 0, received: [] }, notifyError: false })
const failNext = shallowRef(2)

const timer = window.setInterval(() => void webhook.reload(), 3000)
onBeforeUnmount(() => window.clearInterval(timer))

async function simulateFailure() {
  try {
    await demoApi.webhookFailNext(failNext.value)
    ElMessage.success(`接收端接下来 ${failNext.value} 次请求将返回 503`)
    void webhook.reload()
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
}
</script>

<template>
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
</template>

<style scoped>
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
