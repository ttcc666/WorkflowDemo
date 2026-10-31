<script setup lang="ts">
import type { ActionMeta } from '../../api/types'
import type { ActionMode, ActionStep } from '../flow/flowModel'

/** 动作节点的步骤列表：参数表单根据后端注册动作的参数描述自动生成。 */
const props = defineProps<{ actions: ActionMeta[] }>()

const steps = defineModel<ActionStep[]>({ required: true })

const metaOf = (type: string) => props.actions.find((a) => a.type === type)

function update(index: number, patch: Partial<ActionStep>) {
  steps.value = steps.value.map((s, i) => (i === index ? { ...s, ...patch } : s))
}

function setParam(index: number, name: string, value: string) {
  const step = steps.value[index]
  if (step) update(index, { parameters: { ...step.parameters, [name]: value } })
}

/** 切换动作类型时重置执行方式、参数默认值和输出变量 */
function stepFor(type: string): Partial<ActionStep> {
  const meta = metaOf(type)
  return {
    type,
    mode: meta && !meta.supportsSync ? 'Async' : 'Sync',
    parameters: Object.fromEntries((meta?.parameters ?? []).filter((p) => p.defaultValue).map((p) => [p.name, p.defaultValue!])),
    outputVariable: null,
  }
}

const changeType = (index: number, type: string) => update(index, stepFor(type))

// 一次性赋值：defineModel 的新值要等父组件回传，连续两次修改时后一次会基于旧列表覆盖前一次
const add = () =>
  (steps.value = [...steps.value, { type: '', mode: 'Sync', parameters: {}, ...stepFor(props.actions[0]?.type ?? '') }])

const remove = (index: number) => (steps.value = steps.value.filter((_, i) => i !== index))

function move(index: number, delta: number) {
  const list = [...steps.value]
  const [item] = list.splice(index, 1)
  list.splice(index + delta, 0, item!)
  steps.value = list
}
</script>

<template>
  <div class="steps">
    <el-empty v-if="steps.length === 0" :image-size="48" description="未配置动作" />

    <div v-for="(step, index) in steps" :key="index" class="step">
      <div class="step__header">
        <span>步骤 {{ index + 1 }}</span>
        <span>
          <el-button link size="small" :disabled="index === 0" @click="move(index, -1)">上移</el-button>
          <el-button link size="small" :disabled="index === steps.length - 1" @click="move(index, 1)">下移</el-button>
          <el-button link type="danger" size="small" @click="remove(index)">删除</el-button>
        </span>
      </div>

      <el-form label-position="top" size="small">
        <el-form-item label="动作">
          <el-select :model-value="step.type" @update:model-value="(v: string) => changeType(index, v)">
            <el-option v-for="a in actions" :key="a.type" :label="a.displayName" :value="a.type" />
          </el-select>
          <div v-if="metaOf(step.type)?.description" class="step__help">{{ metaOf(step.type)?.description }}</div>
        </el-form-item>

        <el-form-item label="步骤名称">
          <el-input :model-value="step.name ?? ''" :placeholder="metaOf(step.type)?.displayName"
            @update:model-value="(v: string) => update(index, { name: v || null })" />
        </el-form-item>

        <el-form-item label="执行方式">
          <el-radio-group :model-value="step.mode" @update:model-value="(v) => update(index, { mode: v as ActionMode, outputVariable: null })">
            <el-radio-button value="Sync" :disabled="metaOf(step.type)?.supportsSync === false">同步</el-radio-button>
            <el-radio-button value="Async" :disabled="metaOf(step.type)?.supportsAsync === false">异步</el-radio-button>
          </el-radio-group>
          <div class="step__help">
            {{ step.mode === 'Sync' ? '在审批事务内执行，失败则本次操作回滚；结果可写入表单变量供后续分支使用' : '提交后由后台执行，失败自动重试，适合调用外部系统' }}
          </div>
        </el-form-item>

        <el-form-item v-for="p in metaOf(step.type)?.parameters ?? []" :key="p.name" :label="p.label" :required="p.required">
          <el-select v-if="p.kind === 'Select'" :model-value="step.parameters[p.name] ?? p.defaultValue ?? ''"
            @update:model-value="(v: string) => setParam(index, p.name, v)">
            <el-option v-for="o in p.options ?? []" :key="o" :label="o" :value="o" />
          </el-select>
          <el-switch v-else-if="p.kind === 'Boolean'" :model-value="step.parameters[p.name] === 'true'"
            @update:model-value="(v) => setParam(index, p.name, String(!!v))" />
          <el-input v-else :model-value="step.parameters[p.name] ?? ''" :placeholder="p.defaultValue ?? ''"
            :type="p.kind === 'TextArea' || p.kind === 'Code' ? 'textarea' : 'text'" :rows="p.kind === 'Code' ? 6 : 3"
            :class="{ 'step__code': p.kind === 'Code' }" @update:model-value="(v: string) => setParam(index, p.name, v)" />
          <div v-if="p.help" class="step__help">{{ p.help }}</div>
          <div v-if="p.template" class="step__help">支持占位符，如 {标题}、{form.字段}</div>
        </el-form-item>

        <template v-if="step.mode === 'Sync'">
          <el-form-item label="输出变量">
            <el-input :model-value="step.outputVariable ?? ''" placeholder="如 result，后续条件可写 result.xxx"
              @update:model-value="(v: string) => update(index, { outputVariable: v || null })" />
          </el-form-item>
          <el-checkbox :model-value="!!step.continueOnError" @update:model-value="(v) => update(index, { continueOnError: !!v })">
            失败时记录并继续流转
          </el-checkbox>
        </template>
      </el-form>
    </div>

    <el-button size="small" plain type="primary" class="steps__add" :disabled="actions.length === 0" @click="add">+ 添加动作</el-button>
  </div>
</template>

<style scoped>
.steps {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.step {
  padding: 8px 10px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafbfc;
}

.step__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
  font-weight: 600;
}

.step__help {
  margin-top: 2px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}

.step__code :deep(textarea) {
  font-family: Consolas, monospace;
  font-size: 12px;
}

.steps__add {
  align-self: flex-start;
}
</style>
