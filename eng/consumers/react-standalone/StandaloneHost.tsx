import { useLayoutEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { createHashRouter, Link, Navigate, Outlet, RouterProvider } from 'react-router'
import { ApprovalProvider, createApprovalClient, type UserPickerProps } from '@approval/react-ui'
import WorkbenchPage from '@approval/react-ui/pages/WorkbenchPage'
import ApprovalDetailPage from '@approval/react-ui/pages/ApprovalDetailPage'
import StartFormPage from '@approval/react-ui/pages/StartFormPage'
import '@approval/react-ui/style.css'

function HostPicker({ value, onChange, disabled, label }: UserPickerProps) {
  return <label>{label}<select disabled={disabled} value={value[0] ?? ''} onChange={event => onChange(event.target.value ? [event.target.value] : [])}>
    <option value="">请选择</option><option value="alice">Alice</option><option value="bob">Bob</option>
  </select></label>
}
function OrderPanel({ businessKey }: { businessKey: string }) { return <p data-testid="host-business">宿主业务：{businessKey}</p> }
function Layout() { return <><nav><Link to="/other">宿主其他页面</Link> · <Link to="/embedded/inbox">审批入口</Link></nav><Outlet /></> }
const router = createHashRouter([
  { Component: Layout, children: [
    { path: '/other', element: <p>宿主自己的页面</p> },
    { path: '/embedded', children: [
      { path: 'inbox', Component: WorkbenchPage },
      { path: 'workbench', element: <Navigate to="../inbox" replace /> },
      { path: 'approvals/:id', Component: ApprovalDetailPage },
      { path: 'start/:key', Component: StartFormPage },
    ] },
  ] },
])
const client = createApprovalClient({ apiBase: '/api/approval', headers: () => ({ 'X-User-Id': 'alice' }), userKey: () => 'alice', userPicker: HostPicker, businessPanels: { Form: OrderPanel } })
function Host() {
  const [actor, setActor] = useState('alice')
  useLayoutEffect(() => { client.updateOptions({ headers: () => ({ 'X-User-Id': actor }), userKey: () => actor }) }, [actor])
  return <><header style={{ padding: 16, color: 'rgb(20, 40, 60)', fontFamily: 'serif' }} data-testid="host-header">第三方业务系统
    <label>宿主身份<select value={actor} onChange={event => setActor(event.target.value)}><option value="alice">Alice</option><option value="bob">Bob</option></select></label>
  </header><ApprovalProvider client={client} options={{ userKey: () => actor }}><RouterProvider router={router} /></ApprovalProvider></>
}
createRoot(document.getElementById('root')!).render(<Host />)
