<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useApprovalClient } from '../client'
import { useAsyncData } from '../composables/useAsyncData'
import { usePagedList } from '../composables/usePagedList'
import { approvalRouteNames } from '../routes'
import ListFilterBar from '../features/workbench/ListFilterBar.vue'
import TodoTable from '../features/workbench/TodoTable.vue'
import InstanceTable from '../features/workbench/InstanceTable.vue'
import CarbonCopyTable from '../features/workbench/CarbonCopyTable.vue'
import type { CarbonCopyItem } from '../contracts/api'
import { ElMessage } from 'element-plus'
import { errorMessage } from '../api/http'
import { useCommandIntent } from '../composables/useCommandIntent'

const { api: approvalApi, userKey: approvalUserKey } = useApprovalClient()

type Tab = 'todo' | 'done' | 'started' | 'cc'
const tabs: Tab[] = ['todo', 'done', 'started', 'cc']

const route = useRoute()
const router = useRouter()

const tab = computed<Tab>({
  get: () => (tabs.includes(route.query.tab as Tab) ? (route.query.tab as Tab) : 'todo'),
  set: (value) => router.replace({ query: { ...route.query, tab: value } }),
})

// 各列表的页码与筛选同步到路由 query（键带列表前缀）；切换用户后回到第一页重新加载
const listOptions = (prefix: string) => ({ routePrefix: prefix, resetOn: [approvalUserKey] })
const todo = usePagedList((q, s) => approvalApi.todo(q, s), listOptions('todo_'))
const done = usePagedList((q, s) => approvalApi.done(q, s), listOptions('done_'))
const started = usePagedList((q, s) => approvalApi.started(q, s), listOptions('started_'))
const cc = usePagedList((q, s) => approvalApi.carbonCopies(q, s), listOptions('cc_'))
const ccIntent = useCommandIntent('carbon-copy-read')

async function openCopy(item: CarbonCopyItem) {
  try {
    if (!item.readAt) await ccIntent.run(item.id, {}, (_, key) => approvalApi.readCarbonCopy(item.id, key))
    open(item.instanceId)
  } catch (error) { ElMessage.error(errorMessage(error)) }
}

const todoCount = useAsyncData(async (signal) => (await approvalApi.todoCount(signal)).count, {
  watch: [approvalUserKey],
  initial: 0,
  notifyError: false,
})
const flows = useAsyncData((signal) => approvalApi.flows(signal), { initial: [], notifyError: false })

function refresh() {
  void todoCount.reload()
  void { todo, done, started, cc }[tab.value].reload()
}

function open(instanceId: string) {
  void router.push({ name: approvalRouteNames.detail, params: { id: instanceId } })
}

const pageSizes = [10, 20, 50, 100]
</script>

<template>
  <el-card shadow="never">
    <template #header>
      <div class="card-header">
        <span>审批工作台</span>
        <el-button text type="primary" @click="refresh">刷新</el-button>
      </div>
    </template>

    <el-tabs v-model="tab">
      <el-tab-pane name="todo">
        <template #label>
          <el-badge :value="todoCount.data.value ?? 0" :hidden="!todoCount.data.value" :offset="[8, 2]">我的待办</el-badge>
        </template>
        <ListFilterBar v-model="todo.filters" :flows="flows.data.value ?? []" overdue @search="todo.search" @reset="todo.reset" />
        <TodoTable :items="todo.items.value" :loading="todo.loading.value" @open="open" />
        <el-pagination v-model:current-page="todo.page.value" v-model:page-size="todo.pageSize.value" :total="todo.total.value"
          :page-sizes="pageSizes" layout="total, sizes, prev, pager, next" background size="small" class="workbench__pager" />
      </el-tab-pane>
      <el-tab-pane label="我已处理" name="done">
        <ListFilterBar v-model="done.filters" :flows="flows.data.value ?? []" status dates @search="done.search" @reset="done.reset" />
        <InstanceTable :items="done.items.value" :loading="done.loading.value" empty-text="暂无已处理的审批" @open="open" />
        <el-pagination v-model:current-page="done.page.value" v-model:page-size="done.pageSize.value" :total="done.total.value"
          :page-sizes="pageSizes" layout="total, sizes, prev, pager, next" background size="small" class="workbench__pager" />
      </el-tab-pane>
      <el-tab-pane label="我发起的" name="started">
        <ListFilterBar v-model="started.filters" :flows="flows.data.value ?? []" status dates keyword-placeholder="标题"
          @search="started.search" @reset="started.reset" />
        <InstanceTable :items="started.items.value" :loading="started.loading.value" empty-text="暂无发起的审批" @open="open" />
        <el-pagination v-model:current-page="started.page.value" v-model:page-size="started.pageSize.value" :total="started.total.value"
          :page-sizes="pageSizes" layout="total, sizes, prev, pager, next" background size="small" class="workbench__pager" />
      </el-tab-pane>
      <el-tab-pane label="抄送我的" name="cc">
        <ListFilterBar v-model="cc.filters" :flows="flows.data.value ?? []" status dates @search="cc.search" @reset="cc.reset" />
        <el-checkbox v-model="cc.filters.unreadOnly" @change="cc.search">只看未读</el-checkbox>
        <CarbonCopyTable :items="cc.items.value" :loading="cc.loading.value" @open="openCopy" />
        <el-pagination v-model:current-page="cc.page.value" v-model:page-size="cc.pageSize.value" :total="cc.total.value"
          :page-sizes="pageSizes" layout="total, sizes, prev, pager, next" background size="small" class="workbench__pager" />
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.workbench__pager {
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
