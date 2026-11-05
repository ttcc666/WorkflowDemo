<script setup lang="ts">
import type { CarbonCopyItem } from '@ui/contracts/api'
import { formatDateTime, instanceStatusLabels } from '@ui/utils/labels'
import { shallowRef } from 'vue'
import { useApprovalUser } from '@ui/composables/useApprovalUser'
import CarbonCopyRecipientsDialog from '@ui/features/approval/CarbonCopyRecipientsDialog.vue'

const { can } = useApprovalUser()
const selected = shallowRef('')
const recipientsVisible = shallowRef(false)

defineProps<{ items: CarbonCopyItem[]; loading: boolean }>()
defineEmits<{ open: [item: CarbonCopyItem] }>()
</script>

<template>
  <el-table v-loading="loading" :data="items" row-key="id" empty-text="暂无抄送" @row-click="(row: CarbonCopyItem) => $emit('open', row)">
    <el-table-column label="阅读" width="90"><template #default="{ row }"><el-tag :type="row.readAt ? 'info' : 'warning'">{{ row.readAt ? '已读' : '未读' }}</el-tag></template></el-table-column>
    <el-table-column label="标题" min-width="200"><template #default="{ row }"><el-link>{{ row.title || row.businessKey }}</el-link></template></el-table-column>
    <el-table-column prop="definitionName" label="流程" width="140" />
    <el-table-column label="版本" width="70"><template #default="{ row }">v{{ row.definitionVersion }}</template></el-table-column>
    <el-table-column label="状态" width="130"><template #default="{ row }">{{ instanceStatusLabels[row.status as CarbonCopyItem['status']].text }}</template></el-table-column>
    <el-table-column prop="assigneeValue" label="接收用户 / 角色" width="140" />
    <el-table-column v-if="can('approval.directory.read')" label="人员" width="100"><template #default="{ row }"><el-button link @click.stop="selected = row.id; recipientsVisible = true">查看人员</el-button></template></el-table-column>
    <el-table-column prop="nodeId" label="抄送节点" width="110" />
    <el-table-column label="抄送时间" width="170"><template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template></el-table-column>
    <el-table-column label="阅读时间" width="170"><template #default="{ row }">{{ formatDateTime(row.readAt) }}</template></el-table-column>
  </el-table>
  <CarbonCopyRecipientsDialog :carbon-copy-id="selected" v-model="recipientsVisible" />
</template>
