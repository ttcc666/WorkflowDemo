<script setup lang="ts">
import { shallowRef, watch } from 'vue'

const props = defineProps<{ value: unknown; label: string; kind: 'object' | 'array'; clearable?: boolean }>()
const emit = defineEmits<{ update: [value: Record<string, unknown> | unknown[]]; clear: [] }>()
const text = shallowRef('')
const error = shallowRef('')
watch(() => props.value, value => {
  text.value = value === undefined ? '' : JSON.stringify(value, null, 2)
  error.value = ''
}, { immediate: true })

function apply() {
  if (props.clearable && !text.value.trim()) {
    error.value = ''
    emit('clear')
    return
  }
  try {
    const value: unknown = JSON.parse(text.value)
    if (props.kind === 'array' ? !Array.isArray(value) : !value || typeof value !== 'object' || Array.isArray(value)) {
      throw new Error()
    }
    error.value = ''
    emit('update', value as Record<string, unknown> | unknown[])
  } catch {
    error.value = props.kind === 'object' ? '请输入合法的 JSON 对象' : '请输入合法的 JSON 数组'
  }
}
</script>

<template>
  <el-form-item :label="label" :error="error">
    <el-input v-model="text" type="textarea" :rows="4" :aria-label="label" @change="apply" />
  </el-form-item>
</template>
