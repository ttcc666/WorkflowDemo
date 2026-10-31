<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import type { ApprovalTask } from '../../api/types'
import UserPicker from '../../components/UserPicker.vue'
import { assigneeText } from '../../utils/labels'

/** 管理员改派待办：选择新处理人（不能是原处理人、发起人），填写原因（选填）；时限不重置。 */
const visible = defineModel<boolean>('visible', { required: true })
const props = defineProps<{ task: ApprovalTask | null; initiatorId: string }>()
const emit = defineEmits<{ confirm: [task: ApprovalTask, toUserId: string, reason: string] }>()

const toUserId = shallowRef<string | null>(null)
const reason = shallowRef('')

watch(visible, (value) => {
  if (!value) return
  toUserId.value = null
  reason.value = ''
})

const excluded = computed(() =>
  [props.initiatorId, props.task?.assigneeKind === 'User' ? props.task.assigneeValue : ''].filter(Boolean),
)

function submit() {
  if (!props.task || !toUserId.value) return
  emit('confirm', props.task, toUserId.value, reason.value.trim())
  visible.value = false
}
</script>

<template>
  <el-dialog v-model="visible" title="改派待办" width="480px" append-to-body>
    <el-form v-if="task" label-position="top">
      <div class="reassign__current">
        {{ task.nodeName }} · 当前处理人：{{ assigneeText(task.assigneeKind, task.assigneeValue) }}
      </div>
      <el-form-item label="改派给" required>
        <UserPicker v-model="toUserId" :exclude="excluded" placeholder="搜索用户" />
        <div class="reassign__help">改派后原处理人不能再处理该任务；处理时限不重置。</div>
      </el-form-item>
      <el-form-item label="原因">
        <el-input v-model="reason" type="textarea" :rows="3" maxlength="500" show-word-limit placeholder="选填，记录在审批轨迹中" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :disabled="!toUserId" @click="submit">确定</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.reassign__current {
  margin-bottom: 12px;
  color: #606266;
}

.reassign__help {
  width: 100%;
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
}
</style>
