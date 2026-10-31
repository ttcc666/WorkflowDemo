<script setup lang="ts">
import { computed, reactive, shallowRef } from 'vue'
import { approvalApi } from '@/api/approval'
import type { AddSignMode, ApprovalTask, DemoUser, ReturnTarget } from '@/api/types'
import { assigneeText, taskKindLabels } from '@/utils/labels'

type ActionKind = 'approve' | 'reject' | 'transfer' | 'withdraw' | 'addSign' | 'resubmit'

const props = defineProps<{
  myTask: ApprovalTask | null | undefined
  canWithdraw: boolean
  users: DemoUser[]
  currentUserId: string
  initiatorId: string
  /** 当前任务所在节点的选项（来自流程定义） */
  allowReturn: boolean
  allowAddSign: boolean
  busy: boolean
}>()

const emit = defineEmits<{
  approve: [comment: string]
  /** targetNodeId 为空表示驳回并结束流程 */
  reject: [comment: string, targetNodeId: string | null]
  transfer: [toUserId: string, comment: string]
  withdraw: [comment: string]
  addSign: [userIds: string[], mode: AddSignMode, comment: string]
  resubmit: [comment: string]
}>()

const titles: Record<ActionKind, string> = {
  approve: '同意',
  reject: '驳回',
  transfer: '转交',
  withdraw: '撤回申请',
  addSign: '加签',
  resubmit: '重新提交',
}

const addSignModes: { value: AddSignMode; label: string; help: string }[] = [
  { value: 'Before', label: '前加签', help: '先请对方审批，对方同意后回到你继续审批' },
  { value: 'After', label: '后加签', help: '你同意，对方也同意后节点才算通过' },
  { value: 'Parallel', label: '并加签', help: '与你同时审批，节点通过前对方必须同意' },
]

const dialogVisible = shallowRef(false)
const action = shallowRef<ActionKind>('approve')
const form = reactive({
  comment: '',
  toUserId: '',
  rejectMode: 'end' as 'end' | 'return',
  targetNodeId: '',
  signers: [] as string[],
  signMode: 'Before' as AddSignMode,
})

const isResubmit = computed(() => props.myTask?.kind === 'Resubmit')
const otherUsers = computed(() => props.users.filter((u) => u.id !== props.currentUserId && u.id !== props.initiatorId))
const returnTargets = shallowRef<ReturnTarget[]>([])
const loadingTargets = shallowRef(false)

const commentRequired = computed(() => action.value === 'reject')
const canSubmit = computed(() => {
  if (commentRequired.value && !form.comment.trim()) return false
  if (action.value === 'transfer') return form.toUserId.length > 0
  if (action.value === 'addSign') return form.signers.length > 0
  if (action.value === 'reject' && form.rejectMode === 'return') return form.targetNodeId.length > 0
  return true
})

async function open(kind: ActionKind) {
  action.value = kind
  Object.assign(form, { comment: '', toUserId: '', rejectMode: 'end', targetNodeId: '', signers: [], signMode: 'Before' })
  dialogVisible.value = true

  if (kind === 'reject' && props.allowReturn && props.myTask) {
    loadingTargets.value = true
    try {
      returnTargets.value = await approvalApi.returnTargets(props.myTask.id)
    } catch {
      returnTargets.value = []
    } finally {
      loadingTargets.value = false
    }
  }
}

const targetLabel = (t: ReturnTarget) => (t.isInitiator ? '发起人（修改后重新提交）' : t.nodeName)

function submit() {
  const comment = form.comment.trim()
  switch (action.value) {
    case 'approve':
      emit('approve', comment)
      break
    case 'reject':
      emit('reject', comment, form.rejectMode === 'return' ? form.targetNodeId : null)
      break
    case 'transfer':
      emit('transfer', form.toUserId, comment)
      break
    case 'addSign':
      emit('addSign', form.signers, form.signMode, comment)
      break
    case 'resubmit':
      emit('resubmit', comment)
      break
    default:
      emit('withdraw', comment)
  }
  dialogVisible.value = false
}
</script>

