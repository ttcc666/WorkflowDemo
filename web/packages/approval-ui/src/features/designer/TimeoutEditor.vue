<script setup lang="ts">
import { shallowRef, watch } from 'vue'
import { timeoutActionLabels, type TimeoutAction, type TimeoutPolicy } from '@ui/features/flow/flowModel'

/** 审批节点时限：超时后提醒（可重复催办）、自动同意或自动驳回。 */
const timeout = defineModel<TimeoutPolicy | null>({ required: true })

type Unit = 'minute' | 'hour' | 'day'
const unitMinutes: Record<Unit, number> = { minute: 1, hour: 60, day: 1440 }
const unitLabels: Record<Unit, string> = { minute: '分钟', hour: '小时', day: '天' }

const pickUnit = (minutes: number): Unit => (minutes % 1440 === 0 ? 'day' : minutes % 60 === 0 ? 'hour' : 'minute')

// 单位只是界面状态：按当前值选择最合适的单位，修改数值时换算成分钟保存
const unit = shallowRef<Unit>(pickUnit(timeout.value?.minutes ?? 1440))
const repeatUnit = shallowRef<Unit>(pickUnit(timeout.value?.repeatMinutes ?? 240))
watch(timeout, (value, old) => {
  if (value && !old) unit.value = pickUnit(value.minutes)
})

function toggle(enabled: boolean) {
  timeout.value = enabled ? { minutes: 1440, action: 'Remind', repeatMinutes: null, maxReminders: 3 } : null
}

function patch(change: Partial<TimeoutPolicy>) {
  if (timeout.value) timeout.value = { ...timeout.value, ...change }
}

const amount = (minutes: number, u: Unit) => +(minutes / unitMinutes[u]).toFixed(2)
</script>

<template>
  <div class="timeout">
    <el-switch :model-value="!!timeout" active-text="限时处理" @update:model-value="(v) => toggle(!!v)" />

    <template v-if="timeout">
      <div class="timeout__row">
        <span class="timeout__label">时限</span>
        <el-input-number :model-value="amount(timeout.minutes, unit)" :min="0.1" :step="1" size="small" controls-position="right"
          @update:model-value="(v) => v && patch({ minutes: Math.max(1, Math.round(v * unitMinutes[unit])) })" />
        <el-select v-model="unit" size="small" class="timeout__unit">
          <el-option v-for="(label, u) in unitLabels" :key="u" :label="label" :value="u" />
        </el-select>
      </div>

      <div class="timeout__row">
        <span class="timeout__label">超时后</span>
        <el-radio-group :model-value="timeout.action" size="small" @update:model-value="(v) => patch({ action: v as TimeoutAction })">
          <el-radio-button v-for="(label, a) in timeoutActionLabels" :key="a" :value="a">{{ label }}</el-radio-button>
        </el-radio-group>
      </div>

      <template v-if="timeout.action === 'Remind'">
        <div class="timeout__row">
          <el-checkbox :model-value="!!timeout.repeatMinutes" size="small"
            @update:model-value="(v) => patch({ repeatMinutes: v ? 240 : null })">仍未处理时重复催办</el-checkbox>
        </div>
        <div v-if="timeout.repeatMinutes" class="timeout__row">
          <span class="timeout__label">每隔</span>
          <el-input-number :model-value="amount(timeout.repeatMinutes, repeatUnit)" :min="0.1" size="small" controls-position="right"
            @update:model-value="(v) => v && patch({ repeatMinutes: Math.max(1, Math.round(v * unitMinutes[repeatUnit])) })" />
          <el-select v-model="repeatUnit" size="small" class="timeout__unit">
            <el-option v-for="(label, u) in unitLabels" :key="u" :label="label" :value="u" />
          </el-select>
        </div>
        <div v-if="timeout.repeatMinutes" class="timeout__row">
          <span class="timeout__label">最多提醒</span>
          <el-input-number :model-value="timeout.maxReminders ?? 3" :min="1" :max="20" size="small" controls-position="right"
            @update:model-value="(v) => v && patch({ maxReminders: v })" />
          <span class="timeout__label">次</span>
        </div>
      </template>

      <div class="timeout__help">
        <p v-if="timeout.action !== 'Remind'">自动决定仅在宿主显式启用时生效；默认仍执行超时提醒。</p>
        <template v-if="timeout.action === 'Remind'">
          提醒内容在“通知”中配置（事件选“超时提醒”，接收人可选当前审批人或其上级角色实现催办）；未配置时默认发站内信给当前审批人。
        </template>
        <template v-else>超时后由系统自动{{ timeout.action === 'AutoApprove' ? '同意' : '驳回' }}，并记录在审批轨迹中。</template>
        时限按自然时间计算。
      </div>
    </template>
  </div>
</template>

<style scoped>
.timeout {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.timeout__row {
  display: flex;
  align-items: center;
  gap: 6px;
}

.timeout__label {
  flex: none;
  color: #606266;
  font-size: 12px;
}

.timeout__unit {
  width: 80px;
}

.timeout__help {
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}
</style>
