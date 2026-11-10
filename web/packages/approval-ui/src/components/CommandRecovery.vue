<script setup lang="ts">
import { computed, onScopeDispose } from 'vue'
import { useApprovalClient } from '@ui/client'
import type { CommandIntent } from '@ui/composables/useCommandIntent'
import CommandRecoveryItem from './CommandRecoveryItem.vue'

const props = defineProps<{ intent: CommandIntent; busy?: boolean; scope?: string | null; description?: string }>()
const emit = defineEmits<{ recovered: [result: unknown, scope: string]; reset: [scope: string] }>()
const { userKey } = useApprovalClient()
let active = true
onScopeDispose(() => { active = false })
function recovered(value: unknown, scope: string) { if (active) emit('recovered', value, scope) }
const scopes = computed(() => props.scope === undefined ? props.intent.pendingScopes() : props.scope === null ? [] : [props.scope])
</script>
<template>
  <CommandRecoveryItem v-for="current in scopes" :key="`${userKey}:${current}`"
    :intent="intent" :scope="current" :busy="busy" :description="description"
    :recovered="recovered" :reset="scope => emit('reset', scope)" />
</template>
