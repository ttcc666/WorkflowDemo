<script setup lang="ts">
import { computed, reactive, useTemplateRef, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import type { Certificate, CertificateData } from '@/api/types'
import { certTypeLabels } from '@/utils/labels'

/** 证书新增 / 变更申请表单。certificate 为空表示新增。 */
const props = defineProps<{ certificate: Certificate | null; submitting: boolean; initialData?: CertificateData; serverErrors?: Record<string, string[]> }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ submit: [data: CertificateData] }>()

const empty: CertificateData = { certNo: '', name: '', holder: '', certType: 'Normal', issueDate: '', expireDate: '' }
const form = reactive<CertificateData>({ ...empty })
const formRef = useTemplateRef<FormInstance>('formRef')

const isCreate = computed(() => !props.certificate)

// 每次打开时以当前证书数据为初始值
watch(visible, (open) => {
  if (!open) return
  const { certNo, name, holder, certType, issueDate, expireDate } = props.initialData ?? props.certificate ?? empty
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
  <el-dialog v-model="visible" :title="isCreate ? '申请登记注册证' : `申请变更注册证 ${certificate?.certNo}`" width="560px">
    <el-alert type="info" :closable="false" show-icon class="form-tip"
      :title="isCreate ? '部门负责人初审 → 质量/法规复审 → 负责人终审，通过后登记入库' : '审核期间保留原内容，通过后才生效；历史申请保留提交时快照'" />

    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="注册证编号" prop="certNo" :error="serverErrors?.certNo?.join('；')">
        <el-input v-model="form.certNo" :disabled="!isCreate" :maxlength="100" placeholder="填写注册证编号" />
      </el-form-item>
      <el-form-item label="注册证名称" prop="name" :error="serverErrors?.name?.join('；')">
        <el-input v-model="form.name" :maxlength="200" />
      </el-form-item>
      <el-form-item label="持有人" prop="holder" :error="serverErrors?.holder?.join('；')">
        <el-input v-model="form.holder" :maxlength="200" />
      </el-form-item>
      <el-form-item label="证书类型" prop="certType" :error="serverErrors?.certType?.join('；')">
        <el-radio-group v-model="form.certType">
          <el-radio v-for="(label, value) in certTypeLabels" :key="value" :value="value">{{ label }}</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="发证日期" prop="issueDate" :error="serverErrors?.issueDate?.join('；')">
        <el-date-picker v-model="form.issueDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
      </el-form-item>
      <el-form-item label="到期日期" prop="expireDate" :error="serverErrors?.expireDate?.join('；')">
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
