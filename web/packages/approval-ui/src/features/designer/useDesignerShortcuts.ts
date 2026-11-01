import { onBeforeUnmount } from 'vue'

interface DesignerHistoryActions {
  undo: () => void
  redo: () => void
}

/** 为设计器注册快捷键；输入控件保留原生撤销，组件卸载时清理监听。 */
export function useDesignerShortcuts(actions: DesignerHistoryActions) {
  function onKeydown(e: KeyboardEvent) {
    if (!(e.ctrlKey || e.metaKey)) return
    const target = e.target as HTMLElement | null
    if (target?.closest('input, textarea, [contenteditable="true"]')) return
    const key = e.key.toLowerCase()
    if (key === 'z' && !e.shiftKey) {
      e.preventDefault()
      actions.undo()
    } else if (key === 'y' || (key === 'z' && e.shiftKey)) {
      e.preventDefault()
      actions.redo()
    }
  }
  window.addEventListener('keydown', onKeydown)
  onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
}
