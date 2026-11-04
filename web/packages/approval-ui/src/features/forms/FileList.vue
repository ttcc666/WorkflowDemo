<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { useApprovalClient } from '../../client'
import { errorMessage } from '../../api/http'
import type { FileValue } from '../../contracts/forms'

const { api: approvalApi } = useApprovalClient()

/** 附件列表：点击下载（带鉴权请求头，服务端校验下载权限）；removable 时可移除。 */
defineProps<{ files: FileValue[]; removable?: boolean }>()
const emit = defineEmits<{ remove: [file: FileValue] }>()

function sizeText(size: number) {
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(1)} MB`
}

async function download(file: FileValue) {
  try {
    await approvalApi.downloadFile(file.id, file.name)
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
}
</script>

<template>
  <span v-if="files.length === 0" class="file-list__empty">-</span>
  <ul v-else class="file-list">
    <li v-for="file in files" :key="file.id" class="file-list__item">
      <el-link type="primary" :underline="false" @click="download(file)">📎 {{ file.name }}</el-link>
      <span class="file-list__size">{{ sizeText(file.size) }}</span>
      <el-button v-if="removable" link type="danger" size="small" @click="emit('remove', file)">移除</el-button>
    </li>
  </ul>
</template>

<style scoped>
.file-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.file-list__item {
  display: flex;
  align-items: center;
  gap: 8px;
  line-height: 1.8;
}

.file-list__size {
  color: #909399;
  font-size: 12px;
}

.file-list__empty {
  color: #303133;
}
</style>
