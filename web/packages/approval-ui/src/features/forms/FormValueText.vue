<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { useDirectory } from '../../composables/useDirectory'
import FileList from './FileList.vue'
import type { FileValue, FormField } from './formModel'

/** 字段值的只读展示：选项显示名称、人员显示姓名、金额保留两位小数。 */
const props = defineProps<{ field: FormField; value: unknown }>()

const directory = useDirectory()
const userNames = shallowRef<string[]>([])

const userIds = computed(() => {
  if (props.field.type !== 'User' && props.field.type !== 'Users') return []
  return (Array.isArray(props.value) ? props.value : props.value ? [props.value] : []).map(String)
})

watch(userIds, async (ids) => {
  userNames.value = ids.length ? (await directory.resolve(ids)).map((u) => u.name) : []
}, { immediate: true })

const optionLabel = (v: unknown) => props.field.options?.find((o) => o.value === v)?.label ?? String(v)

const text = computed(() => {
  const v = props.value
  if (props.field.type === 'Switch') return v === true ? '是' : '否'
  if (v === null || v === undefined || v === '' || (Array.isArray(v) && v.length === 0)) return '-'
  switch (props.field.type) {
    case 'Select':
    case 'Radio':
      return optionLabel(v)
    case 'MultiSelect':
      return (v as unknown[]).map(optionLabel).join('、')
    case 'DateRange':
      return (v as string[]).join(' 至 ')
    case 'Money':
      return Number(v).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    case 'Formula': {
      const digits = props.field.precision ?? 2
      return Number(v).toLocaleString('zh-CN', { minimumFractionDigits: digits, maximumFractionDigits: digits })
    }
    case 'User':
    case 'Users':
      return userNames.value.join('、') || userIds.value.join('、')
    // 明细表只显示行数（详情页用表格展示，这里用于修改记录等摘要）
    case 'Table':
      return Array.isArray(v) ? `共 ${v.length} 行` : String(v)
    default:
      return String(v)
  }
})
</script>

<template>
  <FileList v-if="field.type === 'File'" :files="Array.isArray(value) ? (value as FileValue[]) : []" />
  <span v-else class="field-value" :class="{ 'field-value--multiline': field.type === 'TextArea' }">{{ text }}</span>
</template>

<style scoped>
.field-value {
  color: #303133;
  line-height: 1.6;
}

.field-value--multiline {
  white-space: pre-wrap;
}
</style>
