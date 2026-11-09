<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { CommandIntent } from '@ui/composables/useCommandIntent'
import { errorMessage } from '@ui/api/http'

const props = defineProps<{ intent: CommandIntent; busy?: boolean; scope?: string | null; description?: string }>()
const emit = defineEmits<{ recovered: [result: unknown, scope: string]; reset: [] }>()
const recovering = shallowRef(false)
const scope = computed(() => props.scope === undefined ? props.intent.pending.value : props.scope)
async function retry() {
  const current = scope.value
  if (recovering.value || props.busy || current === null || !props.intent.canRetry(current)) return
  recovering.value = true
  try {
    const result = await props.intent.retry(current)
    ElMessage.success('上次操作结果已确认')
    emit('recovered', result, current)
  } catch (cause) { ElMessage.error(errorMessage(cause)) }
  finally { recovering.value = false }
}
async function reset() {
  const current = scope.value
  if (current === null || recovering.value || props.busy) return
  recovering.value = true
  try {
    await ElMessageBox.confirm('确认已核对原操作结果，且当前操作是新的业务意图。新键可能产生另一条业务记录。', '开始新操作', { type: 'warning' })
    if (scope.value !== current || props.busy) return
    props.intent.reset(current)
    emit('reset')
  } catch { /* Cancel leaves the original operation intact. */ }
  finally { recovering.value = false }
}
</script>
<template>
  <el-alert v-if="scope !== null" type="warning" :closable="false" title="上次提交结果尚未确认">
    <p v-if="description">{{ description }}</p>
    <p role="status">恢复状态：{{ recovering ? '正在确认原操作结果' : intent.hasConflictingPending(scope) ? '检测到多笔未确认操作，请人工核对全部结果，禁止自动重试' : intent.canRetry(scope) ? '原请求仍在本页，可恢复' : '当前页面已无原请求正文，请人工核对，禁止自动重试' }}</p>
    <el-button v-if="intent.canRetry(scope)" :loading="recovering" :disabled="busy" @click="retry">按原内容重试上次操作</el-button>
    <el-button :disabled="busy || recovering" @click="reset">核对结果后开始新操作</el-button>
  </el-alert>
</template>
