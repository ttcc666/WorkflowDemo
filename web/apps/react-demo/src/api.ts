import { useMemo } from 'react'
import { useApprovalClient } from '@approval/react-ui'
import { createCertificateApi } from '@demo-shared/certificates'
import { createDemoApi } from '@demo-shared/demo'
export function useCertificateApi() { const client = useApprovalClient(); return useMemo(() => createCertificateApi(client.http), [client]) }
export function useDemoApi() { const client = useApprovalClient(); return useMemo(() => createDemoApi(client.http), [client]) }
