<script setup lang="ts">
import { shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { errorMessage } from '@/api/http'
import type { Certificate, CertificateData } from '@/api/types'
import CertificateFormDialog from '@/features/certificates/CertificateFormDialog.vue'
import CertificateHistoryDrawer from '@/features/certificates/CertificateHistoryDrawer.vue'
import CertificateTable from '@/features/certificates/CertificateTable.vue'
import RevokeDialog from '@/features/certificates/RevokeDialog.vue'
import { findApprovalInstanceId, useCertificates } from '@/features/certificates/useCertificates'

const router = useRouter()
const { certificates, loading, reload, submitCreate, submitUpdate, submitRevoke } = useCertificates()

const selected = shallowRef<Certificate | null>(null)
const formVisible = shallowRef(false)
const revokeVisible = shallowRef(false)
const historyVisible = shallowRef(false)
const submitting = shallowRef(false)

function openCreate() {
  selected.value = null
  formVisible.value = true
}

function openEdit(certificate: Certificate) {
  selected.value = certificate
  formVisible.value = true
}

function openRevoke(certificate: Certificate) {
  selected.value = certificate
  revokeVisible.value = true
}

function openHistory(certificate: Certificate) {
  selected.value = certificate
  historyVisible.value = true
}

async function openRequest(requestId: string) {
  try {
    const instanceId = await findApprovalInstanceId(requestId)
    if (instanceId) await router.push({ name: 'approval-detail', params: { id: instanceId } })
    else ElMessage.warning('未找到对应的审批')
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
}

async function submit(action: () => Promise<string>, close: () => void) {
  submitting.value = true
  try {
    const requestId = await action()
    close()
    ElMessage.success('已提交审核')
    await openRequest(requestId)
  } catch (e) {
    ElMessage.error(errorMessage(e))
  } finally {
    submitting.value = false
  }
}

function onFormSubmit(data: CertificateData) {
  const target = selected.value
  void submit(() => (target ? submitUpdate(target.id, data) : submitCreate(data)), () => (formVisible.value = false))
}

function onRevokeSubmit(reason: string) {
  const target = selected.value!
  void submit(() => submitRevoke(target.id, reason), () => (revokeVisible.value = false))
}
</script>

<template>
  <el-card shadow="never">
    <template #header>
      <div class="card-header">
        <span>证书管理</span>
        <div>
          <el-button text type="primary" @click="reload">刷新</el-button>
          <el-button type="primary" @click="openCreate">申请新增</el-button>
        </div>
      </div>
    </template>

    <CertificateTable
      :certificates="certificates ?? []"
      :loading="loading"
      @edit="openEdit"
      @revoke="openRevoke"
      @history="openHistory"
      @open-pending="openRequest"
    />
  </el-card>

  <CertificateFormDialog v-model:visible="formVisible" :certificate="selected" :submitting="submitting" @submit="onFormSubmit" />
  <RevokeDialog v-model:visible="revokeVisible" :certificate="selected" :submitting="submitting" @submit="onRevokeSubmit" />
  <CertificateHistoryDrawer v-if="selected && historyVisible" v-model:visible="historyVisible" :certificate="selected"
    @open-request="openRequest" />
</template>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
