import { useEffect, useRef, useState } from "react";
import {
  defaultFileExtensions,
  type FileValue,
  type FormField,
} from "@approval/shared";
import { useApprovalClient, useApprovalIdentity } from "../../client";
import { Button, ErrorState, Input } from "../../components/ui/primitives";
import { Upload } from "lucide-react";
import { Card, CardContent } from "../../components/shadcn/card";
import {
  Item,
  ItemGroup,
  ItemContent,
  ItemDescription,
  ItemActions,
} from "../../components/shadcn/item";

export function FileList({
  files,
  remove,
}: {
  files: FileValue[];
  remove?: (id: string) => void;
}) {
  const { api } = useApprovalClient();
  const [error, setError] = useState<unknown>(null);
  return (
    <ItemGroup className="ar-file-list">
      {files.map((file) => (
        <Item size="sm" variant="outline" key={file.id}>
          <ItemContent>
            <Button
              variant="ghost"
              onClick={() => {
                setError(null);
                void api.downloadFile(file.id, file.name).catch(setError);
              }}
            >
              {file.name}
            </Button>
            <ItemDescription>
              {(file.size / 1024).toFixed(1)} KB
            </ItemDescription>
          </ItemContent>
          <ItemActions>
            {remove && (
              <Button
                variant="danger"
                aria-label={`移除 ${file.name}`}
                onClick={() => remove(file.id)}
              >
                移除
              </Button>
            )}
          </ItemActions>
        </Item>
      ))}
      <ErrorState error={error} />
    </ItemGroup>
  );
}
export function FileUploadInput({
  field,
  value,
  onChange,
}: {
  field: FormField;
  value: FileValue[];
  onChange: (files: FileValue[]) => void;
}) {
  const client = useApprovalClient();
  const identity = useApprovalIdentity();
  const active = useRef(0);
  const input = useRef<HTMLInputElement>(null);
  const currentFiles = useRef(value);
  currentFiles.current = value;
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    active.current++;
    setUploading(false);
    setError(null);
    return () => {
      active.current++;
    };
  }, [identity]);
  const accept = field.accept?.length ? field.accept : defaultFileExtensions;
  const max = field.maxFiles ?? 10;
  async function upload(files: FileList | null) {
    if (!files || uploading) return;
    const owner = client.generation;
    const uploadId = ++active.current;
    setUploading(true);
    setError(null);
    try {
      for (const file of Array.from(files)) {
        if (currentFiles.current.length >= max)
          throw new Error(`最多上传 ${max} 个文件`);
        if (!accept.includes(file.name.split(".").pop()?.toLowerCase() ?? ""))
          throw new Error("文件类型不允许");
        const stored = await client.api.uploadFile(file);
        if (client.generation !== owner || active.current !== uploadId) return;
        const next = [...currentFiles.current, stored];
        currentFiles.current = next;
        onChange(next);
      }
    } catch (cause) {
      if (
        client.generation === owner &&
        active.current === uploadId &&
        (cause as Error)?.name !== "AbortError"
      )
        setError(cause);
    } finally {
      if (client.generation === owner && active.current === uploadId) {
        setUploading(false);
        if (input.current) input.current.value = "";
      }
    }
  }
  return (
    <Card size="sm">
      <CardContent className="approval:flex approval:flex-col approval:gap-3">
        <FileList
          files={value}
          remove={(id) => onChange(value.filter((file) => file.id !== id))}
        />
        <Input
          ref={input}
          type="file"
          multiple
          accept={accept.map((type) => "." + type).join(",")}
          hidden
          onChange={(event) => void upload(event.target.files)}
        />
        <Button
          onClick={() => input.current?.click()}
          busy={uploading}
          disabled={value.length >= max}
        >
          <Upload size={16} /> 上传附件
        </Button>
        <ErrorState error={error} />
      </CardContent>
    </Card>
  );
}
