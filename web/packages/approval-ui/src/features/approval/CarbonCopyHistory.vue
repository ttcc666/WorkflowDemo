<script setup lang="ts">
import { useApprovalClient } from '../../client'
import { useAsyncData } from '../../composables/useAsyncData'
import { formatDateTime } from '../../utils/labels'
import { shallowRef } from 'vue'
import { useApprovalUser } from '../../composables/useApprovalUser'
import CarbonCopyRecipientsDialog from './CarbonCopyRecipientsDialog.vue'

const props = defineProps<{ instanceId: string }>()
const { api, userKey } = useApprovalClient()
const { can } = useApprovalUser()
const selected = shallowRef('')
const recipientsVisible = shallowRef(false)
const { data, loading } = useAsyncData(signal => api.carbonCopyHistory(props.instanceId, signal), {
  watch: [() => props.instanceId, userKey], initial: [],
})
</script>

<template>
  <el-card shadow="never" header="抄送历史">
    <el-table v-loading="loading" :data="data" size="small" empty-text="暂无抄送记录">
      <el-table-column prop="nodeId" label="节点" /><el-table-column prop="assigneeValue" label="接收人 / 角色" />
      <el-table-column label="轮次" width="60"><template #default="{ row }">{{ row.round + 1 }}</template></el-table-column>
      <el-table-column label="抄送时间"><template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template></el-table-column>
      <el-table-column v-if="can('approval.directory.read')" label="人员" width="100"><template #default="{ row }"><el-button link @click="selected = row.id; recipientsVisible = true">查看人员</el-button></template></el-table-column>
    </el-table>
  </el-card>
  <CarbonCopyRecipientsDialog :carbon-copy-id="selected" v-model="recipientsVisible" />
</template>
