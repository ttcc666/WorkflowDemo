import { DatePicker } from "@approval/react-ui";
import { useState } from "react";
import {
  ApiError,
  Button,
  CommandRecovery,
  Dialog,
  ErrorState,
  Field,
  FieldGroup,
  Input,
  Select,
  useCommandIntent,
} from "@approval/react-ui";
import type { CertificateData } from "@demo-shared/types";
import { certTypeLabels } from "@demo-shared/labels";
import { useCertificateApi } from "../api";

export function CertificateFormDialog({
  certificateId,
  initial,
  sourceRequestId,
  close,
  submitted,
}: {
  certificateId?: string;
  initial?: CertificateData;
  sourceRequestId?: string;
  close: () => void;
  submitted: (requestId: string) => void;
}) {
  const api = useCertificateApi();
  const [data, setData] = useState<CertificateData>(
    initial
      ? { ...initial }
      : {
          certNo: "",
          name: "",
          holder: "",
          certType: "Normal",
          issueDate: "",
          expireDate: "",
        },
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const intent = useCommandIntent("certificate-create-update");
  async function submit() {
    if (busy) return;
    const body = {
      ...data,
      certNo: data.certNo.trim(),
      name: data.name.trim(),
      holder: data.holder.trim(),
    };
    const local: Record<string, string[]> = {};
    for (const key of [
      "certNo",
      "name",
      "holder",
      "issueDate",
      "expireDate",
    ] as const)
      if (!body[key]) local[key] = ["此项必填"];
    if (body.issueDate && body.expireDate && body.issueDate > body.expireDate)
      local.expireDate = ["到期日期不能早于发证日期"];
    setErrors(local);
    if (Object.keys(local).length) return;
    setBusy(true);
    setError(null);
    try {
      const result = await intent.run(
        `${certificateId ?? "create"}:${sourceRequestId ?? ""}`,
        { certificateId, sourceRequestId, data: body },
        (payload, key) =>
          payload.certificateId
            ? api.submitUpdate(
                payload.certificateId,
                payload.data,
                key,
                payload.sourceRequestId,
              )
            : api.submitCreate(payload.data, key, payload.sourceRequestId),
      );
      submitted(result.requestId);
    } catch (cause) {
      setError(cause);
      if (cause instanceof ApiError && cause.errors) setErrors(cause.errors);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busy) close();
      }}
      title={certificateId ? "申请变更注册证" : "申请登记注册证"}
      footer={
        <Button variant="primary" busy={busy} onClick={() => void submit()}>
          提交审核
        </Button>
      }
    >
      <CommandRecovery intent={intent} busy={busy} recovered={close} />
      <p>审批通过后生效，历史申请保留提交时快照。</p>
      <FieldGroup className="ar-dialog-form-grid">
        {(
          [
            ["certNo", "注册证编号"],
            ["name", "注册证名称"],
            ["holder", "持有人"],
          ] as const
        ).map(([key, label]) => (
          <Field
            key={key}
            label={label}
            required
            error={errors[key]?.join("；")}
          >
            <Input
              aria-label={label}
              value={data[key]}
              disabled={busy || (key === "certNo" && !!certificateId)}
              maxLength={key === "certNo" ? 100 : 200}
              onChange={(event) =>
                setData({ ...data, [key]: event.target.value })
              }
            />
          </Field>
        ))}
        <Field label="证书类型" error={errors.certType?.join("；")}>
          <Select
            aria-label="证书类型"
            value={data.certType}
            options={Object.entries(certTypeLabels).map(([value, label]) => ({
              value,
              label,
            }))}
            onValueChange={(nextValue) =>
              setData({ ...data, certType: nextValue })
            }
          />
        </Field>
        {(
          [
            ["issueDate", "发证日期"],
            ["expireDate", "到期日期"],
          ] as const
        ).map(([key, label]) => (
          <Field
            key={key}
            label={label}
            required
            error={errors[key]?.join("；")}
          >
            <DatePicker
              label={label}

              value={data[key]}
              disabled={busy}
              onChange={(nextValue) => setData({ ...data, [key]: nextValue })}
            />
          </Field>
        ))}
      </FieldGroup>
      <ErrorState error={error} />
    </Dialog>
  );
}
