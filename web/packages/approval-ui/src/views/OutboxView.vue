<script setup lang="ts">
import CommandRecovery from '@ui/components/CommandRecovery.vue'
import { onBeforeUnmount } from 'vue'
import { ElMessage } from 'element-plus'
import { useCommandIntent } from '@ui/composables/useCommandIntent'
import { useApprovalClient } from '@ui/client'
import { errorMessage } from '@ui/api/http'
import type { OutboxMessage } from '@ui/contracts/api'
import { useApprovalUser } from '@ui/composables/useApprovalUser'
import { usePagedList } from '@ui/composables/usePagedList'
import { formatDateTime, outboxStatusLabels } from '@ui/utils/labels'

const { api: approvalApi, userKey: approvalUserKey, config: approvalConfig } = useApprovalClient()

/** 投递记录：外部渠道通知与可靠事件的投递状态、重试（分页，状态与页码同步到地址栏）。宿主可通过 outboxPanel 选项在下方追加面板。 */
const { can } = useApprovalUser()
const intent = useCommandIntent('outbox-retry')
const outboxPanel = approvalConfig().outboxPanel

const outbox = usePagedList((query, signal) => approvalApi.outbox(query, signal), {
  routePrefix: '',
  resetOn: [approvalUserKey],
  notifyError: false,
})

function refresh() {
  if (can('approval.outbox.read')) void outbox.reload()
}
const timer = window.setInterval(refresh, 3000)
onBeforeUnmount(() => window.clearInterval(timer))

async function retry(message: OutboxMessage) {
  try {
    await intent.run(message.id, {}, (_, operationId) => approvalApi.retryOutbox(message.id, operationId))
    ElMessage.success('已安排重新投递')
    refresh()
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
}

const msg = (r: unknown) => r as OutboxMessage
</script>

<template>
  <el-alert v-if="!can('approval.outbox.read')" type="warning" :closable="false" show-icon title="没有投递记录查看权限" />
  <div v-else class="outbox">
    <CommandRecovery :intent="intent" @recovered="refresh" />
    <el-card shadow="never">
      <template #header>
        <div class="outbox__header">
          <span>投递记录（Outbox）</span>
          <el-radio-group v-model="outbox.filters.status" size="small" @change="outbox.search">
            <el-radio-button value="">全部</el-radio-button>
            <el-radio-button v-for="(l, s) in outboxStatusLabels" :key="s" :value="s">{{ l.text }}</el-radio-button>
          </el-radio-group>
        </div>
      </template>
      <el-table :data="outbox.items.value" row-key="id" empty-text="暂无记录" size="small">
        <el-table-column prop="target" label="渠道 / 事件" width="110" />
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
            <el-button v-if="can('approval.outbox.retry') && msg(row).status !== 'Succeeded'" link type="primary" size="small" @click="retry(msg(row))">重试</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination v-model:current-page="outbox.page.value" v-model:page-size="outbox.pageSize.value" :total="outbox.total.value"
        :page-sizes="[20, 50, 100]" layout="total, sizes, prev, pager, next" background size="small" class="outbox__pager" />
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

.outbox__pager {
  justify-content: flex-end;
  margin-top: 12px;
}

</style>
