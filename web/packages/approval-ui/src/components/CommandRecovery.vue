<script setup lang="ts">
import { computed } from 'vue'
import { useApprovalClient } from '@ui/client'
import type { CommandIntent } from '@ui/composables/useCommandIntent'
import CommandRecoveryItem from './CommandRecoveryItem.vue'

const props = defineProps<{ intent: CommandIntent; busy?: boolean; scope?: string | null; description?: string }>()
const emit = defineEmits<{ recovered: [result: unknown, scope: string]; reset: [scope: string] }>()
const { userKey } = useApprovalClient()
const scopes = computed(() => props.scope === undefined ? props.intent.pendingScopes() : props.scope === null ? [] : [props.scope])
</script>
<template>
  <CommandRecoveryItem v-for="current in scopes" :key="`${userKey}:${current}`"
    :intent="intent" :scope="current" :busy="busy" :description="description"
    :recovered="(value, scope) => emit('recovered', value, scope)" :reset="scope => emit('reset', scope)" />
</template>
