<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import { approvalRouteNames } from '../routes'
import { useApprovalClient } from '../client'
import type { DefinitionGovernanceSummary, SynchronousActionScan } from '../contracts/api'
import { ElMessage } from 'element-plus'
import { errorMessage } from '../api/http'
import { useApprovalUser } from '../composables/useApprovalUser'
import { useAsyncData } from '../composables/useAsyncData'
import DefinitionTable from '../features/definitions/DefinitionTable.vue'
import DefinitionVersionsDrawer from '../features/definitions/DefinitionVersionsDrawer.vue'
import NewDefinitionDialog from '../features/definitions/NewDefinitionDialog.vue'

const { api: approvalApi, userKey } = useApprovalClient()

const router = useRouter()
// 完整流程定义含动作参数、通知配置，仅管理员（及后端配置的 DefinitionReaders 角色）可查看
const { me, canViewDefinitions, can } = useApprovalUser()
const { data: definitions, loading, reload } = useAsyncData(
  async (signal): Promise<DefinitionGovernanceSummary[]> => (canViewDefinitions.value ? approvalApi.governanceDefinitions(signal) : []),
  { initial: [], watch: [canViewDefinitions] },
)

const existingKeys = computed(() => (definitions.value ?? []).map((d) => d.key))
const newVisible = shallowRef(false)
const versionsOf = shallowRef<DefinitionGovernanceSummary | null>(null)
const scan = shallowRef<SynchronousActionScan | null>(null)
async function scanActions(page = 1) {
  try { scan.value = await approvalApi.scanSynchronousActions(page) }
  catch (error) { ElMessage.error(errorMessage(error)) }
}
const versionsVisible = shallowRef(false)
watch(userKey, () => { newVisible.value = false; versionsOf.value = null; versionsVisible.value = false; scan.value = null }, { flush: 'sync' })

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
          <el-button text type="primary" @click="reload">刷新</el-button>
          <el-button v-if="can('approval.diagnostics.read')" @click="scanActions()">扫描旧同步动作</el-button>
          <el-button v-if="can('approval.definition.design')" type="primary" @click="newVisible = true">新建流程</el-button>
        </div>
      </div>
    </template>
    <DefinitionTable :definitions="definitions ?? []" :loading="loading" :can-design="can('approval.definition.design')" @design="design" @versions="showVersions" />
    <section v-if="scan">
      <p>本页扫描 {{ scan.definitionsScanned }} 个发布版本，共 {{ scan.totalDefinitions }} 个；扫描只读，不改变旧流程。</p>
      <el-table :data="scan.findings" size="small">
        <el-table-column prop="key" label="流程" /><el-table-column prop="version" label="版本" />
        <el-table-column prop="nodeId" label="节点" /><el-table-column prop="actionType" label="动作" />
        <el-table-column prop="reason" label="需处置原因" /><el-table-column prop="runningInstances" label="在途数量" />
      </el-table>
      <el-pagination :current-page="scan.page" :page-size="scan.pageSize" :total="scan.totalDefinitions" layout="prev, pager, next" @current-change="scanActions" />
    </section>
  </el-card>

  <NewDefinitionDialog v-model:visible="newVisible" :existing-keys="existingKeys" @create="(key, name) => design(key, { name })" />
  <DefinitionVersionsDrawer v-if="versionsOf && versionsVisible" v-model:visible="versionsVisible" :flow-key="versionsOf.key"
    :can-design="can('approval.definition.design')"
    @changed="reload"
    @edit-from="(id) => design(versionsOf!.key, { from: id })" />
</template>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
