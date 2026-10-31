<script setup lang="ts">
import { reactive, useTemplateRef, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'

const props = defineProps<{ existingKeys: string[] }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ create: [key: string, name: string] }>()

const form = reactive({ key: '', name: '' })
const formRef = useTemplateRef<FormInstance>('formRef')

watch(visible, (open) => {
  if (!open) return
  form.key = ''
  form.name = ''
  formRef.value?.clearValidate()
})

const rules: FormRules<typeof form> = {
  key: [
    { required: true, message: '请输入流程标识', trigger: 'blur' },
    { pattern: /^[a-z][a-z0-9._-]{1,99}$/, message: '小写字母开头，可含数字、点、横线、下划线', trigger: 'blur' },
    {
      validator: (_r, value: string, cb) => (props.existingKeys.includes(value) ? cb(new Error('该标识已存在')) : cb()),
      trigger: 'blur',
    },
  ],
  name: [{ required: true, message: '请输入流程名称', trigger: 'blur' }],
}

async function submit() {
  if (!(await formRef.value?.validate().catch(() => false))) return
  emit('create', form.key, form.name.trim())
  visible.value = false
}
</script>

<template>
  <el-dialog v-model="visible" title="新建流程" width="460px">
    <el-form ref="formRef" :model="form" :rules="rules" label-width="80px">
      <el-form-item label="流程标识" prop="key">
        <el-input v-model="form.key" placeholder="如 contract.sign" />
      </el-form-item>
      <el-form-item label="流程名称" prop="name">
        <el-input v-model="form.name" placeholder="如 合同签署审批" />
      </el-form-item>
    </el-form>
    <div class="dialog-tip">业务代码通过流程标识发起审批（StartApprovalRequest.DefinitionKey），发布后即可使用。</div>
    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" @click="submit">开始设计</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.dialog-tip {
  color: #909399;
  font-size: 12px;
}
</style>
