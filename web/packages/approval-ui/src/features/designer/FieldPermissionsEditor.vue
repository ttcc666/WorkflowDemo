<script setup lang="ts">
import { computed } from 'vue'
import type { FieldAccess } from '@ui/contracts/flow'
import { fieldTypeLabels, type FormField } from '@ui/features/forms/formModel'

/**
 * 节点的表单字段权限：只读（默认）/ 可编辑（仅审批节点，计算字段除外）/ 隐藏。
 * 字段改名或删除后残留的设置单独列出，可一键清除（发布时服务端也会校验）。
 */
const props = defineProps<{
  fields: FormField[]
  nodeType: 'Approval' | 'Cc'
}>()

const permissions = defineModel<Record<string, FieldAccess>>({ required: true })

type Choice = 'ReadOnly' | FieldAccess

const formFields = computed(() => props.fields.filter((f) => f.type !== 'Description'))
const known = computed(() => new Set(formFields.value.map((f) => f.key.toLowerCase())))
const stale = computed(() => Object.keys(permissions.value).filter((key) => !known.value.has(key.toLowerCase())))

/** 字段标识不区分大小写（与服务端一致） */
const keyOf = (field: FormField) => Object.keys(permissions.value).find((k) => k.toLowerCase() === field.key.toLowerCase())
const choiceOf = (field: FormField): Choice => {
  const key = keyOf(field)
  return key ? (permissions.value[key] ?? 'ReadOnly') : 'ReadOnly'
}

function choices(field: FormField): { value: Choice; label: string }[] {
  const editable = props.nodeType === 'Approval' && field.type !== 'Formula'
  return [
    { value: 'ReadOnly', label: '只读' },
    ...(editable ? [{ value: 'Editable' as const, label: '可编辑' }] : []),
    { value: 'Hidden', label: '隐藏' },
  ]
}

function set(field: FormField, choice: Choice) {
  const next = { ...permissions.value }
  const key = keyOf(field)
  if (key) delete next[key]
  if (choice !== 'ReadOnly') next[field.key] = choice
  permissions.value = next
}

function clearStale() {
  permissions.value = Object.fromEntries(Object.entries(permissions.value).filter(([key]) => known.value.has(key.toLowerCase())))
}
</script>

<template>
  <div class="field-permissions">
    <div class="field-permissions__help">
      <div v-if="nodeType === 'Approval'">
        <strong>可编辑</strong>：表单审批的审批人同意时可修改，提交后整表重新校验与计算，轨迹记录修改前后的值。
      </div>
      <div>
        <strong>隐藏</strong>：只以本节点{{ nodeType === 'Approval' ? '审批人' : '抄送人' }}身份查看的人看不到该字段；
        发起人、管理员始终可见。标题与通知模板不要引用隐藏字段。
      </div>
    </div>

    <el-empty v-if="formFields.length === 0" :image-size="48" description="流程没有表单字段，请先在“表单”页签添加" />
    <div v-for="field in formFields" :key="field.key" class="field-permissions__row">
      <div class="field-permissions__field">
        <span class="field-permissions__label">{{ field.label || field.key }}</span>
        <span class="field-permissions__meta">{{ fieldTypeLabels[field.type] }} · {{ field.key }}</span>
      </div>
      <el-radio-group :model-value="choiceOf(field)" size="small" @update:model-value="(v) => set(field, v as Choice)">
        <el-radio-button v-for="c in choices(field)" :key="c.value" :value="c.value">{{ c.label }}</el-radio-button>
      </el-radio-group>
    </div>

    <el-alert v-if="stale.length" type="warning" :closable="false" show-icon class="field-permissions__stale"
      :title="`表单中已没有字段：${stale.join('、')}`">
      <el-button link type="primary" size="small" @click="clearStale">清除这些设置</el-button>
    </el-alert>
  </div>
</template>

<style scoped>
.field-permissions {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.field-permissions__help {
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}

.field-permissions__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.field-permissions__field {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.field-permissions__label {
  overflow: hidden;
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.field-permissions__meta {
  color: #909399;
  font-size: 12px;
}

.field-permissions__stale {
  margin-top: 4px;
}
</style>
