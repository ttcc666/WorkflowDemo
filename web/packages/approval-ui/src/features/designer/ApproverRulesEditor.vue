<script setup lang="ts">
import { computed } from 'vue'
import type { ApproverTypeMeta, ApproverValueKind } from '../../api/types'
import UserPicker from '../../components/UserPicker.vue'
import { userFields } from '../forms/formModel'
import { useDesignerForm } from './designerContext'
import { approverTypeLabels, formatLevel, levelApproverTypes, parseLevel, valuelessApproverTypes, type ApproverRule } from '../flow/flowModel'

/** 审批人 / 通知接收人规则列表：类型 + 参数，参数控件按类型描述（valueKind）生成。 */
const props = withDefaults(
  defineProps<{
    types: ApproverTypeMeta[]
    roles: string[]
    /** 至少保留的规则数 */
    min?: number
    defaultType?: string
  }>(),
  { min: 1, defaultType: 'Role' },
)

const rules = defineModel<ApproverRule[]>({ required: true })

// 流程带表单时，“表单人员字段”与级数的基准人员从人员字段中选择
const form = useDesignerForm()
const personFields = computed(() => userFields(form.value))

/** 规则类型的描述；元数据中没有（如已移除的自定义类型）时按内置兜底 */
function metaOf(type: string): ApproverTypeMeta {
  return (
    props.types.find((t) => t.type === type) ?? {
      type,
      displayName: approverTypeLabels[type] ?? type,
      valueKind: valuelessApproverTypes.has(type) ? 'None' : levelApproverTypes.has(type) ? 'Level' : 'Text',
      help: null,
      options: null,
      ordered: type === 'ManagerChain',
    }
  )
}

const kindOf = (type: string): ApproverValueKind => metaOf(type).valueKind
const typeOptions = computed(() => props.types.map((t) => ({ value: t.type, label: t.displayName })))

function update(index: number, patch: Partial<ApproverRule>) {
  rules.value = rules.value.map((r, i) => (i === index ? { ...r, ...patch } : r))
}

function changeType(index: number, type: string) {
  update(index, { type, value: kindOf(type) === 'Level' ? '1' : '' })
}

const add = () => (rules.value = [...rules.value, { type: props.defaultType, value: kindOf(props.defaultType) === 'Level' ? '1' : '' }])
const remove = (index: number) => (rules.value = rules.value.filter((_, i) => i !== index))
const splitIds = (value: string) => value.split(',').map((s) => s.trim()).filter(Boolean)

const levelOf = (value: string) => parseLevel(value) ?? { level: 1, field: null }
function updateLevel(index: number, patch: { level?: number | null; field?: string | null }) {
  const current = levelOf(rules.value[index]!.value)
  const level = patch.level === undefined ? current.level : Math.min(10, Math.max(1, patch.level ?? 1))
  const field = patch.field === undefined ? current.field : patch.field || null
  update(index, { value: formatLevel(level, field) })
}
</script>

<template>
  <div class="rules">
    <div v-for="(rule, index) in rules" :key="index" class="rule">
      <div class="rule__line">
        <el-select :model-value="rule.type" class="rule__type" @update:model-value="(v: string) => changeType(index, v)">
          <el-option v-for="t in typeOptions" :key="t.value" :label="t.label" :value="t.value" />
          <el-option v-if="!types.some((t) => t.type === rule.type)" :label="metaOf(rule.type).displayName" :value="rule.type" />
        </el-select>

        <el-select v-if="kindOf(rule.type) === 'Role'" :model-value="rule.value" class="rule__value" filterable allow-create
          placeholder="选择角色" @update:model-value="(v: string) => update(index, { value: v })">
          <el-option v-for="r in roles" :key="r" :label="r" :value="r" />
        </el-select>
        <UserPicker v-else-if="kindOf(rule.type) === 'User'" :model-value="splitIds(rule.value)" class="rule__value" multiple
          placeholder="选择用户" @update:model-value="(v) => update(index, { value: ((v as string[] | null) ?? []).join(',') })" />
        <el-select v-else-if="kindOf(rule.type) === 'FormUserField' && personFields.length" :model-value="rule.value" class="rule__value"
          placeholder="选择人员字段" @update:model-value="(v: string) => update(index, { value: v })">
          <el-option v-for="f in personFields" :key="f.key" :label="`${f.label}（${f.key}）`" :value="f.key" />
        </el-select>
        <template v-else-if="kindOf(rule.type) === 'Level'">
          <el-input-number :model-value="levelOf(rule.value).level" :min="1" :max="10" controls-position="right" class="rule__level"
            title="级数" @update:model-value="(v) => updateLevel(index, { level: v })" />
          <el-select :model-value="levelOf(rule.value).field ?? ''" class="rule__value" title="以谁为基准"
            @update:model-value="(v: string) => updateLevel(index, { field: v })">
            <el-option label="发起人" value="" />
            <el-option v-for="f in personFields" :key="f.key" :label="`${f.label}（${f.key}）`" :value="f.key" />
          </el-select>
        </template>
        <el-select v-else-if="kindOf(rule.type) === 'Text' && metaOf(rule.type).options?.length" :model-value="rule.value"
          class="rule__value" @update:model-value="(v: string) => update(index, { value: v })">
          <el-option v-for="o in metaOf(rule.type).options" :key="o" :label="o" :value="o" />
        </el-select>
        <el-input v-else-if="kindOf(rule.type) !== 'None'" :model-value="rule.value" class="rule__value"
          :placeholder="kindOf(rule.type) === 'FormUserField' ? '字段名，如 reviewerId' : '参数'"
          @update:model-value="(v: string) => update(index, { value: v })" />
        <span v-else class="rule__value rule__value--static">{{ metaOf(rule.type).displayName }}</span>

        <el-button link type="danger" :disabled="rules.length <= min" @click="remove(index)">删除</el-button>
      </div>
      <div v-if="metaOf(rule.type).help && kindOf(rule.type) === 'Level'" class="rule__help">{{ metaOf(rule.type).help }}</div>
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

.rule__line {
  display: flex;
  align-items: center;
  gap: 6px;
}

.rule__type {
  width: 110px;
  flex: none;
}

.rule__level {
  width: 80px;
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

.rule__help {
  margin-top: 2px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}

.rules__add {
  align-self: flex-start;
}
</style>
