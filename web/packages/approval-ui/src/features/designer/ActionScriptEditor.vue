<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, shallowRef, useTemplateRef, watch } from 'vue'
import { basicSetup, EditorView } from 'codemirror'
import { javascript, javascriptLanguage } from '@codemirror/lang-javascript'

const props = defineProps<{ script: string; outputs: string[] }>()
const emit = defineEmits<{ update: [script: string] }>()
const host = useTemplateRef<HTMLDivElement>('host')
const source = shallowRef(props.script)
const variables = computed(() => ['context.form', 'context.outputs', 'context.business', 'context.instance',
  'context.initiator', ...props.outputs.map(key => `context.outputs.${key}`)])
const syntaxError = computed(() => {
  if (source.value.length > 16000) return '脚本最多 16000 个字符'
  let position: number | undefined
  javascriptLanguage.parser.parse(source.value).iterate({
    enter(node) { if (node.type.isError && position === undefined) position = node.from },
  })
  return position === undefined ? '' : `第 ${source.value.slice(0, position).split('\n').length} 行附近有语法错误`
})
let editor: EditorView | undefined

function commit() {
  if (source.value !== props.script) emit('update', source.value)
}

onMounted(() => {
  editor = new EditorView({
    parent: host.value!,
    doc: props.script,
    extensions: [
      basicSetup,
      javascript(),
      EditorView.contentAttributes.of({ 'aria-label': 'JavaScript', spellcheck: 'false' }),
      EditorView.updateListener.of(update => { if (update.docChanged) source.value = update.state.doc.toString() }),
      EditorView.domEventHandlers({ blur: commit }),
    ],
  })
})
onBeforeUnmount(() => editor?.destroy())
watch(() => props.script, value => {
  if (!editor || value === source.value) return
  editor.dispatch({ changes: { from: 0, to: editor.state.doc.length, insert: value } })
})

function insert(variable: string) {
  if (!editor) return
  editor.dispatch(editor.state.replaceSelection(variable))
  editor.focus()
  commit()
}
</script>

<template>
  <div class="script-editor">
    <div class="script-editor__toolbar">
      <strong>JavaScript</strong>
      <el-dropdown trigger="click" @command="insert">
        <el-button size="small">插入上下文</el-button>
        <template #dropdown><el-dropdown-menu>
          <el-dropdown-item v-for="variable in variables" :key="variable" :command="variable">{{ variable }}</el-dropdown-item>
        </el-dropdown-menu></template>
      </el-dropdown>
    </div>
    <div ref="host" class="script-editor__body" />
    <small v-if="syntaxError" role="status" class="script-editor__error">{{ syntaxError }}</small>
    <small><code>execute(context)</code> 返回 JSON 对象。可读取 <code>context.form</code>、<code>context.outputs</code>、
      <code>context.business</code>、<code>context.instance</code> 和 <code>context.initiator</code>。</small>
    <small v-if="outputs.length">当前节点的上游输出 {{ outputs.join('、') }}</small>
  </div>
</template>

<style scoped>
.script-editor { display: grid; gap: 6px; min-width: 0; }
.script-editor__toolbar { display: flex; align-items: center; justify-content: space-between; }
.script-editor__body { border: 1px solid var(--el-border-color); border-radius: 4px; min-width: 0; }
.script-editor__body :deep(.cm-editor) { min-height: 120px; max-height: 260px; font-size: 12px; }
.script-editor__error { color: var(--el-color-danger); }
</style>
