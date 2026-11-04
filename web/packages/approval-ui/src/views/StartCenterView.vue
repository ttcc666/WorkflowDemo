<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useCommandIntent } from '../composables/useCommandIntent'
import { useApprovalClient } from '../client'
import { errorMessage } from '../api/http'
import type { DraftSummary, StartableFlow } from '../contracts/api'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import { approvalRouteNames } from '../routes'
import { formatDateTime } from '../utils/labels'

const { api: approvalApi, userKey: approvalUserKey } = useApprovalClient()

/** 发起中心：我的草稿 + 按分类列出当前用户可发起的流程（设置了“在发起中心显示”）。 */
const router = useRouter()
const intent = useCommandIntent('draft-delete')
const { can, capabilities } = useApprovalUser()
const keyword = shallowRef('')
const flows = useAsyncData((signal) => can('approval.instance.start') ? approvalApi.startableForms(signal) : Promise.resolve([]), { watch: [approvalUserKey, capabilities], initial: [] })
const drafts = useAsyncData((signal) => can('approval.draft.read') ? approvalApi.drafts(signal) : Promise.resolve([]), { watch: [approvalUserKey, capabilities], initial: [], notifyError: false })

const groups = computed(() => {
  const text = keyword.value.trim().toLowerCase()
  const matched = (flows.data.value ?? []).filter(
    (f) => !text || f.name.toLowerCase().includes(text) || (f.description ?? '').toLowerCase().includes(text),
  )
  const map = new Map<string, StartableFlow[]>()
  for (const flow of matched) {
    const category = flow.category || '其他'
    map.set(category, [...(map.get(category) ?? []), flow])
  }
  return [...map].map(([name, items]) => ({ name, items }))
})

const open = (flow: StartableFlow) => router.push({ name: approvalRouteNames.startForm, params: { key: flow.key } })
const openDraft = (draft: DraftSummary) =>
  router.push({ name: approvalRouteNames.startForm, params: { key: draft.definitionKey }, query: { draft: draft.id } })

async function removeDraft(draft: DraftSummary) {
  try {
    await ElMessageBox.confirm(`删除「${draft.definitionName}」的草稿？`, '删除草稿', { type: 'warning' })
  } catch {
    return
  }
  try {
    await intent.run(draft.id, {}, (_, operationId) => approvalApi.deleteDraft(draft.id, operationId))
    ElMessage.success('已删除')
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
  await drafts.reload()
}
</script>

<template>
  <el-card v-if="drafts.data.value?.length" shadow="never" class="start__drafts">
    <template #header>我的草稿（{{ drafts.data.value.length }}）</template>
    <el-table :data="drafts.data.value" size="small" @row-click="(row: DraftSummary) => openDraft(row)">
      <el-table-column label="流程" min-width="160">
        <template #default="{ row }"><el-link type="primary" underline="never">{{ row.definitionName }}</el-link></template>
      </el-table-column>
      <el-table-column prop="comment" label="说明" min-width="200" show-overflow-tooltip />
      <el-table-column label="最后保存" width="170">
        <template #default="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
      </el-table-column>
      <el-table-column label="" width="140" align="right">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click.stop="openDraft(row as DraftSummary)">继续编辑</el-button>
          <el-button v-if="can('approval.draft.delete')" link type="danger" size="small" @click.stop="removeDraft(row as DraftSummary)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-card>

  <el-card v-loading="flows.loading.value" shadow="never">
    <template #header>
      <div class="start__header">
        <span>发起申请</span>
        <el-input v-model="keyword" placeholder="搜索流程" clearable class="start__search" />
      </div>
    </template>

    <el-empty v-if="!flows.loading.value && groups.length === 0"
      :description="keyword ? '没有匹配的流程' : '暂无可发起的流程。管理员可在流程设计器的“发起设置”中开启“在发起中心显示”'" />

    <section v-for="group in groups" :key="group.name" class="start__group">
      <div class="start__group-title">{{ group.name }}</div>
      <div class="start__cards">
        <div v-for="flow in group.items" :key="flow.key" class="start__card" role="button" tabindex="0" @click="open(flow)"
          @keydown.enter="open(flow)">
          <div class="start__card-icon">{{ flow.name.slice(0, 1) }}</div>
          <div class="start__card-body">
            <div class="start__card-name">{{ flow.name }}</div>
            <div class="start__card-desc">{{ flow.description || '点击填写申请' }}</div>
          </div>
        </div>
      </div>
    </section>
  </el-card>
</template>

<style scoped>
.start__drafts {
  margin-bottom: 16px;
}

.start__drafts :deep(.el-table__row) {
  cursor: pointer;
}

.start__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.start__search {
  width: 240px;
}

.start__group + .start__group {
  margin-top: 20px;
}

.start__group-title {
  margin-bottom: 10px;
  color: #606266;
  font-weight: 600;
}

.start__cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 12px;
}

.start__card {
  display: flex;
  gap: 12px;
  padding: 14px;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  background: #fff;
  cursor: pointer;
  transition: border-color 0.15s, box-shadow 0.15s;
}

.start__card:hover,
.start__card:focus-visible {
  border-color: var(--el-color-primary-light-5);
  box-shadow: 0 2px 8px rgb(64 158 255 / 12%);
  outline: none;
}

.start__card-icon {
  display: flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 8px;
  background: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  font-size: 18px;
  font-weight: 600;
}

.start__card-body {
  min-width: 0;
}

.start__card-name {
  color: #303133;
  font-weight: 600;
}

.start__card-desc {
  display: -webkit-box;
  overflow: hidden;
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}
</style>
