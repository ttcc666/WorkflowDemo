<script setup lang="ts">
import CertificateFormDialog from '@/features/certificates/CertificateFormDialog.vue'
import CertificateHistoryDrawer from '@/features/certificates/CertificateHistoryDrawer.vue'
import CertificateTable from '@/features/certificates/CertificateTable.vue'
import CertificateDiagnosticsPanel from '@/features/certificates/CertificateDiagnosticsPanel.vue'
import RevokeDialog from '@/features/certificates/RevokeDialog.vue'
import { useCertificateWorkbench } from '@/features/certificates/useCertificateWorkbench'

const { certificates, loading, reload, page, pageSize, total, filters, search,
  selected, source, formVisible, revokeVisible, historyVisible, submitting, serverErrors,
  openCreate, openEdit, openRevoke, openHistory, openRequest, onFormSubmit, onRevokeSubmit } = useCertificateWorkbench()
</script>

<template>
  <el-card shadow="never">
    <template #header>
      <div class="card-header">
        <span>注册证管理</span>
        <div>
          <el-button text type="primary" @click="reload">刷新</el-button>
          <el-button type="primary" @click="openCreate">登记注册证</el-button>
        </div>
      </div>
    </template>

    <div class="filters">
      <el-input v-model="filters.keyword" clearable placeholder="证号、名称或持有人" @keyup.enter="search" />
      <el-button @click="search">查询</el-button>
    </div>
    <CertificateTable
      :certificates="certificates ?? []"
      :loading="loading"
      @edit="openEdit"
      @revoke="openRevoke"
      @history="openHistory"
      @open-pending="openRequest"
    />
    <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total"
      :page-sizes="[20, 50, 100]" layout="total, sizes, prev, pager, next" class="pagination" />
  </el-card>

  <CertificateDiagnosticsPanel />

  <CertificateFormDialog v-model:visible="formVisible" :certificate="selected" :submitting="submitting"
    :initial-data="source?.snapshot" :server-errors="serverErrors" @submit="onFormSubmit" />
  <RevokeDialog v-model:visible="revokeVisible" :certificate="selected" :submitting="submitting"
    :initial-reason="source?.reason ?? undefined" :error="serverErrors?.reason?.join('；')" @submit="onRevokeSubmit" />
  <CertificateHistoryDrawer v-if="selected && historyVisible" v-model:visible="historyVisible" :certificate="selected"
    @open-request="openRequest" />
</template>

<style scoped>
.filters { display: flex; gap: 8px; max-width: 460px; margin-bottom: 16px; }
.pagination { margin-top: 16px; justify-content: flex-end; }
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
