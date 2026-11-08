import { approval } from '@/approval'
import { createDemoApi } from '@demo-shared/demo'
export const demoApi = createDemoApi(approval.http)
