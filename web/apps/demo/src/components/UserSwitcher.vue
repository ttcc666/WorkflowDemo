<script setup lang="ts">
import { useDemoUsers } from '@/composables/useDemoUsers'

const { userId, users, roles, switchUser } = useDemoUsers()
</script>

<template>
  <div class="user-switcher">
    <span class="user-switcher__label">演示用户</span>
    <el-select :model-value="userId" size="small" class="user-switcher__select" @update:model-value="switchUser">
      <el-option v-for="u in users" :key="u.id" :label="u.id" :value="u.id">
        <span>{{ u.name }}</span>
        <span class="user-switcher__roles">{{ u.roles.join('、') || '普通用户' }}</span>
      </el-option>
    </el-select>
    <el-tag v-for="role in roles" :key="role" size="small" effect="plain">{{ role }}</el-tag>
  </div>
</template>

<style scoped>
.user-switcher {
  display: flex;
  align-items: center;
  gap: 8px;
}

.user-switcher__label {
  color: #909399;
  font-size: 13px;
}

.user-switcher__select {
  width: 160px;
}

.user-switcher__roles {
  float: right;
  margin-left: 16px;
  color: #909399;
  font-size: 12px;
}
</style>
