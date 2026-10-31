<script setup lang="ts">
import { computed, reactive, ref, shallowRef, useTemplateRef } from 'vue'
import { approvalApi } from '../../api/approval'
import type { AddSignMode, ApprovalTask, ReturnTarget } from '../../api/types'
import UserPicker from '../../components/UserPicker.vue'
import DynamicForm from '../forms/DynamicForm.vue'
import { initialValues, type FormSchema, type FormValues } from '../forms/formModel'
import { assigneeText, taskKindLabels } from '../../utils/labels'

type ActionKind = 'approve' | 'reject' | 'transfer' | 'withdraw' | 'addSign' | 'resubmit' | 'terminate'

const props = defineProps<{
  myTask: ApprovalTask | null | undefined
  canWithdraw: boolean
  /** 管理员：流程进行中时可终止 */
  canTerminate?: boolean
  currentUserId: string
  initiatorId: string
  /** 当前任务所在节点的选项（来自流程定义） */
  allowReturn: boolean
  allowAddSign: boolean
  busy: boolean
  /** 表单审批：重新提交时可修改的表单结构与当前数据 */
  resubmitForm?: FormSchema
  formData?: FormValues
  /** 审批人在申请内容中修改了的字段名称（同意时一并提交） */
  pendingChanges?: string[]
}>()

const emit = defineEmits<{
  approve: [comment: string]
  /** targetNodeId 为空表示驳回并结束流程 */
  reject: [comment: string, targetNodeId: string | null]
  transfer: [toUserId: string, comment: string]
  withdraw: [comment: string]
  addSign: [userIds: string[], mode: AddSignMode, comment: string]
  /** formData 仅在可修改表单时传出 */
  resubmit: [comment: string, formData?: FormValues]
  terminate: [reason: string]
}>()

const titles: Record<ActionKind, string> = {
  approve: '同意',
  reject: '驳回',
  transfer: '转交',
  withdraw: '撤回申请',
  addSign: '加签',
  resubmit: '重新提交',
  terminate: '终止流程',
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
  toUserId: null as string | null,
  rejectMode: 'end' as 'end' | 'return',
  targetNodeId: '',
  signers: [] as string[] | null,
  signMode: 'Before' as AddSignMode,
})

const isResubmit = computed(() => props.myTask?.kind === 'Resubmit')
// 重新提交时编辑的表单数据（深层响应，DynamicForm 就地修改）
const resubmitData = ref<FormValues>({})
const resubmitFormRef = useTemplateRef<InstanceType<typeof DynamicForm>>('resubmitFormRef')
const editingForm = computed(() => action.value === 'resubmit' && !!props.resubmitForm)
// 转交 / 加签对象不能是自己和发起人
const excludedUsers = computed(() => [props.currentUserId, props.initiatorId])
const returnTargets = shallowRef<ReturnTarget[]>([])
const loadingTargets = shallowRef(false)

const commentRequired = computed(() => action.value === 'reject' || action.value === 'terminate')
const commentLabel = computed(() => {
  if (action.value === 'terminate') return '终止原因'
  if (action.value === 'reject') return form.rejectMode === 'return' ? '退回原因' : '驳回原因'
  return '意见'
})
const canSubmit = computed(() => {
  if (commentRequired.value && !form.comment.trim()) return false
  if (action.value === 'transfer') return !!form.toUserId
  if (action.value === 'addSign') return (form.signers ?? []).length > 0
  if (action.value === 'reject' && form.rejectMode === 'return') return form.targetNodeId.length > 0
  return true
})

async function open(kind: ActionKind) {
  action.value = kind
  Object.assign(form, { comment: '', toUserId: null, rejectMode: 'end', targetNodeId: '', signers: [], signMode: 'Before' })
  if (kind === 'resubmit' && props.resubmitForm) resubmitData.value = initialValues(props.resubmitForm, props.formData)
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

async function submit() {
  const comment = form.comment.trim()
  if (editingForm.value && !(await resubmitFormRef.value?.validate())) return
  switch (action.value) {
    case 'approve':
      emit('approve', comment)
      break
    case 'reject':
      emit('reject', comment, form.rejectMode === 'return' ? form.targetNodeId : null)
      break
    case 'transfer':
      emit('transfer', form.toUserId!, comment)
      break
    case 'addSign':
      emit('addSign', form.signers ?? [], form.signMode, comment)
      break
    case 'resubmit':
      emit('resubmit', comment, editingForm.value ? resubmitData.value : undefined)
      break
    case 'terminate':
      emit('terminate', comment)
      break
    default:
      emit('withdraw', comment)
  }
  dialogVisible.value = false
}
</script>

<template>
  <div v-if="myTask || canWithdraw || canTerminate" class="approval-actions">
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
    <el-button v-if="canTerminate" type="danger" plain :loading="busy" @click="open('terminate')">终止流程</el-button>
  </div>

  <el-dialog v-model="dialogVisible" :title="titles[action]" :width="editingForm ? '820px' : '520px'" append-to-body>
    <template v-if="editingForm && resubmitForm">
      <div class="dialog-help dialog-help--block">修改申请内容后重新提交，流程将从第一个审批节点重新开始。</div>
      <DynamicForm ref="resubmitFormRef" v-model="resubmitData" :schema="resubmitForm" />
    </template>
    <el-alert v-if="action === 'terminate'" type="warning" :closable="false" show-icon class="dialog-help--block"
      title="终止后流程结束、所有待办取消，业务单据按驳回处理，并通知申请人" />
    <el-alert v-if="action === 'approve' && pendingChanges?.length" type="warning" :closable="false" show-icon class="dialog-help--block"
      :title="`同意时将一并提交对申请内容的修改：${pendingChanges.join('、')}`" />
    <el-alert v-else-if="pendingChanges?.length && action !== 'withdraw' && action !== 'terminate'" type="info" :closable="false" show-icon
      class="dialog-help--block" :title="`对申请内容的修改（${pendingChanges.join('、')}）只在同意时提交，本操作不会保存`" />
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
        <UserPicker v-model="form.toUserId" :exclude="excludedUsers" placeholder="搜索用户" />
      </el-form-item>

      <template v-if="action === 'addSign'">
        <el-form-item label="加签方式">
          <el-radio-group v-model="form.signMode">
            <el-radio-button v-for="m in addSignModes" :key="m.value" :value="m.value">{{ m.label }}</el-radio-button>
          </el-radio-group>
          <div class="dialog-help">{{ addSignModes.find((m) => m.value === form.signMode)?.help }}</div>
        </el-form-item>
        <el-form-item label="加签人" required>
          <UserPicker v-model="form.signers" multiple :exclude="excludedUsers" placeholder="搜索用户" />
        </el-form-item>
      </template>

      <el-form-item :label="commentLabel" :required="commentRequired">
        <el-input v-model="form.comment" type="textarea" :rows="4" maxlength="500" show-word-limit
          :placeholder="commentRequired ? '请填写原因' : '选填'" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="dialogVisible = false">取消</el-button>
      <el-button :type="action === 'reject' || action === 'terminate' ? 'danger' : 'primary'" :disabled="!canSubmit" @click="submit">确定</el-button>
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

.dialog-help--block {
  margin: 0 0 12px;
}

.dialog-help {
  width: 100%;
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}
</style>
