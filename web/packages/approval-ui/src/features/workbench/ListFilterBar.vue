<script setup lang="ts">
import { computed } from 'vue'
import type { FlowSummary } from '../../contracts/api'
import type { ListFilters } from '../../composables/usePagedList'
import { instanceStatusLabels } from '../../utils/labels'

/** 列表筛选栏：关键字（回车查询）、流程、状态、发起日期、只看已超时；下拉与日期修改后立即查询。 */
const props = withDefaults(
  defineProps<{
    flows: FlowSummary[]
    /** 显示状态筛选（流程列表） */
    status?: boolean
    /** 显示发起日期区间 */
    dates?: boolean
    /** 显示“只看已超时”（待办） */
    overdue?: boolean
    keywordPlaceholder?: string
  }>(),
  { status: false, dates: false, overdue: false, keywordPlaceholder: '标题 / 申请人' },
)

const filters = defineModel<ListFilters>({ required: true })
const emit = defineEmits<{ search: []; reset: [] }>()

const range = computed({
  get: () => (filters.value.from && filters.value.to ? [filters.value.from, filters.value.to] : null),
  set: (value: string[] | null) => {
    filters.value.from = value?.[0] ?? ''
    filters.value.to = value?.[1] ?? ''
  },
})

const statusOptions = computed(() => (props.status ? Object.entries(instanceStatusLabels) : []))
</script>

<template>
  <div class="list-filters">
    <el-input v-model="filters.keyword" :placeholder="keywordPlaceholder" clearable class="list-filters__keyword"
      @keyup.enter="emit('search')" @clear="emit('search')" />
    <el-select v-model="filters.definitionKey" placeholder="全部流程" clearable filterable class="list-filters__select"
      @change="emit('search')">
      <el-option v-for="f in flows" :key="f.key" :label="f.name" :value="f.key" />
    </el-select>
    <el-select v-if="status" v-model="filters.status" placeholder="全部状态" clearable class="list-filters__status" @change="emit('search')">
      <el-option v-for="[value, label] in statusOptions" :key="value" :label="label.text" :value="value" />
    </el-select>
    <el-date-picker v-if="dates" v-model="range" type="daterange" value-format="YYYY-MM-DD" start-placeholder="发起日期"
      end-placeholder="结束日期" class="list-filters__dates" @change="emit('search')" />
    <el-checkbox v-if="overdue" v-model="filters.overdueOnly" @change="emit('search')">只看已超时</el-checkbox>
    <el-input v-model="filters.businessKey" placeholder="业务编号" clearable class="list-filters__select" @keyup.enter="emit('search')" />
    <el-input v-model="filters.businessType" placeholder="业务类型" clearable class="list-filters__select" @keyup.enter="emit('search')" />
    <el-input v-model="filters.initiatorId" placeholder="发起人 ID" clearable class="list-filters__select" @keyup.enter="emit('search')" />
    <el-input v-model="filters.approverId" placeholder="审批人 ID" clearable class="list-filters__select" @keyup.enter="emit('search')" />
    <el-select v-model="filters.sort" placeholder="默认排序" clearable class="list-filters__select" @change="emit('search')">
      <el-option label="最新在前" value="Newest" /><el-option label="最早在前" value="Oldest" /><el-option label="标题" value="Title" />
    </el-select>
    <el-button type="primary" @click="emit('search')">查询</el-button>
    <el-button @click="emit('reset')">重置</el-button>
  </div>
</template>

<style scoped>
.list-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.list-filters__keyword {
  width: 200px;
}

.list-filters__select {
  width: 160px;
}

.list-filters__status {
  width: 120px;
}

.list-filters__dates {
  max-width: 260px;
}

.list-filters .el-button + .el-button {
  margin-left: 0;
}
</style>
