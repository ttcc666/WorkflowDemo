<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { fieldVariables, type FlowSettings, type FormSchema } from './formModel'

/** 发起设置：是否出现在发起中心、分类、描述、发起角色、标题模板。 */
const props = defineProps<{ form: FormSchema; roles: string[] }>()
const settings = defineModel<FlowSettings>({ required: true })

const categories = ['人事', '财务', '行政', '采购', 'IT', '其他']
const titleInput = useTemplateRef<{ input?: HTMLInputElement }>('titleInput')

const variables = computed(() => [
  { name: '流程名', label: '流程名称' },
  { name: '申请人', label: '发起人 ID' },
  { name: '发起时间', label: '发起时间' },
  ...fieldVariables(props.form)
    .filter((v) => v.type !== 'Users')
    .map((v) => ({ name: `form.${v.name}`, label: v.label })),
])

function update(patch: Partial<FlowSettings>) {
  settings.value = { ...settings.value, ...patch }
}

function insertVariable(name: string) {
  const token = `{${name}}`
  const current = settings.value.titleTemplate ?? ''
  const input = titleInput.value?.input
  const start = input?.selectionStart ?? current.length
  const end = input?.selectionEnd ?? current.length
  update({ titleTemplate: current.slice(0, start) + token + current.slice(end) })
  requestAnimationFrame(() => {
    input?.focus()
    input?.setSelectionRange(start + token.length, start + token.length)
  })
}
</script>

<template>
  <el-form label-position="top" class="settings" @submit.prevent>
    <el-form-item label="在发起中心显示">
      <el-switch :model-value="!!settings.startable" @update:model-value="(v) => update({ startable: v === true })" />
      <div class="settings__help">
        打开后用户可在“发起申请”中直接填写表单发起审批（表单审批，无需业务代码）。
        由业务模块发起的流程（如证书审核）请保持关闭。
      </div>
    </el-form-item>

    <template v-if="settings.startable">
      <el-form-item label="分类">
        <el-select :model-value="settings.category ?? ''" filterable allow-create clearable placeholder="选择或输入分类"
          @update:model-value="(v: string) => update({ category: v || null })">
          <el-option v-for="c in categories" :key="c" :label="c" :value="c" />
        </el-select>
      </el-form-item>

      <el-form-item label="描述">
        <el-input :model-value="settings.description ?? ''" type="textarea" :rows="2" maxlength="500" show-word-limit
          placeholder="在发起中心卡片上显示" @update:model-value="(v: string) => update({ description: v || null })" />
      </el-form-item>

      <el-form-item label="允许发起的角色">
        <el-select :model-value="settings.initiatorRoles ?? []" multiple filterable allow-create placeholder="不限（所有人可发起）"
          @update:model-value="(v: string[]) => update({ initiatorRoles: v })">
          <el-option v-for="r in roles" :key="r" :label="r" :value="r" />
        </el-select>
      </el-form-item>
    </template>

    <el-form-item label="审批标题">
      <el-input ref="titleInput" :model-value="settings.titleTemplate ?? ''" maxlength="200" placeholder="{流程名} - {申请人}"
        @update:model-value="(v: string) => update({ titleTemplate: v || null })" />
      <div class="settings__vars">
        <el-tooltip v-for="v in variables" :key="v.name" :content="v.label" placement="top">
          <el-tag size="small" effect="plain" class="settings__var" @click="insertVariable(v.name)">{{ v.name }}</el-tag>
        </el-tooltip>
      </div>
      <div class="settings__help">在发起中心发起时按模板生成标题，例如 <code>{申请人}的请假申请（{form.days} 天）</code>；留空为“流程名 - 申请人”。</div>
    </el-form-item>
  </el-form>
</template>

<style scoped>
.settings {
  max-width: 640px;
}

.settings__help {
  width: 100%;
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}

.settings__help code {
  padding: 0 3px;
  border-radius: 3px;
  background: #f4f4f5;
}

.settings__vars {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 6px;
}

.settings__var {
  cursor: pointer;
}
</style>
