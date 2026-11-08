import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "@approval/react-ui";
import { useState } from "react";
import {
  Button,
  Card,
  DataTable,
  ErrorState,
  Field,
  Input,
  formatDateTime,
  useAsyncData,
} from "@approval/react-ui";
import { useDemoApi } from "../api";
export function DemoWebhookPanel() {
  const api = useDemoApi();
  const state = useAsyncData((signal) => api.webhook(signal));
  const [count, setCount] = useState(0);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  async function fail() {
    if (busy) return;
    setBusy(true);
    try {
      await api.webhookFailNext(count);
      state.reload();
    } catch (cause) {
      setError(cause);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Card
      title="模拟外部 Webhook"
      description="本地演示工具，可模拟失败并观察投递重试。"
    >
      <div className="ar-page-filter-toolbar">
        <Field label="接下来失败次数">
          <Input
            type="number"
            aria-label="接下来失败次数"
            min={0}
            max={100}
            value={count}
            onChange={(event) => setCount(Number(event.target.value))}
          />
        </Field>
        <Button busy={busy} onClick={() => void fail()}>
          配置 HTTP 503
        </Button>
        <Button onClick={state.reload}>刷新接收记录</Button>
      </div>
      <p>剩余模拟失败次数：{state.data?.failNext ?? "—"}</p>
      <ErrorState error={error || state.error} retry={state.reload} />
      <DataTable
        rows={state.data?.received ?? []}
        rowKey={(row) => row.receivedAt + row.idempotencyKey}
        columns={[
          { key: "path", label: "请求路径", render: (row) => row.path },
          {
            key: "key",
            label: "Idempotency-Key",
            render: (row) => row.idempotencyKey,
          },
          {
            key: "time",
            label: "接收时间",
            render: (row) => formatDateTime(row.receivedAt),
          },
          {
            key: "body",
            label: "正文",
            render: (row) => (
              <Accordion type="single" collapsible>
                <AccordionItem
                  value="content"
                  className="approval:group/disclosure"
                >
                  <AccordionTrigger>查看正文</AccordionTrigger>
                  <AccordionContent
                    forceMount
                    className="approval:group-data-[state=closed]/disclosure:hidden"
                  >
                    <div className="approval:flex approval:flex-col approval:gap-4">
                      <pre
                        className="ar-code-block"
                        style={{ whiteSpace: "pre-wrap" }}
                      >
                        {row.body}
                      </pre>
                    </div>
                  </AccordionContent>
                </AccordionItem>
              </Accordion>
            ),
          },
        ]}
        loading={state.loading}
        error={state.error}
      />
    </Card>
  );
}
