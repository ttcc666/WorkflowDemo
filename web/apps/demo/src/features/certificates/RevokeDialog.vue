<script setup lang="ts">
import { shallowRef, watch } from 'vue'
import type { Certificate } from '@/api/types'

const props = defineProps<{ certificate: Certificate | null; submitting: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ submit: [reason: string] }>()

const reason = shallowRef('')
watch(visible, (open) => {
  if (open) reason.value = ''
})
</script>

<template>
  <el-dialog v-model="visible" :title="`申请注销证书 ${props.certificate?.certNo ?? ''}`" width="480px">
    <el-alert type="warning" :closable="false" show-icon class="revoke-tip" title="注销需经质量审核与负责人会签，通过后证书不可恢复" />
    <el-input v-model="reason" type="textarea" :rows="4" maxlength="300" show-word-limit placeholder="请填写注销原因" />
    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="danger" :loading="submitting" :disabled="!reason.trim()" @click="emit('submit', reason.trim())">提交审核</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.revoke-tip {
  margin-bottom: 12px;
}
</style>
