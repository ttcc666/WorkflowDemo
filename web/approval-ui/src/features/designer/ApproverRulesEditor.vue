<script setup lang="ts">
import type { DemoUser } from '@/api/types'
import { approverTypeLabels, valuelessApproverTypes, type ApproverRule } from '@/features/flow/flowModel'

/** 审批人 / 通知接收人规则列表：类型 + 参数（角色、用户、表单字段）。 */
const props = withDefaults(
  defineProps<{
    types: string[]
    roles: string[]
    users: DemoUser[]
    /** 至少保留的规则数 */
    min?: number
    defaultType?: string
  }>(),
  { min: 1, defaultType: 'Role' },
)

const rules = defineModel<ApproverRule[]>({ required: true })

function update(index: number, patch: Partial<ApproverRule>) {
  rules.value = rules.value.map((r, i) => (i === index ? { ...r, ...patch } : r))
}

const add = () => (rules.value = [...rules.value, { type: props.defaultType, value: '' }])
const remove = (index: number) => (rules.value = rules.value.filter((_, i) => i !== index))
const splitIds = (value: string) => value.split(',').map((s) => s.trim()).filter(Boolean)
</script>

<template>
  <div class="rules">
    <div v-for="(rule, index) in rules" :key="index" class="rule">
      <el-select :model-value="rule.type" class="rule__type" @update:model-value="(v: string) => update(index, { type: v, value: '' })">
        <el-option v-for="t in types" :key="t" :label="approverTypeLabels[t] ?? t" :value="t" />
      </el-select>

      <el-select v-if="rule.type === 'Role'" :model-value="rule.value" class="rule__value" filterable allow-create
        placeholder="选择角色" @update:model-value="(v: string) => update(index, { value: v })">
        <el-option v-for="r in roles" :key="r" :label="r" :value="r" />
      </el-select>
      <el-select v-else-if="rule.type === 'User'" :model-value="splitIds(rule.value)" class="rule__value" multiple
        filterable collapse-tags placeholder="选择用户" @update:model-value="(v: string[]) => update(index, { value: v.join(',') })">
        <el-option v-for="u in users" :key="u.id" :label="u.id" :value="u.id" />
      </el-select>
      <el-input v-else-if="!valuelessApproverTypes.has(rule.type)" :model-value="rule.value" class="rule__value"
        :placeholder="rule.type === 'FormField' ? '字段名，如 reviewerId' : '参数'"
        @update:model-value="(v: string) => update(index, { value: v })" />
      <span v-else class="rule__value rule__value--static">{{ approverTypeLabels[rule.type] ?? rule.type }}</span>

      <el-button link type="danger" :disabled="rules.length <= min" @click="remove(index)">删除</el-button>
    </div>
    <el-button link type="primary" class="rules__add" @click="add">+ 添加</el-button>
  </div>
</template>

<style scoped>
.rules {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
}

.rule {
  display: flex;
  align-items: center;
  gap: 6px;
}

.rule__type {
  width: 110px;
  flex: none;
}

.rule__value {
  flex: 1;
  min-width: 0;
}

.rule__value--static {
  color: #606266;
  font-size: 12px;
}

.rules__add {
  align-self: flex-start;
}
</style>
