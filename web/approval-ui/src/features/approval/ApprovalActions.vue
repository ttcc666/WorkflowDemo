<script setup lang="ts">
import { computed, reactive, shallowRef } from 'vue'
import type { ApprovalTask, DemoUser } from '@/api/types'
import { assigneeText } from '@/utils/labels'

type ActionKind = 'approve' | 'reject' | 'transfer' | 'withdraw'

const props = defineProps<{
  myTask: ApprovalTask | null | undefined
  canWithdraw: boolean
  users: DemoUser[]
  currentUserId: string
  busy: boolean
}>()

const emit = defineEmits<{
  approve: [comment: string]
  reject: [comment: string]
  transfer: [toUserId: string, comment: string]
  withdraw: [comment: string]
}>()

const titles: Record<ActionKind, string> = {
  approve: '同意',
  reject: '驳回',
  transfer: '转交',
  withdraw: '撤回申请',
}

const dialogVisible = shallowRef(false)
const action = shallowRef<ActionKind>('approve')
const form = reactive({ comment: '', toUserId: '' })

const transferCandidates = computed(() => props.users.filter((u) => u.id !== props.currentUserId))
const commentRequired = computed(() => action.value === 'reject')
const canSubmit = computed(
  () =>
    (!commentRequired.value || form.comment.trim().length > 0) &&
    (action.value !== 'transfer' || form.toUserId.length > 0),
)

function open(kind: ActionKind) {
  action.value = kind
  form.comment = ''
  form.toUserId = ''
  dialogVisible.value = true
}

function submit() {
  const comment = form.comment.trim()
  if (action.value === 'approve') emit('approve', comment)
  else if (action.value === 'reject') emit('reject', comment)
  else if (action.value === 'transfer') emit('transfer', form.toUserId, comment)
  else emit('withdraw', comment)
  dialogVisible.value = false
}
</script>

<template>
  <div v-if="myTask || canWithdraw" class="approval-actions">
    <template v-if="myTask">
      <span class="approval-actions__hint">
        待您处理：<strong>{{ myTask.nodeName }}</strong>（{{ assigneeText(myTask.assigneeKind, myTask.assigneeValue) }}）
      </span>
      <el-button type="success" :loading="busy" @click="open('approve')">同意</el-button>
      <el-button type="danger" :loading="busy" @click="open('reject')">驳回</el-button>
      <el-button :loading="busy" @click="open('transfer')">转交</el-button>
    </template>
    <el-button v-if="canWithdraw" :loading="busy" @click="open('withdraw')">撤回申请</el-button>
  </div>

  <el-dialog v-model="dialogVisible" :title="titles[action]" width="480px" append-to-body>
    <el-form label-position="top">
      <el-form-item v-if="action === 'transfer'" label="转交给" required>
        <el-select v-model="form.toUserId" placeholder="选择用户" filterable style="width: 100%">
          <el-option v-for="u in transferCandidates" :key="u.id" :label="u.id" :value="u.id">
            <span>{{ u.id }}</span>
            <span class="option-roles">{{ u.roles.join('、') }}</span>
          </el-option>
        </el-select>
      </el-form-item>
      <el-form-item :label="commentRequired ? '驳回原因' : '审批意见'" :required="commentRequired">
        <el-input v-model="form.comment" type="textarea" :rows="4" maxlength="500" show-word-limit
          :placeholder="commentRequired ? '请填写驳回原因' : '选填'" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="dialogVisible = false">取消</el-button>
      <el-button :type="action === 'reject' ? 'danger' : 'primary'" :disabled="!canSubmit" @click="submit">确定</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.approval-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  border: 1px solid #faecd8;
  border-radius: 6px;
  background: #fdf6ec;
}

.approval-actions__hint {
  margin-right: auto;
  color: #606266;
}

.option-roles {
  float: right;
  color: #909399;
  font-size: 12px;
}
</style>
