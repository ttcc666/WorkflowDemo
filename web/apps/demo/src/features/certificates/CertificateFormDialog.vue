<script setup lang="ts">
import { computed, reactive, useTemplateRef, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import type { Certificate, CertificateData } from '@/api/types'
import { certTypeLabels } from '@/utils/labels'

/** 证书新增 / 变更申请表单。certificate 为空表示新增。 */
const props = defineProps<{ certificate: Certificate | null; submitting: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ submit: [data: CertificateData] }>()

const empty: CertificateData = { certNo: '', name: '', holder: '', certType: 'Normal', issueDate: '', expireDate: '' }
const form = reactive<CertificateData>({ ...empty })
const formRef = useTemplateRef<FormInstance>('formRef')

const isCreate = computed(() => !props.certificate)

// 每次打开时以当前证书数据为初始值
watch(visible, (open) => {
  if (!open) return
  const { certNo, name, holder, certType, issueDate, expireDate } = props.certificate ?? empty
  Object.assign(form, { certNo, name, holder, certType, issueDate, expireDate })
  formRef.value?.clearValidate()
})

const rules: FormRules<CertificateData> = {
  certNo: [{ required: true, message: '请输入证书编号', trigger: 'blur' }],
  name: [{ required: true, message: '请输入证书名称', trigger: 'blur' }],
  holder: [{ required: true, message: '请输入持有人', trigger: 'blur' }],
  issueDate: [{ required: true, message: '请选择发证日期', trigger: 'change' }],
  expireDate: [
    { required: true, message: '请选择到期日期', trigger: 'change' },
    {
      validator: (_rule, value: string, callback) =>
        form.issueDate && value && value <= form.issueDate ? callback(new Error('到期日期必须晚于发证日期')) : callback(),
      trigger: 'change',
    },
  ],
}

async function submit() {
  if (!(await formRef.value?.validate().catch(() => false))) return
  emit('submit', { ...form, certNo: form.certNo.trim(), name: form.name.trim(), holder: form.holder.trim() })
}
</script>

<template>
  <el-dialog v-model="visible" :title="isCreate ? '申请新增证书' : `申请变更证书 ${certificate?.certNo}`" width="560px">
    <el-alert type="info" :closable="false" show-icon class="form-tip"
      :title="isCreate ? '提交后进入审核，审核通过后证书才会生成' : '提交后进入审核，审核期间证书保持原内容，通过后才生效'" />

    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="证书编号" prop="certNo">
        <el-input v-model="form.certNo" :disabled="!isCreate" placeholder="如 C-1001" />
      </el-form-item>
      <el-form-item label="证书名称" prop="name">
        <el-input v-model="form.name" />
      </el-form-item>
      <el-form-item label="持有人" prop="holder">
        <el-input v-model="form.holder" />
      </el-form-item>
      <el-form-item label="证书类型" prop="certType">
        <el-radio-group v-model="form.certType">
          <el-radio v-for="(label, value) in certTypeLabels" :key="value" :value="value">{{ label }}</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="发证日期" prop="issueDate">
        <el-date-picker v-model="form.issueDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
      </el-form-item>
      <el-form-item label="到期日期" prop="expireDate">
        <el-date-picker v-model="form.expireDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :loading="submitting" @click="submit">提交审核</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.form-tip {
  margin-bottom: 16px;
}
</style>
