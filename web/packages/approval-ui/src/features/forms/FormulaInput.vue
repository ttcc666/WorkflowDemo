<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { parseFormula } from './formula'

/** 公式输入：点击变量插入到光标处，实时提示语法错误与不存在的字段。 */
const props = defineProps<{
  /** 可引用的数值变量（字段标识与名称） */
  variables: { name: string; label: string }[]
  placeholder?: string
}>()

const model = defineModel<string>({ required: true })
const input = useTemplateRef<{ textarea?: HTMLTextAreaElement }>('input')

const error = computed(() => {
  if (!model.value.trim()) return ''
  const { formula, error } = parseFormula(model.value)
  if (error) return error
  const known = new Set(props.variables.map((v) => v.name.toLowerCase()))
  const missing = formula!.identifiers.filter((name) => !known.has(name.toLowerCase()))
  return missing.length ? `引用了不存在或不是数值的字段：${missing.join('、')}` : ''
})

function insert(name: string) {
  const textarea = input.value?.textarea
  const value = model.value
  const start = textarea?.selectionStart ?? value.length
  const end = textarea?.selectionEnd ?? value.length
  model.value = value.slice(0, start) + name + value.slice(end)
  requestAnimationFrame(() => {
    textarea?.focus()
    textarea?.setSelectionRange(start + name.length, start + name.length)
  })
}
</script>

<template>
  <div class="formula-input">
    <el-input ref="input" v-model="model" type="textarea" :rows="2" maxlength="500" :placeholder="placeholder ?? '如 qty * price'"
      class="formula-input__text" />
    <div v-if="error" class="formula-input__error">{{ error }}</div>
    <div class="formula-input__vars">
      <el-tooltip v-for="v in variables" :key="v.name" :content="v.label" placement="top">
        <el-tag size="small" effect="plain" class="formula-input__var" @click="insert(v.name)">{{ v.name }}</el-tag>
      </el-tooltip>
      <span v-if="!variables.length" class="formula-input__help">还没有可引用的数值字段</span>
    </div>
    <div class="formula-input__help">
      支持 + - * / ( )，函数 round(x, 2)、min、max、abs、if(a &gt; 1000, 1, 0)、ifnull(x, 0)；空值参与运算结果为空，可用 ifnull 给默认值。
    </div>
  </div>
</template>

<style scoped>
.formula-input {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 100%;
}

.formula-input__text :deep(textarea) {
  font-family: Consolas, Menlo, monospace;
}

.formula-input__error {
  color: var(--el-color-danger);
  font-size: 12px;
  line-height: 1.5;
}

.formula-input__vars {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.formula-input__var {
  cursor: pointer;
}

.formula-input__help {
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}
</style>
