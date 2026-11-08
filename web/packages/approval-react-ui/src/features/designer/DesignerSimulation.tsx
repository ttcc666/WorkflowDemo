import { Card } from "../../components/ui/primitives";
import { useEffect, useRef, useState } from "react";
import {
  initialValues,
  type ApprovalSimulationResult,
  type FlowDefinition,
} from "@approval/shared";
import { useApprovalClient, useApprovalIdentity } from "../../client";
import {
  Button,
  Badge,
  Dialog,
  ErrorState,
  Field,
  Textarea,
} from "../../components/ui/primitives";
import { FlowViewer } from "../flow/FlowViewer";
import { simulationPresentation } from "../flow/simulationPresentation";
import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "../../components/shadcn/accordion";

export function DesignerSimulation({
  open,
  onOpenChange,
  definition,
  nodeId,
  ruleIndex,
  actionId,
}: {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  definition: FlowDefinition;
  nodeId?: string;
  ruleIndex?: number;
  actionId?: string;
}) {
  const client = useApprovalClient();
  const identity = useApprovalIdentity();
  const [formData, setFormData] = useState(
    JSON.stringify(initialValues(definition.form), null, 2),
  );
  const [result, setResult] = useState<ApprovalSimulationResult | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(false);
  const revision = useRef(0);
  useEffect(() => {
    revision.current++;
    setResult(null);
    setError(null);
    setLoading(false);
  }, [open, identity, definition, nodeId, ruleIndex, formData]);
  useEffect(
    () => () => {
      revision.current++;
    },
    [],
  );
  async function simulate() {
    if (!open || loading) return;
    const current = ++revision.current;
    const owner = client.generation;
    setLoading(true);
    setError(null);
    try {
      const values: unknown = JSON.parse(formData);
      if (!values || Array.isArray(values) || typeof values !== "object")
        throw new Error("模拟数据必须是 JSON 对象");
      const response = await client.api.simulate(
        JSON.stringify(definition),
        values as Record<string, unknown>,
        nodeId,
        ruleIndex,
      );
      if (current === revision.current && owner === client.generation)
        setResult(response);
    } catch (cause) {
      if (current === revision.current && owner === client.generation)
        setError(cause);
    } finally {
      if (current === revision.current) setLoading(false);
    }
  }
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={nodeId ? "单节点 / 步骤预演" : "完整流程模拟"}
      wide
      footer={
        <Button
          variant="primary"
          busy={loading}
          onClick={() => void simulate()}
        >
          执行模拟
        </Button>
      }
    >
      <Card
        title="模拟输入"
        description="使用示例表单数据检查节点路径与动作输出。"
      >
        <Field label="模拟表单数据 JSON">
          <Textarea
            aria-label="模拟表单数据 JSON"
            rows={7}
            value={formData}
            onChange={(event) => setFormData(event.target.value)}
          />
        </Field>
      </Card>
      {actionId && <p>按节点顺序预演并定位步骤：{actionId}</p>}
      <ErrorState error={error} />
      {result && (
        <>
          <div className="ar-row">
            <Badge
              status={
                !result.isValid
                  ? "Failed"
                  : result.isComplete
                    ? "Succeeded"
                    : "Pending"
              }
            >
              {!result.isValid
                ? "校验失败"
                : result.isComplete
                  ? "已完成"
                  : "未完成"}
            </Badge>
            <strong>
              {result.isValid
                ? result.isComplete
                  ? "模拟完成"
                  : "预演已暂停，请查看节点说明"
                : "模拟校验失败"}
            </strong>
          </div>
          {[...result.errors, ...result.warnings].map((message, index) => (
            <p key={index} role="status">
              {message}
            </p>
          ))}
          <FlowViewer definition={definition} simulation={result} />
          {result.steps.map((step, index) => (
            <Card
              key={`${step.nodeId}:${index}`}
              title={definition.nodes[step.nodeId]?.name ?? step.nodeId}
              description={
                step.nodeId + " · " + simulationPresentation(step).label
              }
            >
              {step.blockReason && <p role="status">{step.blockReason}</p>}
              {step.actions?.map((action) => (
                <p key={action.id}>
                  {action.id} · {action.type} ·{" "}
                  {action.status === "Succeeded"
                    ? "预演成功"
                    : action.status === "Failed"
                      ? "预演失败"
                      : action.reason?.includes("计划")
                        ? "仅展示计划"
                        : "未执行"}
                  {action.reason && `：${action.reason}`}
                </p>
              ))}
              {actionId &&
                step.actions?.some((action) => action.id === actionId) && (
                  <Card data-selected-action={actionId}>
                    <strong>所选步骤 {actionId}</strong>
                    <pre style={{ whiteSpace: "pre-wrap" }}>
                      {JSON.stringify(
                        step.actions.find((action) => action.id === actionId),
                        null,
                        2,
                      )}
                    </pre>
                  </Card>
                )}
              <Accordion type="single" collapsible>
                <AccordionItem value="raw">
                  <AccordionTrigger>
                    查看节点原始数据 {step.nodeId}
                  </AccordionTrigger>
                  <AccordionContent>
                    <pre className="ar-json-content">
                      {JSON.stringify(step, null, 2)}
                    </pre>
                  </AccordionContent>
                </AccordionItem>
              </Accordion>
            </Card>
          ))}
        </>
      )}
    </Dialog>
  );
}
