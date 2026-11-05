<script setup lang="ts">
import { shallowRef } from 'vue'
import { ElMessage } from 'element-plus'
import type { CommandIntent } from '@ui/composables/useCommandIntent'
import { errorMessage } from '@ui/api/http'

const props = defineProps<{ intent: CommandIntent; busy?: boolean }>()
const emit = defineEmits<{ recovered: [result: unknown, scope: string] }>()
const recovering = shallowRef(false)
async function retry() {
  const scope = props.intent.pending.value
  if (recovering.value || props.busy || scope === null) return
  recovering.value = true
  try {
    const result = await props.intent.retry(scope)
    ElMessage.success('上次操作结果已确认')
    emit('recovered', result, scope)
  } catch (cause) { ElMessage.error(errorMessage(cause)) }
  finally { recovering.value = false }
}
</script>
<template>
  <el-alert v-if="intent.pending.value && intent.canRetry()" type="warning" :closable="false" title="上次提交结果尚未确认；可按原内容重试。">
    <el-button :loading="recovering" :disabled="busy" @click="retry">按原内容重试上次操作</el-button>
  </el-alert>
</template>