<template>
  <div v-if="myTask || canWithdraw" class="approval-actions">
    <template v-if="myTask && isResubmit">
      <span class="approval-actions__hint">申请已被退回，请修改后<strong>重新提交</strong>，或撤回本次申请</span>
      <el-button type="primary" :loading="busy" @click="open('resubmit')">重新提交</el-button>
    </template>
    <template v-else-if="myTask">
      <span class="approval-actions__hint">
        待您处理：<strong>{{ myTask.nodeName }}</strong>（{{ assigneeText(myTask.assigneeKind, myTask.assigneeValue) }}）
        <el-tag v-if="taskKindLabels[myTask.kind]" size="small" type="warning" effect="plain">{{ taskKindLabels[myTask.kind] }}</el-tag>
      </span>
      <el-button type="success" :loading="busy" @click="open('approve')">同意</el-button>
      <el-button type="danger" :loading="busy" @click="open('reject')">{{ allowReturn ? '驳回 / 退回' : '驳回' }}</el-button>
      <el-button :loading="busy" @click="open('transfer')">转交</el-button>
      <el-button v-if="allowAddSign" :loading="busy" @click="open('addSign')">加签</el-button>
    </template>
    <span v-else class="approval-actions__hint" />
    <el-button v-if="canWithdraw" :loading="busy" @click="open('withdraw')">撤回申请</el-button>
  </div>

  <el-dialog v-model="dialogVisible" :title="titles[action]" width="520px" append-to-body>
    <el-form label-position="top">
      <template v-if="action === 'reject' && allowReturn">
        <el-form-item label="处理方式">
          <el-radio-group v-model="form.rejectMode">
            <el-radio value="end">驳回，结束流程</el-radio>
            <el-radio value="return">退回到指定节点，流程继续</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="form.rejectMode === 'return'" label="退回到" required>
          <el-select v-model="form.targetNodeId" v-loading="loadingTargets" placeholder="选择节点" style="width: 100%">
            <el-option v-for="t in returnTargets" :key="t.nodeId" :label="targetLabel(t)" :value="t.nodeId" />
          </el-select>
          <div class="dialog-help">只能退回到已审批通过的上游节点；退回后该节点重新审批，之后的节点按流程重新流转。</div>
        </el-form-item>
      </template>

      <el-form-item v-if="action === 'transfer'" label="转交给" required>
        <el-select v-model="form.toUserId" placeholder="选择用户" filterable style="width: 100%">
          <el-option v-for="u in otherUsers" :key="u.id" :label="u.id" :value="u.id">
            <span>{{ u.id }}</span>
            <span class="option-roles">{{ u.roles.join('、') }}</span>
          </el-option>
        </el-select>
      </el-form-item>

      <template v-if="action === 'addSign'">
        <el-form-item label="加签方式">
          <el-radio-group v-model="form.signMode">
            <el-radio-button v-for="m in addSignModes" :key="m.value" :value="m.value">{{ m.label }}</el-radio-button>
          </el-radio-group>
          <div class="dialog-help">{{ addSignModes.find((m) => m.value === form.signMode)?.help }}</div>
        </el-form-item>
        <el-form-item label="加签人" required>
          <el-select v-model="form.signers" multiple filterable placeholder="选择用户" style="width: 100%">
            <el-option v-for="u in otherUsers" :key="u.id" :label="u.id" :value="u.id">
              <span>{{ u.id }}</span>
              <span class="option-roles">{{ u.roles.join('、') }}</span>
            </el-option>
          </el-select>
        </el-form-item>
      </template>

      <el-form-item :label="commentRequired ? (form.rejectMode === 'return' ? '退回原因' : '驳回原因') : '意见'" :required="commentRequired">
        <el-input v-model="form.comment" type="textarea" :rows="4" maxlength="500" show-word-limit
          :placeholder="commentRequired ? '请填写原因' : '选填'" />
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
  display: flex;
  align-items: center;
  gap: 6px;
  margin-right: auto;
  color: #606266;
}

.option-roles {
  float: right;
  color: #909399;
  font-size: 12px;
}

.dialog-help {
  width: 100%;
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}
</style>
