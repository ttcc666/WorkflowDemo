<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { ElMessage } from 'element-plus'
import { useApprovalClient } from '../../client'
import { errorMessage } from '../../api/http'
import FileList from './FileList.vue'
import type { FileValue, FormField } from '../../contracts/forms'

const { api: approvalApi } = useApprovalClient()

/** 附件字段的输入：选择文件后立即上传（未绑定，提交表单后绑定到申请），值为已上传文件的列表。 */
const props = defineProps<{ field: FormField }>()
const model = defineModel<unknown>()

const files = computed(() => (Array.isArray(model.value) ? (model.value as FileValue[]) : []))
const max = computed(() => props.field.maxFiles ?? 10)
const accept = computed(() => (props.field.accept?.length ? props.field.accept.map((e) => `.${e}`).join(',') : undefined))
const uploading = shallowRef(0)
const input = shallowRef<HTMLInputElement>()

async function onSelect(event: Event) {
  const target = event.target as HTMLInputElement
  const selected = [...(target.files ?? [])]
  target.value = ''
  for (const file of selected) {
    if (files.value.length >= max.value) {
      ElMessage.warning(`${props.field.label}最多上传 ${max.value} 个文件`)
      break
    }
    uploading.value++
    try {
      const uploaded = await approvalApi.uploadFile(file)
      model.value = [...files.value, uploaded]
    } catch (e) {
      ElMessage.error(`${file.name}：${errorMessage(e)}`)
    } finally {
      uploading.value--
    }
  }
}

const remove = (file: FileValue) => (model.value = files.value.filter((f) => f.id !== file.id))
</script>

<template>
  <div class="file-upload">
    <input ref="input" type="file" multiple :accept="accept" class="file-upload__input" @change="onSelect" />
    <el-button size="small" :loading="uploading > 0" :disabled="files.length >= max" @click="input?.click()">
      上传文件
    </el-button>
    <span class="file-upload__hint">
      最多 {{ max }} 个<template v-if="field.accept?.length">，支持 {{ field.accept.join('、') }}</template>
    </span>
    <FileList v-if="files.length" :files="files" removable class="file-upload__list" @remove="remove" />
  </div>
</template>

<style scoped>
.file-upload {
  width: 100%;
}

.file-upload__input {
  display: none;
}

.file-upload__hint {
  margin-left: 8px;
  color: #909399;
  font-size: 12px;
}

.file-upload__list {
  margin-top: 4px;
}
</style>
