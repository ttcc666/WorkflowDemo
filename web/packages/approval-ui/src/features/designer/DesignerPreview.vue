<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { FlowDefinition } from '../../contracts/flow'
import { initialValues, type FormValues } from '../forms/formModel'
import DynamicForm from '../forms/DynamicForm.vue'
import FlowViewer from '../flow/FlowViewer.vue'

const props = defineProps<{ definition: FlowDefinition }>()
const visible = defineModel<boolean>({ required: true })
const values = ref<FormValues>({})
watch(() => props.definition, value => { values.value = initialValues(value.form ?? { fields: [] }) }, { immediate: true })
const json = computed(() => JSON.stringify(props.definition))
</script>

<template>
  <el-dialog v-model="visible" title="草稿预览" width="900px" destroy-on-close>
    <el-alert type="info" :closable="false" title="预览未发布设计，不创建流程实例；业务表单仍由宿主决定。" />
    <FlowViewer :definition-json="json" />
    <DynamicForm v-if="definition.form" v-model="values" :schema="definition.form" />
  </el-dialog>
</template>
