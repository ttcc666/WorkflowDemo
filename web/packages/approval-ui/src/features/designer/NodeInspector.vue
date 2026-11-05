<script setup lang="ts">
import { computed } from 'vue'
import type { ApprovalMeta } from '@ui/contracts/api'
import {
  modeLabels,
  nodeTypeLabels,
  notifyEventsFor,
  type ApproveMode,
  type FlowNodeData,
} from '@ui/features/flow/flowModel'
import ApproverRulesEditor from './ApproverRulesEditor.vue'
import FieldPermissionsEditor from './FieldPermissionsEditor.vue'
import NotificationRulesEditor from './NotificationRulesEditor.vue'
import TimeoutEditor from './TimeoutEditor.vue'
import { useDesignerForm } from './designerContext'

/** 节点属性面板：只通过 update 事件提交修改，不直接改 props。 */
const props = defineProps<{
  nodeId: string
  data: FlowNodeData
  meta: ApprovalMeta
  roles: string[]
}>()

const emit = defineEmits<{
  update: [patch: Partial<FlowNodeData>]
  remove: []
}>()

const hasApprovers = computed(() => props.data.type === 'Approval' || props.data.type === 'Cc')
const notifyEvents = computed(() => notifyEventsFor(props.data.type))
// 字段权限：审批节点（可编辑 / 隐藏）与抄送节点（隐藏），流程有表单时才显示
const form = useDesignerForm()
const permissionCount = computed(() => Object.keys(props.data.fieldPermissions).length)
const showPermissions = computed(() => hasApprovers.value && (form.value.fields.length > 0 || permissionCount.value > 0))
// 逐级审批等有序审批人只能用于依次审批（发布时也会校验）
const orderedRule = computed(() =>
  props.data.type === 'Approval' && props.data.mode !== 'Sequential'
    ? props.meta.approverTypes.find((t) => t.ordered && props.data.approvers.some((r) => r.type === t.type))
    : undefined,
)
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

          <el-form-item v-if="data.type === 'Approval'" label="审批意见">
            <el-checkbox :model-value="data.requireComment" @update:model-value="(v) => emit('update', { requireComment: !!v })">同意时必须填写意见</el-checkbox>
          </el-form-item>
          <el-form-item v-if="hasApprovers" :label="data.type === 'Cc' ? '抄送人' : '审批人'" required>
            <ApproverRulesEditor :preview-node-id="nodeId" :model-value="data.approvers" :types="meta.approverTypes" :roles="roles"
              @update:model-value="(v) => emit('update', { approvers: v })" />
            <el-alert v-if="orderedRule" type="warning" :closable="false" show-icon class="inspector__alert"
              :title="`「${orderedRule.displayName}」需按顺序逐级审批`">
              <el-button link type="primary" size="small" @click="emit('update', { mode: 'Sequential' })">改为依次审批</el-button>
            </el-alert>
          </el-form-item>

          <el-form-item v-if="data.type === 'Approval'" label="审批人可以">
            <div class="inspector__switches">
              <el-checkbox :model-value="data.allowReturn" @update:model-value="(v) => emit('update', { allowReturn: !!v })">
                退回到之前的节点或发起人
              </el-checkbox>
              <el-checkbox :model-value="data.allowAddSign" @update:model-value="(v) => emit('update', { allowAddSign: !!v })">
                加签（前加签 / 后加签 / 并加签）
              </el-checkbox>
              <el-checkbox :model-value="data.allowTransfer" @update:model-value="(v) => emit('update', { allowTransfer: !!v })">
                转交
              </el-checkbox>
              <el-checkbox :model-value="data.requireQualifiedAssignee" @update:model-value="(v) => emit('update', { requireQualifiedAssignee: !!v })">
                处理及改派时必须符合原岗位资格
              </el-checkbox>
            </div>
          </el-form-item>

          <el-form-item v-if="data.type === 'Approval'" label="找不到审批人时">
            <el-select :model-value="data.emptyApproverPolicy"
              @update:model-value="(v) => emit('update', { emptyApproverPolicy: v as FlowNodeData['emptyApproverPolicy'] })">
              <el-option label="报错，阻止提交" value="Error" />
              <el-option label="自动通过" value="AutoApprove" />
            </el-select>
          </el-form-item>

          <div v-if="data.type === 'Parallel'" class="inspector__help">
            从该节点连出的每条线是一个并行分支，所有分支同时开始。各分支最后必须连到同一个“合并”节点，分支之间不能交叉。
          </div>
          <div v-if="data.type === 'Join'" class="inspector__help">
            等待对应并行节点的所有分支都完成后，再进入下一个节点。
          </div>

          <div v-if="data.type === 'Condition'" class="inspector__help">
            点击从该节点出发的连线，设置分支条件与优先级。条件都不满足时走默认分支。
            条件中可使用表单字段与动作输出变量，如 <code>amount &gt; 5000</code>、<code>risk.body.level == "high"</code>。
          </div>
        </el-form>

      </el-tab-pane>

      <el-tab-pane v-if="notifyEvents.length" :label="`通知${data.notifications.length ? ` (${data.notifications.length})` : ''}`">
        <NotificationRulesEditor :model-value="data.notifications" :events="notifyEvents" :channels="meta.channels"
          :recipient-types="meta.recipientTypes" :variables="meta.templateVariables" :roles="roles"
          @update:model-value="(v) => emit('update', { notifications: v })" />
      </el-tab-pane>

      <el-tab-pane v-if="data.type === 'Approval'" :label="data.timeout ? '时限 ⏱' : '时限'">
        <TimeoutEditor :model-value="data.timeout" @update:model-value="(v) => emit('update', { timeout: v })" />
      </el-tab-pane>

      <el-tab-pane v-if="showPermissions" :label="`字段权限${permissionCount ? ` (${permissionCount})` : ''}`">
        <FieldPermissionsEditor :model-value="data.fieldPermissions" :fields="form.fields" :node-type="data.type as 'Approval' | 'Cc'"
          @update:model-value="(v) => emit('update', { fieldPermissions: v })" />
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

.inspector__switches {
  display: flex;
  flex-direction: column;
}

.inspector__alert {
  margin-top: 6px;
}

.inspector__remove {
  width: 100%;
  margin-top: 12px;
}
</style>
