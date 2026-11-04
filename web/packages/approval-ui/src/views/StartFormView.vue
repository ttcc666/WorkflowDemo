<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, useTemplateRef, watch } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useCommandIntent } from '../composables/useCommandIntent'
import { useApprovalClient } from '../client'
import { ApiError, errorMessage } from '../api/http'
import type { DraftDetail, StartForm } from '../contracts/api'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import DynamicForm from '../features/forms/DynamicForm.vue'
import { initialValues, parseFormValues, type FormValues } from '../features/forms/formModel'
import { approvalRouteNames } from '../routes'
import { formatDateTime } from '../utils/labels'

const { api: approvalApi, userKey } = useApprovalClient()
const { can } = useApprovalUser()
const intent = useCommandIntent('start-form')

/**
 * 发起页：按流程的表单结构填写并提交；服务端字段错误回显到对应表单项。
 * 草稿：可随时保存（不校验必填），有修改时每 30 秒自动保存；`?draft=<id>` 继续编辑草稿，提交成功后草稿删除。
 */
const AUTO_SAVE_MS = 30_000

const route = useRoute()
const router = useRouter()
const key = computed(() => route.params.key as string)
const draftParam = computed(() => (typeof route.query.draft === 'string' ? route.query.draft : null))

const start = useAsyncData((signal) => approvalApi.startForm(key.value, signal), { watch: [key, userKey], notifyError: false, resetOnReload: true })
// 当前编辑的草稿 ID（保存新草稿后写回地址栏）；要打开的草稿只在地址栏指向其他草稿时变化，保存后不会重新加载
const draftId = shallowRef<string | null>(draftParam.value)
const openedDraft = shallowRef<string | null>(draftParam.value)
watch(draftParam, (value) => {
  if (value === draftId.value) return
  draftId.value = value
  openedDraft.value = value
})
const draft = useAsyncData(async (signal): Promise<DraftDetail | null> => (openedDraft.value ? approvalApi.draft(openedDraft.value, signal) : null), {
  watch: [openedDraft, userKey],
  resetOnReload: true,
  notifyError: false,
})

// 深层响应：DynamicForm 就地修改字段值
const data = ref<FormValues>({})
const comment = shallowRef('')
const serverErrors = shallowRef<Record<string, string[]>>()
const submitting = shallowRef(false)
const saving = shallowRef(false)
const savedAt = shallowRef<string | null>(null)
const formRef = useTemplateRef<InstanceType<typeof DynamicForm>>('form')

// 是否有未保存的修改：与上次加载 / 保存时的快照比较
const snapshot = () => JSON.stringify([data.value, comment.value.trim()])
const savedSnapshot = shallowRef('')
watch(userKey, () => {
  draftId.value = null
  openedDraft.value = null
  data.value = {}
  comment.value = ''
  serverErrors.value = undefined
  submitting.value = false
  saving.value = false
  savedAt.value = null
  savedSnapshot.value = snapshot()
  if (route.query.draft) void router.replace({ query: { ...route.query, draft: undefined } })
}, { flush: 'sync' })
const dirty = computed(() => !!start.data.value && snapshot() !== savedSnapshot.value)

function load(form: StartForm | undefined, value: DraftDetail | null | undefined) {
  data.value = initialValues(form?.form, value ? parseFormValues(value.formDataJson) : {})
  comment.value = value?.comment ?? ''
  serverErrors.value = undefined
  savedAt.value = value?.updatedAt ?? null
  savedSnapshot.value = snapshot()
  if (value?.adjusted) ElMessage.warning('流程已更新，草稿已按最新表单调整，请检查后再提交')
}

// 表单与草稿都就绪后填充（打开草稿时不先显示一次空表单）
watch([start.data, draft.data], ([form, value]) => {
  if (form && (!openedDraft.value || value !== undefined)) load(form, value)
}, { immediate: true })

async function saveDraft(silent = false) {
  const definition = start.data.value
  if (!can('approval.draft.write') || !definition || saving.value || submitting.value) return false
  saving.value = true
  const current = snapshot()
  try {
    const result = await intent.run(`draft:${definition.key}`, { draftId: draftId.value, formData: data.value, comment: comment.value.trim() },
      (body, operationId) => approvalApi.saveDraft(definition.key, body.draftId, body.formData, body.comment, operationId))
    savedSnapshot.value = current
    savedAt.value = result.updatedAt
    if (draftId.value !== result.draftId) {
      draftId.value = result.draftId
      await router.replace({ query: { ...route.query, draft: result.draftId } })
    }
    if (!silent) ElMessage.success('草稿已保存')
    return true
  } catch (e) {
    ElMessage.error(`保存草稿失败：${errorMessage(e)}`)
    return false
  } finally {
    saving.value = false
  }
}

