<script setup lang="ts">
import type { BranchData } from '@/features/flow/flowModel'

const props = defineProps<{
  data: BranchData
  sourceName: string
  targetName: string
}>()

const emit = defineEmits<{
  update: [patch: Partial<BranchData>]
  remove: []
}>()

function toggleDefault(isDefault: boolean) {
  emit('update', isDefault ? { expression: null } : { expression: '' })
}

const examples = ['certType == "Special"', 'amount > 5000 && amount <= 20000', 'operation == "Revoke"']
</script>

<template>
  <div class="inspector">
    <div class="inspector__header">条件分支</div>
    <div class="branch-path">{{ props.sourceName }} → {{ props.targetName }}</div>

    <el-form label-position="top" size="small">
      <el-form-item label="默认分支">
        <el-switch :model-value="data.expression === null" @update:model-value="(v) => toggleDefault(v as boolean)" />
        <div class="inspector__help">其他条件都不满足时走默认分支，每个条件节点有且仅有一个。</div>
      </el-form-item>

      <template v-if="data.expression !== null">
        <el-form-item label="条件表达式" required>
          <el-input :model-value="data.expression" type="textarea" :rows="3" placeholder='如 certType == "Special"'
            @update:model-value="(v: string) => emit('update', { expression: v })" />
          <div class="inspector__help">
            可引用发起时传入的表单字段，支持 ==、!=、&gt;、&lt;、&amp;&amp;、||。示例：
            <code v-for="e in examples" :key="e" class="example" @click="emit('update', { expression: e })">{{ e }}</code>
          </div>
        </el-form-item>
        <el-form-item label="优先级（数字越小越先匹配）">
          <el-input-number :model-value="data.priority" :min="1" :max="98"
            @update:model-value="(v) => emit('update', { priority: Number(v ?? 1) })" />
        </el-form-item>
      </template>
    </el-form>

    <el-button type="danger" plain size="small" class="inspector__remove" @click="emit('remove')">删除分支</el-button>
  </div>
</template>

<style scoped>
.inspector__header {
  margin-bottom: 4px;
  font-weight: 600;
}

.branch-path {
  margin-bottom: 12px;
  color: #909399;
  font-size: 12px;
}

.inspector__help {
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}

.example {
  display: inline-block;
  margin: 2px 4px 0 0;
  padding: 0 4px;
  border-radius: 3px;
  background: #f4f4f5;
  color: #606266;
  cursor: pointer;
}

.inspector__remove {
  width: 100%;
}
</style>
