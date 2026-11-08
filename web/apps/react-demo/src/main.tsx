import { createRoot } from "react-dom/client";
import { StrictMode, useSyncExternalStore } from "react";
import {
  createBrowserRouter,
  RouterProvider,
  Outlet,
  Link,
  NavLink,
  Navigate,
  useLocation,
} from "react-router";
import {
  LayoutDashboard,
  Plus,
  Network,
  BarChart3,
  Mail,
  Activity,
  FileBadge,
  Workflow,
} from "lucide-react";
import {
  ApprovalProvider,
  ErrorState,
  Loading,
  MessageBell,
  Select,
  createApprovalRoutes,
  useApprovalUser,
  useAsyncData,
  SidebarProvider,
  Sidebar,
  SidebarHeader,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarGroupContent,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuButton,
  SidebarInset,
  SidebarTrigger,
  useSidebar,
  Breadcrumb,
  BreadcrumbList,
  BreadcrumbItem,
  BreadcrumbPage,
  BreadcrumbSeparator,
  Avatar,
  AvatarFallback,
  Separator,
} from "@approval/react-ui";
import { createDemoSession } from "./session";
import { useDemoApi } from "./api";
import { CertificateRequestPanel } from "./features/CertificateRequestPanel";
import { DemoWebhookPanel } from "./features/DemoWebhookPanel";
import {
  NotFoundPage,
  RouterErrorPage,
  RouterLoadingPage,
} from "./features/RouterFeedback";
import "@approval/react-ui/style.css";
import "./styles.css";
const session = createDemoSession(localStorage);
const icons = {
  "/workbench": LayoutDashboard,
  "/start": Plus,
  "/definitions": Network,
  "/monitor": BarChart3,
  "/outbox": Mail,
  "/diagnostics": Activity,
  "/certificates": FileBadge,
};
type NavigationLink = {
  path: keyof typeof icons;
  label: string;
  visible: boolean;
};
function AppSidebar({
  links,
  active,
}: {
  links: NavigationLink[];
  active: string;
}) {
  const { setOpenMobile } = useSidebar();
  return (
    <Sidebar collapsible="offcanvas" className="ar-demo-sidebar">
      <SidebarHeader>
        <Link className="ar-demo-brand" to="/workbench">
          <span className="ar-demo-brand-mark">
            <Workflow />
          </span>
          <span>
            Workflow<small>企业审批中心</small>
          </span>
        </Link>
      </SidebarHeader>
      <SidebarContent>
        {["工作空间", "流程管理"].map((group) => {
          const grouped = links.filter((link) =>
            group === "工作空间"
              ? ["/workbench", "/start", "/certificates"].includes(link.path)
              : !["/workbench", "/start", "/certificates"].includes(link.path),
          );
          return (
            grouped.length > 0 && (
              <SidebarGroup key={group}>
                <SidebarGroupLabel>{group}</SidebarGroupLabel>
                <SidebarGroupContent>
                  <SidebarMenu>
                    {grouped.map((link) => {
                      const Icon = icons[link.path];
                      return (
                        <SidebarMenuItem key={link.path}>
                          <SidebarMenuButton
                            asChild
                            className="approval:h-11 approval:gap-3 approval:px-3"
                            isActive={active.startsWith(link.path)}
                            tooltip={link.label}
                          >
                            <NavLink
                              to={link.path}
                              onClick={() => setOpenMobile(false)}
                            >
                              <Icon />
                              <span>{link.label}</span>
                            </NavLink>
                          </SidebarMenuButton>
                        </SidebarMenuItem>
                      );
                    })}
                  </SidebarMenu>
                </SidebarGroupContent>
              </SidebarGroup>
            )
          );
        })}
      </SidebarContent>
      <SidebarFooter>
        <Separator />
        <small className="ar-demo-environment">Workflow Demo · 本地环境</small>
      </SidebarFooter>
    </Sidebar>
  );
}
function Shell() {
  const user = useApprovalUser();
  const api = useDemoApi();
  const users = useAsyncData(() => api.users());
  const id = useSyncExternalStore(session.subscribe, session.getSnapshot);
  const location = useLocation();
  const links: NavigationLink[] = [
    { path: "/workbench", label: "审批工作台", visible: !!user.me },
    {
      path: "/start",
      label: "发起申请",
      visible:
        user.formsEnabled &&
        (user.can("approval.instance.start") ||
          user.can("approval.draft.read")),
    },
    {
      path: "/definitions",
      label: "流程定义",
      visible: user.canViewDefinitions,
    },
    {
      path: "/monitor",
      label: "流程监控",
      visible: [
        "approval.instance.read-all",
        "approval.statistics.read",
        "approval.task.reassign-bulk",
      ].some(user.can),
    },
    {
      path: "/outbox",
      label: "投递记录",
      visible: user.can("approval.outbox.read"),
    },
    {
      path: "/diagnostics",
      label: "运行诊断",
      visible: user.can("approval.diagnostics.read"),
    },
    { path: "/certificates", label: "注册证业务", visible: !!user.me },
  ];
  const visible = links.filter((link) => link.visible);
  const active = location.pathname.startsWith("/designer/")
    ? "/definitions"
    : location.pathname.startsWith("/approvals/")
      ? "/workbench"
      : location.pathname;
  const title = location.pathname.startsWith("/designer/")
    ? "流程设计器"
    : location.pathname.startsWith("/approvals/")
      ? "审批详情"
      : (visible.find((link) => active.startsWith(link.path))?.label ??
        "工作空间");
  return (
    <SidebarProvider>
      <AppSidebar links={visible} active={active} />
      <SidebarInset className="approval:min-w-0">
        <header className="ar-demo-header">
          <div className="ar-row">
            <SidebarTrigger aria-label="导航菜单" />
            <Separator orientation="vertical" className="approval:h-4" />
            <Breadcrumb>
              <BreadcrumbList>
                <BreadcrumbItem>
                  {[
                    "/definitions",
                    "/monitor",
                    "/outbox",
                    "/diagnostics",
                  ].includes(active)
                    ? "流程管理"
                    : "工作空间"}
                </BreadcrumbItem>
                <BreadcrumbSeparator />
                <BreadcrumbItem>
                  <BreadcrumbPage>{title}</BreadcrumbPage>
                </BreadcrumbItem>
              </BreadcrumbList>
            </Breadcrumb>
          </div>
          <div className="ar-row">
            <MessageBell />
            <Separator orientation="vertical" className="approval:h-5" />
            <Avatar>
              <AvatarFallback>{id.charAt(0).toUpperCase()}</AvatarFallback>
            </Avatar>
            <div className="ar-demo-user-switch">
              <small className="approval:text-muted-foreground">
                演示用户{user.roles.length ? ` · ${user.roles.join("、")}` : ""}
              </small>
              <Select
                aria-label="演示用户"
                value={id}
                options={(users.data ?? []).map((user) => ({
                  value: user.id,
                  label: user.name,
                }))}
                onValueChange={(nextValue) => session.switchUser(nextValue)}
              />
            </div>
          </div>
        </header>
        <div
          className="ar-demo-main"
          data-studio={location.pathname.startsWith("/designer/")}
        >
          <ErrorState error={users.error} retry={users.reload} />
          {user.error ? (
            <ErrorState error={user.error} retry={user.reload} />
          ) : !user.me ? (
            <Loading />
          ) : (
            <Outlet />
          )}
        </div>
      </SidebarInset>
    </SidebarProvider>
  );
}
const router = createBrowserRouter([
  {
    element: <Shell />,
    errorElement: <RouterErrorPage />,
    HydrateFallback: RouterLoadingPage,
    children: [
      { index: true, element: <Navigate to="/workbench" replace /> },
      ...createApprovalRoutes(),
      {
        path: "certificates",
        lazy: () =>
          import("./pages/CertificatesPage").then((module) => ({
            Component: module.default,
          })),
      },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);
function App() {
  useSyncExternalStore(session.subscribe, session.getSnapshot);
  return (
    <ApprovalProvider
      options={{
        headers: session.headers,
        userKey: session.getSnapshot,
        businessPanels: { Certificate: CertificateRequestPanel },
        outboxPanel: DemoWebhookPanel,
      }}
    >
      <RouterProvider router={router} />
    </ApprovalProvider>
  );
}
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
