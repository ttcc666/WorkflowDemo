<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { approvalApi } from '../../api/approval'
import { errorMessage } from '../../api/http'
import type { ReassignResult, TodoItem } from '../../api/types'
import UserPicker from '../../components/UserPicker.vue'
import { useAsyncData } from '../../composables/useAsyncData'
import { formatDateTime, taskStatusLabels } from '../../utils/labels'

/** 离职交接：选择原处理人 → 预览其未完成的待办 → 选择新处理人 → 确认批量改派（不含角色任务）。 */
const fromUser = shallowRef<string | null>(null)
const toUser = shallowRef<string | null>(null)
const reason = shallowRef('离职交接')
const submitting = shallowRef(false)
const result = shallowRef<ReassignResult | null>(null)

const preview = useAsyncData(
  async (signal) => (fromUser.value ? approvalApi.userTasks(fromUser.value, { pageSize: 100 }, signal) : null),
  { watch: [fromUser] },
)
watch(fromUser, () => (result.value = null))

const tasks = computed<TodoItem[]>(() => preview.data.value?.items ?? [])
const total = computed(() => preview.data.value?.total ?? 0)
const canSubmit = computed(() => !!fromUser.value && !!toUser.value && fromUser.value !== toUser.value && total.value > 0)

async function submit() {
  if (!canSubmit.value) return
  try {
    await ElMessageBox.confirm(`把 ${fromUser.value} 的 ${total.value} 条未完成待办改派给 ${toUser.value}？`, '离职交接', { type: 'warning' })
  } catch {
    return
  }
  submitting.value = true
  try {
    result.value = await approvalApi.reassignUser(fromUser.value!, toUser.value!, reason.value.trim())
    ElMessage.success(`已改派 ${result.value.count} 条待办`)
    await preview.reload()
  } catch (e) {
    ElMessage.error(errorMessage(e))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="handover">
    <el-form label-position="top" class="handover__form" @submit.prevent>
      <el-form-item label="原处理人" required>
        <UserPicker v-model="fromUser" placeholder="选择离职或长期不在的人员" />
      </el-form-item>
      <el-form-item label="新处理人" required>
        <UserPicker v-model="toUser" :exclude="fromUser ? [fromUser] : []" placeholder="选择接手人员" />
      </el-form-item>
      <el-form-item label="原因">
        <el-input v-model="reason" maxlength="200" />
      </el-form-item>
      <el-form-item label=" ">
        <el-button type="primary" :disabled="!canSubmit" :loading="submitting" @click="submit">确认改派</el-button>
      </el-form-item>
    </el-form>

    <div class="handover__help">
      只改派指派给本人的待办（含依次审批中尚未轮到、等待加签的任务）；角色任务由角色内其他成员处理，不在此列。
      新处理人是申请的发起人（不允许审批自己的申请）或已是该节点审批人时会跳过；处理时限不重置。
    </div>

    <el-alert v-if="result" :type="result.skipped.length ? 'warning' : 'success'" :closable="false" show-icon class="handover__result"
      :title="`已改派 ${result.count} 条${result.skipped.length ? `，跳过 ${result.skipped.length} 条` : ''}`">
      <div v-for="s in result.skipped" :key="s.taskId">{{ s.title || s.instanceId }} · {{ s.nodeName }}：{{ s.reason }}</div>
    </el-alert>

    <template v-if="fromUser">
      <div class="handover__title">{{ fromUser }} 的未完成待办（{{ total }}）</div>
      <el-table v-loading="preview.loading.value" :data="tasks" size="small" empty-text="没有未完成的待办">
        <el-table-column prop="title" label="标题" min-width="220" show-overflow-tooltip />
        <el-table-column prop="definitionName" label="流程" width="140" show-overflow-tooltip />
        <el-table-column prop="nodeName" label="节点" width="130" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="taskStatusLabels[(row as TodoItem).status].type">{{ taskStatusLabels[(row as TodoItem).status].text }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="initiatorId" label="申请人" width="100" />
        <el-table-column label="到达时间" width="160">
          <template #default="{ row }">{{ formatDateTime((row as TodoItem).createdAt) }}</template>
        </el-table-column>
      </el-table>
      <div v-if="total > tasks.length" class="handover__help">仅显示前 {{ tasks.length }} 条，确认后全部改派。</div>
    </template>
  </div>
</template>

<style scoped>
.handover__form {
  display: grid;
  grid-template-columns: 220px 220px 1fr auto;
  gap: 12px;
  align-items: end;
}

.handover__help {
  margin: 4px 0 12px;
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}

.handover__result {
  margin-bottom: 12px;
}

.handover__title {
  margin-bottom: 8px;
  font-weight: 600;
}
</style>
