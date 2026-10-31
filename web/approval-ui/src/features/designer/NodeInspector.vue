<script setup lang="ts">
import { computed } from 'vue'
import type { ApprovalMeta, DemoUser } from '@/api/types'
import {
  modeLabels,
  nodeTypeLabels,
  notifyEventsFor,
  type ApproveMode,
  type FlowNodeData,
} from '@/features/flow/flowModel'
import ActionStepsEditor from './ActionStepsEditor.vue'
import ApproverRulesEditor from './ApproverRulesEditor.vue'
import NotificationRulesEditor from './NotificationRulesEditor.vue'
import TimeoutEditor from './TimeoutEditor.vue'

/** 节点属性面板：只通过 update 事件提交修改，不直接改 props。 */
const props = defineProps<{
  nodeId: string
  data: FlowNodeData
  meta: ApprovalMeta
  roles: string[]
  users: DemoUser[]
}>()

const emit = defineEmits<{
  update: [patch: Partial<FlowNodeData>]
  remove: []
}>()

const hasApprovers = computed(() => props.data.type === 'Approval' || props.data.type === 'Cc')
const notifyEvents = computed(() => notifyEventsFor(props.data.type))
const modeHelp: Record<ApproveMode, string> = {
  Any: '任意一人同意即通过；角色由角色内任意成员处理',
  All: '所有人都同意才通过；角色会展开为全部成员',
  Sequential: '按顺序逐个审批；角色会展开为全部成员',
}
</script>

<template>
  <div class="inspector">
    <div class="inspector__header">
      <span>{{ nodeTypeLabels[data.type] }}节点</span>
      <span class="inspector__id">{{ nodeId }}</span>
    </div>

    <el-tabs class="inspector__tabs">
      <el-tab-pane label="基本">
        <el-form label-position="top" size="small">
          <el-form-item label="节点名称" required>
            <el-input :model-value="data.name" maxlength="50" @update:model-value="(v: string) => emit('update', { name: v })" />
          </el-form-item>

          <el-form-item v-if="data.type === 'Approval'" label="审批方式">
            <el-radio-group :model-value="data.mode" @update:model-value="(v) => emit('update', { mode: v as ApproveMode })">
              <el-radio-button v-for="(label, mode) in modeLabels" :key="mode" :value="mode">{{ label }}</el-radio-button>
            </el-radio-group>
            <div class="inspector__help">{{ modeHelp[data.mode] }}</div>
          </el-form-item>

          <el-form-item v-if="hasApprovers" :label="data.type === 'Cc' ? '抄送人' : '审批人'" required>
            <ApproverRulesEditor :model-value="data.approvers" :types="meta.approverTypes" :roles="roles" :users="users"
              @update:model-value="(v) => emit('update', { approvers: v })" />
          </el-form-item>

          <el-form-item v-if="data.type === 'Approval'" label="找不到审批人时">
            <el-select :model-value="data.emptyApproverPolicy"
              @update:model-value="(v) => emit('update', { emptyApproverPolicy: v as FlowNodeData['emptyApproverPolicy'] })">
              <el-option label="报错，阻止提交" value="Error" />
              <el-option label="自动通过" value="AutoApprove" />
            </el-select>
          </el-form-item>

          <div v-if="data.type === 'Condition'" class="inspector__help">
            点击从该节点出发的连线，设置分支条件与优先级。条件都不满足时走默认分支。
            条件中可使用表单字段与动作输出变量，如 <code>amount &gt; 5000</code>、<code>risk.body.level == "high"</code>。
          </div>
        </el-form>

        <ActionStepsEditor v-if="data.type === 'Action'" :model-value="data.actions" :actions="meta.actions"
          @update:model-value="(v) => emit('update', { actions: v })" />
      </el-tab-pane>

      <el-tab-pane v-if="notifyEvents.length" :label="`通知${data.notifications.length ? ` (${data.notifications.length})` : ''}`">
        <NotificationRulesEditor :model-value="data.notifications" :events="notifyEvents" :channels="meta.channels"
          :recipient-types="meta.recipientTypes" :variables="meta.templateVariables" :roles="roles" :users="users"
          @update:model-value="(v) => emit('update', { notifications: v })" />
      </el-tab-pane>

      <el-tab-pane v-if="data.type === 'Approval'" :label="data.timeout ? '时限 ⏱' : '时限'">
        <TimeoutEditor :model-value="data.timeout" @update:model-value="(v) => emit('update', { timeout: v })" />
      </el-tab-pane>
    </el-tabs>

    <el-button v-if="data.type !== 'Start'" type="danger" plain size="small" class="inspector__remove" @click="emit('remove')">
      删除节点
    </el-button>
  </div>
</template>

<style scoped>
.inspector__header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 4px;
  font-weight: 600;
}

.inspector__id {
  color: #c0c4cc;
  font-family: monospace;
  font-size: 12px;
  font-weight: normal;
}

.inspector__help {
  margin-top: 4px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}

.inspector__remove {
  width: 100%;
  margin-top: 12px;
}
</style>
