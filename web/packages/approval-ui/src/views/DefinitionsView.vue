<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { approvalRouteNames } from '../routes'
import { approvalApi } from '../api/approval'
import type { ApprovalDefinition } from '../api/types'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import DefinitionTable from '../features/definitions/DefinitionTable.vue'
import DefinitionVersionsDrawer from '../features/definitions/DefinitionVersionsDrawer.vue'
import NewDefinitionDialog from '../features/definitions/NewDefinitionDialog.vue'

const router = useRouter()
// 完整流程定义含动作参数、通知配置，仅管理员（及后端配置的 DefinitionReaders 角色）可查看
const { me, canViewDefinitions, isAdmin } = useApprovalUser()
const { data: definitions, loading, reload } = useAsyncData(
  async (signal): Promise<ApprovalDefinition[]> => (canViewDefinitions.value ? approvalApi.definitions(signal) : []),
  { initial: [], watch: [canViewDefinitions] },
)

const existingKeys = computed(() => (definitions.value ?? []).map((d) => d.key))
const newVisible = shallowRef(false)
const versionsOf = shallowRef<ApprovalDefinition | null>(null)
const versionsVisible = shallowRef(false)

function design(key: string, query: Record<string, string> = {}) {
  void router.push({ name: approvalRouteNames.designer, params: { key }, query })
}

function showVersions(definition: ApprovalDefinition) {
  versionsOf.value = definition
  versionsVisible.value = true
}
</script>

<template>
  <el-alert v-if="me && !canViewDefinitions" type="warning" :closable="false" show-icon title="流程定义仅管理员可查看" />
  <el-card v-else shadow="never">
    <template #header>
      <div class="card-header">
        <span>流程定义</span>
        <div>
          <el-button text type="primary" @click="reload">刷新</el-button>
          <el-button v-if="isAdmin" type="primary" @click="newVisible = true">新建流程</el-button>
        </div>
      </div>
    </template>
    <DefinitionTable :definitions="definitions ?? []" :loading="loading" @design="design" @versions="showVersions" />
  </el-card>

  <NewDefinitionDialog v-model:visible="newVisible" :existing-keys="existingKeys" @create="(key, name) => design(key, { name })" />
  <DefinitionVersionsDrawer v-if="versionsOf && versionsVisible" v-model:visible="versionsVisible" :flow-key="versionsOf.key"
    @edit-from="(id) => design(versionsOf!.key, { from: id })" />
</template>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
