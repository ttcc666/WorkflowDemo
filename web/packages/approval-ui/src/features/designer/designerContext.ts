import { computed, inject, type ComputedRef, type InjectionKey } from 'vue'
import type { FormSchema } from '@ui/contracts/forms'

/** 设计器中正在编辑的表单结构：审批人（人员字段）、条件分支、消息模板据此提示可用字段。 */
export const designerFormKey: InjectionKey<ComputedRef<FormSchema>> = Symbol('designerForm')

export function useDesignerForm() {
  return inject(designerFormKey, computed(() => ({ fields: [] })))
}

/** 设计器拥有预演输入与快照，规则编辑器只发出预览意图。 */
export const simulationPreviewKey: InjectionKey<(nodeId?: string, ruleIndex?: number) => void> = Symbol('simulationPreview')
