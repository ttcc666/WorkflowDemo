<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { useApprovalClient } from '@ui/client'
import { errorMessage } from '@ui/api/http'
import { useCommandIntent } from '@ui/composables/useCommandIntent'
import CommandRecovery from '@ui/components/CommandRecovery.vue'
import type { DefinitionImportReceipt } from '@ui/contracts/api'
import { readDefinitionFile } from './definitionTransfer'

const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ imported: [key: string] }>()
const { api, userKey } = useApprovalClient()
const intent = useCommandIntent('import-definition')
const file = shallowRef<Awaited<ReturnType<typeof readDefinitionFile>>>()
const targetKey = shallowRef('')
const busy = shallowRef(false)
const valid = shallowRef(false)
const error = shallowRef('')
const canValidate = computed(() => file.value && /^[A-Za-z0-9._-]{1,100}$/.test(targetKey.value))
let generation = 0

watch([visible, userKey], () => {
  generation++
  file.value = undefined
  targetKey.value = ''
  busy.value = false
  error.value = ''
  valid.value = false
}, { flush: 'sync' })
watch(targetKey, () => { valid.value = false; error.value = '' })

async function choose(event: Event) {
  const input = event.target as HTMLInputElement
  const selected = input.files?.[0]
  input.value = ''
  file.value = undefined
  valid.value = false
  error.value = ''
  if (!selected) return
  const current = ++generation
  busy.value = true
  try {
    const parsed = await readDefinitionFile(selected)
    if (current === generation) file.value = parsed
  } catch (cause) {
    if (current === generation) error.value = errorMessage(cause)
  } finally { if (current === generation) busy.value = false }
}

async function validate() {
  if (!file.value || !canValidate.value || busy.value) return
  const current = generation
  busy.value = true
  error.value = ''
  valid.value = false
  try {
    await api.validateDefinitionImport(targetKey.value, file.value.packageBase64)
    if (current === generation) valid.value = true
  } catch (cause) {
    if (current === generation) error.value = errorMessage(cause)
  } finally { if (current === generation) busy.value = false }
}

function finish(result: unknown) {
  const { key } = result as DefinitionImportReceipt
  ElMessage.success(`已导入 ${key}，当前为未发布草稿`)
  visible.value = false
  emit('imported', key)
}

async function submit() {
  if (!file.value || !canValidate.value || busy.value) return
  const current = generation
  const key = targetKey.value
  const packageBase64 = file.value.packageBase64
  // An uncertain prior submission may already have occupied this key. Recover it directly.
  if (!valid.value && !intent.hasPending(key)) return
  busy.value = true
  error.value = ''
  try {
    const result = await intent.run(key, { key, packageBase64 },
      (payload, operationId) => api.importDefinitionDraft(payload.key, payload.packageBase64, operationId))
    if (current === generation) finish(result)
  } catch (cause) {
    if (current === generation) error.value = errorMessage(cause)
  } finally { if (current === generation) busy.value = false }
}
</script>

<template>
  <el-dialog v-model="visible" title="导入流程定义" width="min(560px, 95vw)" :close-on-click-modal="false">
    <CommandRecovery :intent="intent" :busy="busy" @recovered="finish" />
    <el-form label-position="top">
      <el-form-item label="流程定义文件" for="approval-definition-file">
        <input id="approval-definition-file" type="file" accept=".json,application/json" :disabled="busy" @change="choose" />
      </el-form-item>
      <p v-if="file">来源 {{ file.source.name }}（{{ file.source.key }} · v{{ file.source.version }}）</p>
      <el-form-item label="目标流程标识">
        <el-input v-model.trim="targetKey" :disabled="busy" maxlength="100" placeholder="输入尚未使用的流程 Key" />
      </el-form-item>
    </el-form>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-alert v-else-if="valid" title="预检通过，可以创建未发布草稿" type="success" :closable="false" show-icon />
    <template #footer>
      <el-button :disabled="!canValidate || busy" @click="validate">检查文件和宿主依赖</el-button>
      <el-button type="primary" :loading="busy" :disabled="!canValidate || (!valid && !intent.hasPending(targetKey))" @click="submit">导入为草稿</el-button>
    </template>
  </el-dialog>
</template>
