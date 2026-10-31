<script setup lang="ts">
import { computed, ref, shallowRef, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { approvalApi } from '../api/approval'
import { ApiError, errorMessage } from '../api/http'
import { useAsyncData } from '../composables/useAsyncData'
import DynamicForm from '../features/forms/DynamicForm.vue'
import { initialValues, type FormValues } from '../features/forms/formModel'
import { approvalRouteNames } from '../routes'

/** 发起页：按流程的表单结构填写并提交；服务端字段错误回显到对应表单项。 */
const route = useRoute()
const router = useRouter()
const key = computed(() => route.params.key as string)

const start = useAsyncData((signal) => approvalApi.startForm(key.value, signal), { watch: [key], notifyError: false })
// 深层响应：DynamicForm 就地修改字段值
const data = ref<FormValues>({})
const comment = shallowRef('')
const serverErrors = shallowRef<Record<string, string[]>>()
const submitting = shallowRef(false)
const formRef = useTemplateRef<InstanceType<typeof DynamicForm>>('form')

watch(start.data, (value) => {
  data.value = initialValues(value?.form)
  serverErrors.value = undefined
}, { immediate: true })

async function submit() {
  const definition = start.data.value
  if (!definition) return
  serverErrors.value = undefined
  if (!(await formRef.value?.validate())) return ElMessage.warning('请检查标红的字段')

  submitting.value = true
  try {
    const { instanceId } = await approvalApi.submitForm(definition.key, data.value, comment.value.trim())
    ElMessage.success('已提交')
    await router.replace({ name: approvalRouteNames.detail, params: { id: instanceId } })
  } catch (e) {
    if (e instanceof ApiError && e.errors) serverErrors.value = e.errors
    ElMessage.error(errorMessage(e))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div v-loading="start.loading.value" class="start-form">
    <el-page-header title="返回" @back="router.push({ name: approvalRouteNames.start })">
      <template #content>{{ start.data.value?.name ?? '发起申请' }}</template>
    </el-page-header>

    <el-result v-if="start.error.value" icon="warning" title="无法发起" :sub-title="start.error.value">
      <template #extra>
        <el-button type="primary" @click="router.push({ name: approvalRouteNames.start })">返回发起中心</el-button>
      </template>
    </el-result>

    <el-card v-else-if="start.data.value" shadow="never">
      <div v-if="start.data.value.settings.description" class="start-form__desc">{{ start.data.value.settings.description }}</div>
      <DynamicForm ref="form" v-model="data" :schema="start.data.value.form" :errors="serverErrors" />
      <el-form label-position="top">
        <el-form-item label="申请说明">
          <el-input v-model="comment" type="textarea" :rows="2" maxlength="500" show-word-limit placeholder="选填，将记录在审批轨迹中" />
        </el-form-item>
      </el-form>
      <div class="start-form__actions">
        <el-button @click="router.push({ name: approvalRouteNames.start })">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submit">提交申请</el-button>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.start-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
  max-width: 960px;
}

.start-form__desc {
  margin-bottom: 16px;
  color: #909399;
  font-size: 13px;
}

.start-form__actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
