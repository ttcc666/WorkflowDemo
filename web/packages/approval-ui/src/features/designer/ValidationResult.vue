<script setup lang="ts">
/** 校验结果：errors 为 null 表示尚未校验（或校验后又有修改）；warnings 为不阻止发布的提示。 */
defineProps<{ errors: string[] | null; warnings?: string[] }>()
</script>

<template>
  <el-alert v-if="errors && errors.length === 0" type="success" title="校验通过，可以发布" show-icon :closable="false" />
  <el-alert v-else-if="errors" type="error" :title="`发现 ${errors.length} 个问题`" show-icon :closable="false">
    <ul class="error-list">
      <li v-for="(error, index) in errors" :key="index">{{ error }}</li>
    </ul>
  </el-alert>
  <el-alert v-if="errors && warnings?.length" type="warning" :title="`${warnings.length} 个提示（不影响发布）`" show-icon :closable="false">
    <ul class="error-list">
      <li v-for="(warning, index) in warnings" :key="index">{{ warning }}</li>
    </ul>
  </el-alert>
</template>

<style scoped>
.error-list {
  margin: 4px 0 0;
  padding-left: 18px;
}
</style>
