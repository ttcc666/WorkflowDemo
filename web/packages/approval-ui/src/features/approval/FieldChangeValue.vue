<script setup lang="ts">
import { computed } from 'vue'
import FormValueText from '../forms/FormValueText.vue'
import type { FormField, FormValues } from '../../contracts/forms'

/** 修改记录中的字段值：明细表逐行列出各列的值，其他字段按类型显示。 */
const props = defineProps<{ field: FormField; value: unknown }>()

const rows = computed(() =>
  props.field.type === 'Table' && Array.isArray(props.value) ? (props.value as FormValues[]).filter((r) => r && typeof r === 'object') : null,
)
</script>

<template>
  <template v-if="rows">
    <div v-for="(row, index) in rows" :key="index" class="change-row">
      <template v-for="(column, i) in field.columns ?? []" :key="column.key">
        <span v-if="i" class="change-row__sep">/</span>
        <FormValueText :field="column" :value="row[column.key]" />
      </template>
    </div>
    <span v-if="rows.length === 0">-</span>
  </template>
  <FormValueText v-else :field="field" :value="value" />
</template>

<style scoped>
.change-row + .change-row {
  margin-top: 2px;
}

.change-row__sep {
  margin: 0 4px;
  color: #c0c4cc;
}
</style>
