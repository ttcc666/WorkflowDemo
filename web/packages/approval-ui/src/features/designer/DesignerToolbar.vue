<script setup lang="ts">
defineProps<{
  flowKey: string
  version: number | null
  dirty: boolean
  validating: boolean
  publishing: boolean
  canPublish: boolean
  canUndo: boolean
  canRedo: boolean
}>()

const name = defineModel<string>('name', { required: true })

const emit = defineEmits<{
  back: []
  undo: []
  redo: []
  layout: []
  validate: []
  publish: []
}>()
</script>

<template>
  <div class="toolbar">
    <el-button text @click="emit('back')">← 返回</el-button>
    <el-input v-model="name" class="toolbar__name" placeholder="流程名称" maxlength="50" />
    <el-tag type="info" effect="plain">{{ flowKey }}</el-tag>
    <el-tag v-if="version" effect="plain">当前 v{{ version }}</el-tag>
    <el-tag v-else type="warning" effect="plain">新流程</el-tag>
    <el-tag v-if="dirty" type="warning">未发布的修改</el-tag>

    <div class="toolbar__actions">
      <el-button-group>
        <el-button :disabled="!canUndo" title="撤销（Ctrl+Z）" @click="emit('undo')">↶ 撤销</el-button>
        <el-button :disabled="!canRedo" title="重做（Ctrl+Y / Ctrl+Shift+Z）" @click="emit('redo')">↷ 重做</el-button>
      </el-button-group>
      <el-button @click="emit('layout')">自动排版</el-button>
      <el-button :loading="validating" @click="emit('validate')">校验</el-button>
      <el-tooltip :disabled="canPublish" content="仅管理员可发布流程" placement="bottom">
        <span>
          <el-button type="primary" :loading="publishing" :disabled="!canPublish" @click="emit('publish')">发布新版本</el-button>
        </span>
      </el-tooltip>
    </div>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}

.toolbar__name {
  width: 220px;
}

.toolbar__actions {
  display: flex;
  gap: 8px;
  margin-left: auto;
}
</style>
