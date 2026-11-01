<script setup lang="ts">
import { onBeforeUnmount, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useApprovalClient } from '../client'
import type { ApprovalMessage } from '../contracts/api'
import { approvalRouteNames } from '../routes'
import { formatDateTime } from '../utils/labels'

const { api: approvalApi, userKey: approvalUserKey } = useApprovalClient()

/** 站内信：铃铛显示未读数，展开查看最近消息，点击跳转审批详情并标记已读。 */
const router = useRouter()
const unread = shallowRef(0)
const messages = shallowRef<ApprovalMessage[]>([])
const loading = shallowRef(false)

async function refreshCount() {
  try {
    unread.value = (await approvalApi.unreadCount()).count
  } catch {
    // 轮询失败忽略，下一轮再试
  }
}

async function loadMessages() {
  loading.value = true
  try {
    messages.value = await approvalApi.messages()
  } finally {
    loading.value = false
  }
}

async function open(message: ApprovalMessage) {
  if (!message.isRead) {
    await approvalApi.markRead(message.id)
    void refreshCount()
    void loadMessages()
  }
  if (message.instanceId) void router.push({ name: approvalRouteNames.detail, params: { id: message.instanceId } })
}

async function readAll() {
  await approvalApi.markAllRead()
  await Promise.all([refreshCount(), loadMessages()])
}

watch(approvalUserKey, refreshCount, { immediate: true })
const timer = window.setInterval(refreshCount, 10000)
onBeforeUnmount(() => window.clearInterval(timer))
</script>

<template>
  <el-popover placement="bottom-end" :width="380" trigger="click" @show="loadMessages">
    <template #reference>
      <el-badge :value="unread" :hidden="unread === 0" :max="99">
        <el-button circle title="站内信">🔔</el-button>
      </el-badge>
    </template>

    <div class="bell__header">
      <span>站内信</span>
      <el-button link type="primary" size="small" :disabled="unread === 0" @click="readAll">全部已读</el-button>
    </div>
    <el-scrollbar v-loading="loading" max-height="420px">
      <el-empty v-if="!messages.length" :image-size="60" description="暂无消息" />
      <div v-for="m in messages" :key="m.id" :class="['bell__item', { 'bell__item--unread': !m.isRead }]" @click="open(m)">
        <div class="bell__title">{{ m.title }}</div>
        <div class="bell__content">{{ m.content }}</div>
        <div class="bell__time">{{ formatDateTime(m.createdAt) }}</div>
      </div>
    </el-scrollbar>
  </el-popover>
</template>

<style scoped>
.bell__header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 6px;
  font-weight: 600;
}

.bell__item {
  padding: 8px;
  border-bottom: 1px solid #f2f3f5;
  cursor: pointer;
}

.bell__item:hover {
  background: #f5f7fa;
}

.bell__item--unread .bell__title::before {
  content: '';
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-right: 6px;
  border-radius: 50%;
  background: #f56c6c;
  vertical-align: middle;
}

.bell__title {
  color: #303133;
  font-weight: 600;
}

.bell__content {
  margin-top: 2px;
  color: #606266;
  font-size: 13px;
}

.bell__time {
  margin-top: 2px;
  color: #c0c4cc;
  font-size: 12px;
}
</style>
