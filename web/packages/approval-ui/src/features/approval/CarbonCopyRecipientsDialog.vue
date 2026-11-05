<script setup lang="ts">
import { useApprovalClient } from '@ui/client'
import { formatDateTime } from '@ui/utils/labels'
import { useCarbonCopyRecipients } from './useCarbonCopyRecipients'

const props = defineProps<{ carbonCopyId: string }>()
const visible = defineModel<boolean>({ required: true })
const { api, userKey } = useApprovalClient()
const { page, pageSize, data, loading, error } = useCarbonCopyRecipients(api, userKey, () => props.carbonCopyId, visible)
</script>

<template>
  <el-dialog v-model="visible" title="抄送接收人员" width="720px" destroy-on-close>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-alert v-if="data?.isCurrentRoleMembership" type="info" :closable="false"
      title="以下为宿主当前角色成员，不是抄送发生时的历史人员快照。" />
    <p v-if="data">{{ data.assigneeKind === 'Role' ? '角色' : '指定用户' }}：{{ data.assigneeValue }}；查询时间：{{ formatDateTime(data.queriedAt) }}</p>
    <el-table v-loading="loading" :data="data?.items ?? []" row-key="userId" empty-text="当前没有接收人员">
      <el-table-column prop="userId" label="用户 ID" min-width="160" />
      <el-table-column label="姓名" min-width="160"><template #default="{ row }">{{ row.isKnown ? row.name : '宿主目录未返回该用户' }}</template></el-table-column>
      <el-table-column label="状态" width="100"><template #default="{ row }">{{ !row.isKnown ? '未知' : row.isActive ? '有效' : '已停用' }}</template></el-table-column>
    </el-table>
    <el-pagination v-if="data" v-model:current-page="page" v-model:page-size="pageSize" :total="data.total"
      :page-sizes="[20, 50, 100]" layout="total, sizes, prev, pager, next" />
  </el-dialog>
</template>
