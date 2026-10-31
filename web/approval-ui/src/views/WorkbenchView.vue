<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { approvalApi } from '@/api/approval'
import { useAsyncData } from '@/composables/useAsyncData'
import { useCurrentUser } from '@/composables/useCurrentUser'
import TodoTable from '@/features/workbench/TodoTable.vue'
import InstanceTable from '@/features/workbench/InstanceTable.vue'

type Tab = 'todo' | 'done' | 'started'

const route = useRoute()
const router = useRouter()
const { userId } = useCurrentUser()

const tab = computed<Tab>({
  get: () => (['todo', 'done', 'started'].includes(route.query.tab as string) ? (route.query.tab as Tab) : 'todo'),
  set: (value) => router.replace({ query: { ...route.query, tab: value } }),
})

// 切换用户后各列表自动刷新
const todo = useAsyncData((s) => approvalApi.todo(s), { watch: [userId], initial: [] })
const done = useAsyncData((s) => approvalApi.done(s), { watch: [userId], initial: [] })
const started = useAsyncData((s) => approvalApi.started(s), { watch: [userId], initial: [] })

function refresh() {
  void todo.reload()
  void done.reload()
  void started.reload()
}

function open(instanceId: string) {
  void router.push({ name: 'approval-detail', params: { id: instanceId } })
}
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
          <el-badge :value="todo.data.value?.length ?? 0" :hidden="!todo.data.value?.length" :offset="[8, 2]">我的待办</el-badge>
        </template>
        <TodoTable :items="todo.data.value ?? []" :loading="todo.loading.value" @open="open" />
      </el-tab-pane>
      <el-tab-pane label="我已处理" name="done">
        <InstanceTable :items="done.data.value ?? []" :loading="done.loading.value" empty-text="暂无已处理的审批" @open="open" />
      </el-tab-pane>
      <el-tab-pane label="我发起的" name="started">
        <InstanceTable :items="started.data.value ?? []" :loading="started.loading.value" empty-text="暂无发起的审批" @open="open" />
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
</style>
