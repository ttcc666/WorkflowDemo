<script setup lang="ts">
import { onScopeDispose, shallowRef } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getApprovalClientCore, useApprovalClient } from '@ui/client'
import type { CommandIntent } from '@ui/composables/useCommandIntent'
import { errorMessage } from '@ui/api/http'

const props = defineProps<{ intent: CommandIntent; busy?: boolean; scope: string; description?: string;
  recovered: (value: unknown, scope: string) => void; reset: (scope: string) => void }>()
const client = getApprovalClientCore(useApprovalClient())
const recovering = shallowRef(false)
const error = shallowRef('')
let active = true
onScopeDispose(() => { active = false })
async function retry() {
  const current = props.scope, manager = props.intent, owner = client.generation
  if (recovering.value || props.busy || !manager.canRetry(current)) return
  recovering.value = true; error.value = ''
  try {
    const result = await manager.retry(current)
    if (owner !== client.generation || manager !== props.intent) return
    props.recovered(result, current)
    if (active) ElMessage.success('上次操作结果已确认')
  } catch (cause) { if (active && owner === client.generation) error.value = errorMessage(cause) }
  finally { recovering.value = false }
}
async function reset() {
  const current = props.scope, manager = props.intent, owner = client.generation
  if (recovering.value || props.busy) return
  recovering.value = true; error.value = ''
  try {
    await ElMessageBox.confirm('确认已核对原操作结果，且当前操作是新的业务意图。新键可能产生另一条业务记录。', '开始新操作', { type: 'warning' })
    if (!active || owner !== client.generation || manager !== props.intent || props.busy) return
    manager.reset(current)
    props.reset(current)
  } catch { /* Cancel leaves the original operation intact. */ }
  finally { recovering.value = false }
}
</script>
<template>
  <el-alert type="warning" :closable="false" title="上次提交结果尚未确认">
    <p>操作：{{ scope }}</p>
    <p v-if="description">{{ description }}</p>
    <p role="status">恢复状态：{{ recovering ? '正在确认原操作结果' : intent.hasConflictingPending(scope) ? '检测到多笔未确认操作，请人工核对全部结果，禁止自动重试' : intent.canRetry(scope) ? '原请求仍在本页，可恢复' : '当前页面已无原请求正文，请人工核对，禁止自动重试' }}</p>
    <el-button v-if="intent.canRetry(scope)" :loading="recovering" :disabled="busy" @click="retry">按原内容重试上次操作</el-button>
    <el-button :disabled="busy || recovering" @click="reset">核对结果后开始新操作</el-button>
    <el-alert v-if="error" type="error" :closable="false" :title="error" />
  </el-alert>
</template>
