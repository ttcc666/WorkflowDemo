import { defineAsyncComponent, type Component } from 'vue'

/**
 * 业务详情面板注册表：审批详情页按 BusinessType 加载对应的业务组件展示单据内容。
 * 组件接收 props：{ businessKey: string }。接入新业务时在这里注册一行即可，未注册的业务显示表单数据。
 */
export const businessPanels: Record<string, Component> = {
  Certificate: defineAsyncComponent(() => import('@/features/certificates/CertificateRequestPanel.vue')),
}
