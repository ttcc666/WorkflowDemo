<script setup lang="ts">
import { computed } from 'vue'
import type { ChannelMeta, TemplateVariableMeta } from '../../api/types'
import {
  defaultNotification,
  defaultTemplates,
  notifyEventLabels,
  type NotificationRule,
  type NotifyEvent,
} from '../flow/flowModel'
import ApproverRulesEditor from './ApproverRulesEditor.vue'

/** 通知规则列表：触发事件 × 接收人 × 渠道 × 消息模板。 */
const props = defineProps<{
  events: NotifyEvent[]
  channels: ChannelMeta[]
  recipientTypes: string[]
  variables: TemplateVariableMeta[]
  roles: string[]
}>()

const rules = defineModel<NotificationRule[]>({ required: true })

const recipientHelp: Record<string, string> = {
  Assignees: '当前审批人：到达节点时为新待办的处理人，转交时为转交对象，超时为超时待办的处理人',
  Approvers: '已审批过的人：本流程中已同意过的所有人',
}
const shownVariables = computed(() => props.variables.filter((v) => !v.name.includes('.')))

function update(index: number, patch: Partial<NotificationRule>) {
  rules.value = rules.value.map((r, i) => (i === index ? { ...r, ...patch } : r))
}

const add = () => (rules.value = [...rules.value, defaultNotification(props.events[0] ?? 'Enter')])
const remove = (index: number) => (rules.value = rules.value.filter((_, i) => i !== index))

/** 默认模板取第一个事件对应的模板，作为输入框的占位提示 */
const placeholderOf = (rule: NotificationRule) => (rule.events[0] ? defaultTemplates[rule.events[0]] : '消息内容，可使用 {变量}')

// 记住每条规则最近聚焦的内容输入框，点击变量时插入到光标处
const contentInputs = new Map<number, HTMLTextAreaElement>()

function insertVariable(index: number, name: string) {
  const token = `{${name}}`
  const content = rules.value[index]?.content ?? ''
  const input = contentInputs.get(index)
  const start = input?.selectionStart ?? content.length
  const end = input?.selectionEnd ?? content.length
  update(index, { content: content.slice(0, start) + token + content.slice(end) })
  requestAnimationFrame(() => {
    input?.focus()
    input?.setSelectionRange(start + token.length, start + token.length)
  })
}
</script>

<template>
  <div class="notify">
    <el-empty v-if="rules.length === 0" :image-size="48" description="未配置通知" />

    <div v-for="(rule, index) in rules" :key="index" class="notify__rule">
      <div class="notify__header">
        <span>通知 {{ index + 1 }}</span>
        <el-button link type="danger" size="small" @click="remove(index)">删除</el-button>
      </div>

      <div class="notify__label">什么时候发</div>
      <el-checkbox-group :model-value="rule.events" size="small"
        @update:model-value="(v) => update(index, { events: v as NotifyEvent[] })">
        <el-checkbox v-for="e in events" :key="e" :value="e" :label="notifyEventLabels[e]" />
      </el-checkbox-group>

      <div class="notify__label">发给谁</div>
      <ApproverRulesEditor :model-value="rule.recipients" :types="recipientTypes" :roles="roles"
        default-type="Assignees" @update:model-value="(v) => update(index, { recipients: v })" />
      <div v-for="r in rule.recipients.filter((r) => recipientHelp[r.type])" :key="r.type" class="notify__help">
        {{ recipientHelp[r.type] }}
      </div>

      <div class="notify__label">用什么渠道</div>
      <el-checkbox-group :model-value="rule.channels" size="small"
        @update:model-value="(v) => update(index, { channels: v as string[] })">
        <el-checkbox v-for="c in channels" :key="c.name" :value="c.name" :label="c.displayName" />
      </el-checkbox-group>

      <div class="notify__label">消息模板</div>
      <el-input :model-value="rule.title ?? ''" size="small" placeholder="标题（留空使用默认标题）" maxlength="100"
        @update:model-value="(v: string) => update(index, { title: v || null })" />
      <el-input :model-value="rule.content ?? ''" type="textarea" :rows="3" size="small" :placeholder="placeholderOf(rule)"
        class="notify__content" @update:model-value="(v: string) => update(index, { content: v || null })"
        @focus="(e: FocusEvent) => contentInputs.set(index, e.target as HTMLTextAreaElement)" />
      <div class="notify__vars">
        <el-tooltip v-for="v in shownVariables" :key="v.name" :content="`${v.description}，也可写 {${v.alias}}`" placement="top">
          <el-tag size="small" effect="plain" class="notify__var" @click="insertVariable(index, v.name)">{{ v.name }}</el-tag>
        </el-tooltip>
        <el-tooltip content="表单字段，如 {form.certNo}" placement="top">
          <el-tag size="small" effect="plain" type="info" class="notify__var" @click="insertVariable(index, 'form.')">form.字段</el-tag>
        </el-tooltip>
      </div>
    </div>

    <el-button size="small" plain type="primary" class="notify__add" @click="add">+ 添加通知</el-button>
  </div>
</template>

<style scoped>
.notify {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.notify__rule {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 8px 10px 10px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafbfc;
}

.notify__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: #303133;
  font-weight: 600;
}

.notify__label {
  margin-top: 4px;
  color: #606266;
  font-size: 12px;
}

.notify__help {
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}

.notify__content {
  margin-top: 4px;
}

.notify__vars {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 2px;
}

.notify__var {
  cursor: pointer;
}

.notify__add {
  align-self: flex-start;
}

.notify :deep(.el-checkbox) {
  margin-right: 12px;
}
</style>
