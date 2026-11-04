/**
 * 基于快照的撤销 / 重做栈。快照是序列化后的流程定义字符串，内容不变的记录会被忽略。
 * 同一 coalesceKey 在 coalesceMs 内的连续记录合并为一步（如在输入框里连续打字）。
 */
export function createHistory(options: { limit?: number; coalesceMs?: number } = {}) {
  const limit = options.limit ?? 100
  const coalesceMs = options.coalesceMs ?? 1000

  let undoStack: string[] = []
  let redoStack: string[] = []
  let present: string | null = null
  let lastKey: string | undefined
  let lastTime = 0

  return {
    get present() {
      return present
    },
    get canUndo() {
      return undoStack.length > 0
    },
    get canRedo() {
      return redoStack.length > 0
    },

    /** 加载新流程时清空历史。 */
    reset(snapshot: string) {
      undoStack = []
      redoStack = []
      present = snapshot
      lastKey = undefined
    },

    /** 记录当前状态；返回是否产生了新的一步。 */
    record(snapshot: string, coalesceKey?: string, now = Date.now()) {
      if (snapshot === present) return false
      const merge = coalesceKey !== undefined && coalesceKey === lastKey && now - lastTime < coalesceMs && undoStack.length > 0
      if (!merge && present !== null) {
        undoStack.push(present)
        if (undoStack.length > limit) undoStack.shift()
      }
      present = snapshot
      redoStack = []
      lastKey = coalesceKey
      lastTime = now
      return !merge
    },

    /** 返回要恢复的快照，没有可撤销的步骤时返回 null。 */
    undo() {
      const previous = undoStack.pop()
      if (previous === undefined) return null
      if (present !== null) redoStack.push(present)
      present = previous
      lastKey = undefined
      return previous
    },

    redo() {
      const next = redoStack.pop()
      if (next === undefined) return null
      if (present !== null) undoStack.push(present)
      present = next
      lastKey = undefined
      return next
    },
  }
}

export type History = ReturnType<typeof createHistory>
