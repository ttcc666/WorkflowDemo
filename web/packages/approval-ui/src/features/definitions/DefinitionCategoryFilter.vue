<script setup lang="ts">
defineProps<{ categories: string[] }>()
const selected = defineModel<string>({ required: true })
</script>

<template>
  <div class="category-filter">
    <el-select v-model="selected" aria-label="流程分类" placeholder="全部分类" clearable>
      <el-option label="全部分类" value="" />
      <el-option label="未分类" value="uncategorized" />
      <el-option v-for="category in categories" :key="category" :label="category" :value="`category:${category}`" />
    </el-select>
    <span>有设计草稿时显示草稿分类，否则显示选用发布版本的分类。</span>
  </div>
</template>

<style scoped>
.category-filter { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
.category-filter .el-select { width: 220px; }
.category-filter span { font-size: 12px; color: #909399; }
</style>