const timer = window.setInterval(() => {
  if (dirty.value && !intent.pending.value) void saveDraft(true)
}, AUTO_SAVE_MS)
onBeforeUnmount(() => window.clearInterval(timer))

async function submit() {
  const identity = userKey.value
  const definition = start.data.value
  if (!definition || !can('approval.instance.start')) return
  serverErrors.value = undefined
  if (!(await formRef.value?.validate())) return ElMessage.warning('请检查标红的字段')
  if (identity !== userKey.value) return

  submitting.value = true
  try {
    const { instanceId } = await intent.run(`start:${definition.key}`, { draftId: draftId.value, formData: data.value, comment: comment.value.trim() },
      (body, operationId) => approvalApi.submitForm(definition.key, body.formData, body.comment, body.draftId, operationId))
    savedSnapshot.value = snapshot()
    ElMessage.success('已提交')
    await router.replace({ name: approvalRouteNames.detail, params: { id: instanceId } })
  } catch (e) {
    if (e instanceof ApiError && e.errors) serverErrors.value = e.errors
    ElMessage.error(errorMessage(e))
  } finally {
    submitting.value = false
  }
}

async function newIntent() {
  try {
    await ElMessageBox.confirm('请先在“我发起的”中确认上次提交的结果。继续后将开始一次新的申请。', '开始新的申请', { type: 'warning' })
    intent.reset(`start:${key.value}`); intent.reset(`draft:${key.value}`)
  } catch { /* Keep the original key when cancelled. */ }
}

// ───── 离开提醒：有未保存的修改时可先保存草稿 ─────

onBeforeRouteLeave(async () => {
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm('有未保存的修改，是否保存为草稿？', '离开发起页', {
      confirmButtonText: '保存草稿',
      cancelButtonText: '不保存',
      distinguishCancelAndClose: true,
      type: 'warning',
    })
    return await saveDraft()
  } catch (action) {
    return action === 'cancel'
  }
})

const onBeforeUnload = (e: BeforeUnloadEvent) => {
  if (dirty.value) e.preventDefault()
}
window.addEventListener('beforeunload', onBeforeUnload)
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))

const backToCenter = () => router.push({ name: approvalRouteNames.start })
const loadError = computed(() => start.error.value ?? draft.error.value)
</script>

<template>
  <div v-loading="start.loading.value || draft.loading.value" class="start-form">
    <el-page-header title="返回" @back="backToCenter">
      <template #content>
        {{ start.data.value?.name ?? '发起申请' }}
        <el-tag v-if="draftId" size="small" type="info" effect="plain" class="start-form__draft-tag">草稿</el-tag>
      </template>
    </el-page-header>

    <el-result v-if="loadError" icon="warning" title="无法发起" :sub-title="loadError">
      <template #extra>
        <el-button type="primary" @click="backToCenter">返回发起中心</el-button>
      </template>
    </el-result>

    <el-card v-else-if="start.data.value" shadow="never">
      <div v-if="start.data.value.settings.description" class="start-form__desc">{{ start.data.value.settings.description }}</div>
      <DynamicForm ref="form" v-model="data" :schema="start.data.value.form" :errors="serverErrors" />
      <el-form label-position="top">
        <el-form-item label="申请说明">
          <el-input v-model="comment" type="textarea" :rows="2" maxlength="500" show-word-limit placeholder="选填，将记录在审批轨迹中" />
        </el-form-item>
      </el-form>
      <div class="start-form__actions">
        <span class="start-form__saved">
          <template v-if="saving">正在保存草稿…</template>
          <template v-else-if="savedAt">草稿已保存 {{ formatDateTime(savedAt) }}<template v-if="dirty">，有新的修改</template></template>
          <template v-else-if="dirty">有修改未保存，每 30 秒自动保存草稿</template>
        </span>
        <el-button @click="backToCenter">取消</el-button>
        <el-button v-if="intent.pending.value" @click="newIntent">确认旧结果后开始新申请</el-button>
        <el-button v-if="can('approval.draft.write')" :loading="saving" :disabled="submitting" @click="saveDraft()">保存草稿</el-button>
        <el-button v-if="can('approval.instance.start')" type="primary" :loading="submitting" @click="submit">提交申请</el-button>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.start-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
  max-width: 960px;
}

.start-form__desc {
  margin-bottom: 16px;
  color: #909399;
  font-size: 13px;
}

.start-form__draft-tag {
  margin-left: 8px;
}

.start-form__actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
}

.start-form__saved {
  margin-right: auto;
  color: #909399;
  font-size: 12px;
}
</style>
