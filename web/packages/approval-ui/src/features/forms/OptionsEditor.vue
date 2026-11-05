<script setup lang="ts">
import type { FormFieldOption } from '@ui/contracts/forms'

/** 选项列表编辑：显示名称 + 值（值用于条件表达式，如 leaveType == "annual"）。 */
const options = defineModel<FormFieldOption[]>({ required: true })

function update(index: number, patch: Partial<FormFieldOption>) {
  options.value = options.value.map((o, i) => (i === index ? { ...o, ...patch } : o))
}

function add() {
  const values = new Set(options.value.map((o) => o.value))
  let n = options.value.length + 1
  while (values.has(`option${n}`)) n++
  options.value = [...options.value, { label: `选项${n}`, value: `option${n}` }]
}

const remove = (index: number) => (options.value = options.value.filter((_, i) => i !== index))

function move(index: number, delta: number) {
  const target = index + delta
  if (target < 0 || target >= options.value.length) return
  const next = [...options.value]
  ;[next[index], next[target]] = [next[target]!, next[index]!]
  options.value = next
}
</script>

<template>
  <div class="options">
    <div class="options__head">
      <span>显示名称</span>
      <span>值</span>
    </div>
    <div v-for="(option, index) in options" :key="index" class="options__row">
      <el-input :model-value="option.label" size="small" placeholder="名称" @update:model-value="(v: string) => update(index, { label: v })" />
      <el-input :model-value="option.value" size="small" placeholder="值" @update:model-value="(v: string) => update(index, { value: v })" />
      <el-button link size="small" :disabled="index === 0" @click="move(index, -1)">↑</el-button>
      <el-button link type="danger" size="small" @click="remove(index)">删除</el-button>
    </div>
    <el-button link type="primary" size="small" @click="add">+ 添加选项</el-button>
  </div>
</template>

<style scoped>
.options {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 100%;
}

.options__head,
.options__row {
  display: grid;
  grid-template-columns: 1fr 1fr auto auto;
  align-items: center;
  gap: 4px;
}

.options__head {
  color: #909399;
  font-size: 12px;
}

.options__row > :nth-child(3),
.options__row > :nth-child(4) {
  margin-left: 0;
}
</style>
