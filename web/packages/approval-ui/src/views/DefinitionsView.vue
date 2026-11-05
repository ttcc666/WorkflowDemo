<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import { approvalRouteNames } from '@ui/routes'
import { useApprovalClient } from '@ui/client'
import type { DefinitionGovernanceSummary } from '@ui/contracts/api'
import { useApprovalUser } from '@ui/composables/useApprovalUser'
import { useAsyncData } from '@ui/composables/useAsyncData'
import DefinitionTable from '@ui/features/definitions/DefinitionTable.vue'
import DefinitionVersionsDrawer from '@ui/features/definitions/DefinitionVersionsDrawer.vue'
import NewDefinitionDialog from '@ui/features/definitions/NewDefinitionDialog.vue'
import DefinitionCategoryFilter from '@ui/features/definitions/DefinitionCategoryFilter.vue'

const { api: approvalApi, userKey } = useApprovalClient()

const router = useRouter()
// 完整流程定义含通知配置，按宿主返回的敏感读取能力展示
const { me, canViewDefinitions, can } = useApprovalUser()
const categorySelection = shallowRef('')
const { data: definitions, loading, reload } = useAsyncData(
  async (signal): Promise<DefinitionGovernanceSummary[]> => (canViewDefinitions.value ? approvalApi.governanceDefinitions(signal,
    categorySelection.value.startsWith('category:') ? categorySelection.value.slice(9) : undefined, categorySelection.value === 'uncategorized') : []),
  { initial: [], resetOnReload: true, watch: [canViewDefinitions, categorySelection, userKey] },
)
const categories = useAsyncData(signal => canViewDefinitions.value ? approvalApi.definitionCategories(signal) : Promise.resolve([]), {
  initial: [], watch: [canViewDefinitions, userKey],
})
async function refresh() { await Promise.all([reload(), categories.reload()]) }

const existingKeys = computed(() => (definitions.value ?? []).map((d) => d.key))
const newVisible = shallowRef(false)
const versionsOf = shallowRef<DefinitionGovernanceSummary | null>(null)
const versionsVisible = shallowRef(false)
watch(userKey, () => { categorySelection.value = ''; definitions.value = []; categories.data.value = []; newVisible.value = false; versionsOf.value = null; versionsVisible.value = false }, { flush: 'sync' })

function design(key: string, query: Record<string, string> = {}) {
  void router.push({ name: approvalRouteNames.designer, params: { key }, query })
}

function showVersions(definition: DefinitionGovernanceSummary) {
  versionsOf.value = definition
  versionsVisible.value = true
}
</script>

<template>
  <el-alert v-if="me && !canViewDefinitions" type="warning" :closable="false" show-icon title="没有流程定义读取权限" />
  <el-card v-else shadow="never">
    <template #header>
      <div class="card-header">
        <span>流程定义</span>
        <div>
          <el-button text type="primary" @click="refresh">刷新</el-button>
          <el-button v-if="can('approval.definition.design')" type="primary" @click="newVisible = true">新建流程</el-button>
        </div>
      </div>
    </template>
    <DefinitionCategoryFilter v-model="categorySelection" :categories="categories.data.value ?? []" />
    <DefinitionTable :definitions="definitions ?? []" :loading="loading" :can-design="can('approval.definition.design')" @design="design" @versions="showVersions" />
  </el-card>

  <NewDefinitionDialog v-model:visible="newVisible" :existing-keys="existingKeys" @create="(key, name) => design(key, { name })" />
  <DefinitionVersionsDrawer v-if="versionsOf && versionsVisible" v-model:visible="versionsVisible" :flow-key="versionsOf.key"
    :can-design="can('approval.definition.design')"
    @changed="refresh"
    @edit-from="(id) => design(versionsOf!.key, { from: id })" />
</template>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
