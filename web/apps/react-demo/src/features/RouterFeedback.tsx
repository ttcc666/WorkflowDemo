import { isRouteErrorResponse, Link, useRouteError } from "react-router";
import {
  Button,
  Card,
  EmptyState,
  Loading,
  PageHeader,
} from "@approval/react-ui";

export function RouterLoadingPage() {
  return (
    <div className="approval-react ar-route-feedback">
      <Loading />
    </div>
  );
}

export function NotFoundPage() {
  return <RouteFeedback missing />;
}

export function RouterErrorPage() {
  const error = useRouteError();
  return (
    <RouteFeedback
      missing={isRouteErrorResponse(error) && error.status === 404}
    />
  );
}

function RouteFeedback({ missing }: { missing: boolean }) {
  return (
    <div className="approval-react ar-route-feedback">
      <PageHeader
        title={missing ? "页面不存在" : "页面加载失败"}
        description={missing ? "请确认链接是否正确。" : "请稍后重试。"}
      />
      <Card>
        <EmptyState
          text={
            missing
              ? "链接可能已失效，请返回审批工作台继续操作。"
              : "暂时无法加载页面，请重试或返回审批工作台。"
          }
        />
        <div className="ar-row">
          <Link className="ar-text-link" to="/workbench">
            返回审批工作台
          </Link>
          {!missing && (
            <Button onClick={() => window.location.reload()}>重新加载</Button>
          )}
        </div>
      </Card>
    </div>
  );
}
