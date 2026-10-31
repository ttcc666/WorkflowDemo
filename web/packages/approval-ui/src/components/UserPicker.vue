<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import type { DirectoryUser } from '../api/types'
import { useDirectory } from '../composables/useDirectory'

/**
 * 用户选择器：远程搜索用户目录，回显已选用户名称。
 * 单选时 v-model 为用户 ID（string），多选时为 ID 数组。宿主未注册用户目录时可直接输入用户 ID。
 */
const props = withDefaults(
  defineProps<{
    multiple?: boolean
    placeholder?: string
    /** 不可选的用户（如当前用户、发起人） */
    exclude?: string[]
    disabled?: boolean
    size?: 'large' | 'default' | 'small'
  }>(),
  { multiple: false, placeholder: '搜索用户', exclude: () => [], disabled: false, size: 'default' },
)

const model = defineModel<string | string[] | null>({ default: null })

const directory = useDirectory()
const options = shallowRef<DirectoryUser[]>([])
const loading = shallowRef(false)

const selectedIds = computed(() => (Array.isArray(model.value) ? model.value : model.value ? [model.value] : []))
const visibleOptions = computed(() => options.value.filter((u) => !props.exclude.includes(u.id)))

async function search(keyword: string) {
  loading.value = true
  try {
    const found = await directory.search(keyword.trim())
    // 保留已选项，避免搜索后已选标签显示为 ID
    const selected = await directory.resolve(selectedIds.value)
    options.value = [...selected.filter((s) => !found.some((f) => f.id === s.id)), ...found]
  } finally {
    loading.value = false
  }
}

watch(selectedIds, async (ids) => {
  if (ids.some((id) => !options.value.some((o) => o.id === id))) {
    const selected = await directory.resolve(ids)
    options.value = [...selected, ...options.value.filter((o) => !ids.includes(o.id))]
  }
}, { immediate: true })

function onVisible(visible: boolean) {
  if (visible && options.value.length <= selectedIds.value.length) void search('')
}
</script>

<template>
  <el-select
    v-model="model"
    :multiple="multiple"
    filterable
    remote
    :remote-method="search"
    :loading="loading"
    :placeholder="placeholder"
    :disabled="disabled"
    :size="size"
    :allow-create="!directory.available.value"
    :default-first-option="!directory.available.value"
    clearable
    class="user-picker"
    @visible-change="onVisible"
  >
    <el-option v-for="u in visibleOptions" :key="u.id" :label="u.name" :value="u.id">
      <span>{{ u.name }}</span>
      <span class="user-picker__meta">{{ u.name !== u.id ? u.id : '' }} {{ u.roles.join('、') }}</span>
    </el-option>
  </el-select>
</template>

<style scoped>
.user-picker {
  width: 100%;
}

.user-picker__meta {
  float: right;
  margin-left: 16px;
  color: #909399;
  font-size: 12px;
}
</style>
