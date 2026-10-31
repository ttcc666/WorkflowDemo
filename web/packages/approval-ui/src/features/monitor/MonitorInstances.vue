<script setup lang="ts">
import { shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { approvalApi } from '../../api/approval'
import { errorMessage } from '../../api/http'
import type { FlowSummary, InstanceListItem } from '../../api/types'
import UserPicker from '../../components/UserPicker.vue'
import { usePagedList } from '../../composables/usePagedList'
import { approvalRouteNames } from '../../routes'
import InstanceTable from '../workbench/InstanceTable.vue'
import ListFilterBar from '../workbench/ListFilterBar.vue'

/** 流程监控：全部流程的分页搜索（状态、流程、发起人、关键字、发起日期），进行中的可终止。 */
defineProps<{ flows: FlowSummary[] }>()

const router = useRouter()
const initiator = shallowRef<string | null>(null)
const list = usePagedList((query, signal) => approvalApi.adminInstances({ ...query, initiatorId: initiator.value ?? undefined }, signal), {
  routePrefix: 'm_',
})

// 发起人不在通用筛选栏中：变化时回到第一页重新查询（search 在条件未变时也会刷新）
function onInitiatorChange() {
  list.search()
}

function reset() {
  initiator.value = null
  list.reset()
}

const open = (id: string) => router.push({ name: approvalRouteNames.detail, params: { id } })

async function terminate(row: InstanceListItem) {
  let reason: string
  try {
    const result = await ElMessageBox.prompt(`终止「${row.title || row.businessKey}」？所有待办将取消，业务单据按驳回处理。`, '终止流程', {
      inputPlaceholder: '终止原因（必填）',
      inputValidator: (value) => (value?.trim() ? true : '请填写终止原因'),
      confirmButtonText: '终止',
      confirmButtonClass: 'el-button--danger',
      type: 'warning',
    })
    reason = result.value.trim()
  } catch {
    return
  }
  try {
    await approvalApi.terminate(row.id, reason)
    ElMessage.success('已终止流程')
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
  await list.reload()
}
</script>

<template>
  <div class="monitor-instances">
    <div class="monitor-instances__filters">
      <ListFilterBar v-model="list.filters" :flows="flows" status dates @search="list.search" @reset="reset" />
      <UserPicker v-model="initiator" placeholder="发起人" class="monitor-instances__initiator" @update:model-value="onInitiatorChange" />
    </div>
    <InstanceTable :items="list.items.value" :loading="list.loading.value" empty-text="没有符合条件的流程" @open="open">
      <template #actions="{ row }">
        <el-button v-if="row.status === 'Running'" link type="danger" size="small" @click.stop="terminate(row)">终止</el-button>
      </template>
    </InstanceTable>
    <el-pagination v-model:current-page="list.page.value" v-model:page-size="list.pageSize.value" :total="list.total.value"
      :page-sizes="[10, 20, 50, 100]" layout="total, sizes, prev, pager, next" background size="small" class="monitor-instances__pager" />
  </div>
</template>

<style scoped>
.monitor-instances__filters {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 8px;
}

.monitor-instances__initiator {
  width: 180px;
}

.monitor-instances__pager {
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
