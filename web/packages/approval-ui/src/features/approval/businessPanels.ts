import { type Component } from 'vue'

/** 按业务类型选择详情面板；配置由调用方所属的客户端显式传入。 */
export function businessPanelOf(businessType: string, panels: Record<string, Component>): Component | undefined {
  return panels[businessType]
}
