import { type Component } from 'vue'
import { approvalConfig } from '../../config'

/**
 * 业务详情面板：审批详情页按 BusinessType 显示对应组件（props：{ businessKey: string }）。
 * 宿主通过插件选项 `businessPanels` 注册；未注册的业务显示原始表单数据。
 */
export function businessPanelOf(businessType: string): Component | undefined {
  return approvalConfig().businessPanels[businessType]
}
