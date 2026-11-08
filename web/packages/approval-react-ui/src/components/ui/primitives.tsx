import {
  useEffect,
  useLayoutEffect,
  useId,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type ComponentProps,
  type InputHTMLAttributes,
  type ReactNode,
  type Ref,
  type TextareaHTMLAttributes,
} from "react";
import { cn } from "cn";
import { Inbox, X } from "lucide-react";
import { Button as UiButton } from "../shadcn/button";
import { Input as UiInput } from "../shadcn/input";
import { Textarea as UiTextarea } from "../shadcn/textarea";
import * as UiSelect from "../shadcn/select";
import { Checkbox as UiCheckbox } from "../shadcn/checkbox";
import { Switch as UiSwitch } from "../shadcn/switch";
import * as UiField from "../shadcn/field";
import * as UiDialog from "../shadcn/dialog";
import * as UiAlertDialog from "../shadcn/alert-dialog";
import * as UiTabs from "../shadcn/tabs";
import * as UiTooltip from "../shadcn/tooltip";
import * as UiCard from "../shadcn/card";
import * as UiEmpty from "../shadcn/empty";
import { Alert, AlertDescription } from "../shadcn/alert";
import { Badge as UiBadge } from "../shadcn/badge";
import { Spinner } from "../shadcn/spinner";
import * as UiPagination from "../shadcn/pagination";
import { FieldState, useFieldAria } from "./fieldContext";
import { revealTab } from "./tabFocus";
export function Button({
  busy,
  variant = "default",
  size = "lg",
  className,
  children,
  disabled,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  ref?: Ref<HTMLButtonElement>;
  busy?: boolean;
  variant?: "default" | "primary" | "danger" | "ghost";
  size?: "default" | "sm" | "lg" | "icon";
}) {
  const variants = {
    default: "outline",
    primary: "default",
    danger: "destructive",
    ghost: "ghost",
  } as const;
  return (
    <UiButton
      type="button"
      {...props}
      variant={variants[variant]}
      size={size}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      className={cn("ar-button", className)}
    >
      {busy && <Spinner data-icon="inline-start" aria-hidden="true" />}
      {children}
    </UiButton>
  );
}
export function Input(
  props: InputHTMLAttributes<HTMLInputElement> & {
    ref?: Ref<HTMLInputElement>;
  },
) {
  const aria = useFieldAria(props);
  return (
    <UiInput {...props} {...aria} className={cn("ar-input", props.className)} />
  );
}
export function Textarea(
  props: TextareaHTMLAttributes<HTMLTextAreaElement> & {
    ref?: Ref<HTMLTextAreaElement>;
  },
) {
  const aria = useFieldAria(props);
  return (
    <UiTextarea
      {...props}
      {...aria}
      className={cn("ar-input", props.className)}
    />
  );
}
export function Select({
  options,
  placeholder = "请选择",
  className,
  value = "",
  onValueChange,
  disabled,
  required,
  name,
  size,
  ...props
}: Omit<
  ComponentProps<typeof UiSelect.SelectTrigger>,
  "value" | "onChange" | "defaultValue"
> & {
  options: { value: string; label: string }[];
  placeholder?: string;
  value?: string | number;
  onValueChange?: (value: string) => void;
  required?: boolean;
  name?: string;
}) {
  const id = useId();
  const aria = useFieldAria(props);
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (disabled) setOpen(false);
  }, [disabled]);
  let empty = `approval-empty-${id}`;
  while (options.some((item) => item.value === empty)) empty += "-";
  return (
    <UiSelect.Select
      open={open && !disabled}
      onOpenChange={setOpen}
      value={String(value)}
      onValueChange={(next) => {
        if (!disabled) onValueChange?.(next === empty ? "" : next);
      }}
      disabled={disabled}
      required={required}
      name={name}
    >
      <UiSelect.SelectTrigger
        {...props}
        {...aria}
        size={size}
        data-value={String(value)}
        className={cn("ar-select approval:w-full approval:min-w-0", className)}
      >
        <UiSelect.SelectValue placeholder={placeholder} />
      </UiSelect.SelectTrigger>
      <UiSelect.SelectContent
        className="approval-react"
        position="popper"
        collisionPadding={16}
      >
        <UiSelect.SelectGroup>
          <UiSelect.SelectItem value={empty} data-option-value="">
            {placeholder}
          </UiSelect.SelectItem>
          {options
            .filter((item) => item.value !== "")
            .map((item) => (
              <UiSelect.SelectItem
                key={item.value}
                value={item.value}
                data-option-value={item.value}
              >
                {item.label}
              </UiSelect.SelectItem>
            ))}
        </UiSelect.SelectGroup>
      </UiSelect.SelectContent>
    </UiSelect.Select>
  );
}
export function Checkbox({
  checked,
  onChange,
  children,
  disabled,
}: {
  checked: boolean;
  onChange: (value: boolean) => void;
  children: ReactNode;
  disabled?: boolean;
}) {
  const id = useId();
  return (
    <UiField.Field orientation="horizontal" data-disabled={disabled}>
      <UiCheckbox
        id={id}
        checked={checked}
        disabled={disabled}
        onCheckedChange={(next) => onChange(next === true)}
      />
      <UiField.FieldLabel htmlFor={id}>{children}</UiField.FieldLabel>
    </UiField.Field>
  );
}
export function Switch({
  checked,
  onChange,
  label,
}: {
  checked: boolean;
  onChange: (value: boolean) => void;
  label: string;
}) {
  const aria = useFieldAria({ "aria-label": label });
  return (
    <UiSwitch
      {...aria}
      checked={checked}
      onCheckedChange={onChange}
      aria-label={label}
    />
  );
}
export function Field({
  label,
  error,
  help,
  required,
  group = false,
  className,
  children,
}: {
  label: string;
  error?: string;
  help?: ReactNode;
  required?: boolean;
  group?: boolean;
  className?: string;
  children: ReactNode;
}) {
  const id = useId();
  const container = useRef<HTMLElement | null>(null);
  const [controlId, setControlId] = useState<string>();
  useLayoutEffect(() => {
    const control = group
      ? null
      : container.current?.querySelector<HTMLElement>(
          'input:not([type="hidden"]):not([hidden]), textarea, [role="combobox"]:not([aria-hidden="true"]), [role="switch"]',
        );
    setControlId(control?.id || undefined);
  }, [children, group]);
  const Container = group ? UiField.FieldSet : UiField.Field;
  const describedBy =
    [help && id + "-help", error && id + "-error"].filter(Boolean).join(" ") ||
    undefined;
  return (
    <FieldState.Provider
      value={{ invalid: !!error, required, describedBy, labelledBy: id }}
    >
      <Container
        ref={(element: HTMLElement | null) => {
          container.current = element;
        }}
        className={cn("ar-field", className)}
        aria-labelledby={id}
        aria-describedby={describedBy}
        data-invalid={!!error}
      >
        {group ? (
          <UiField.FieldLegend id={id}>
            {label}
            {required && (
              <>
                <span aria-hidden="true" className="approval:text-destructive">
                  {" "}
                  *
                </span>
                <span className="approval:sr-only">必填</span>
              </>
            )}
          </UiField.FieldLegend>
        ) : (
          <UiField.FieldLabel id={id} htmlFor={controlId}>
            {label}
            {required && (
              <span aria-hidden="true" className="approval:text-destructive">
                {" "}
                *
              </span>
            )}
          </UiField.FieldLabel>
        )}
        {children}
        {help && (
          <UiField.FieldDescription id={id + "-help"}>
            {help}
          </UiField.FieldDescription>
        )}
        {error && (
          <UiField.FieldError id={id + "-error"}>{error}</UiField.FieldError>
        )}
      </Container>
    </FieldState.Provider>
  );
}
export function Dialog({
  open,
  onOpenChange,
  title,
  children,
  footer,
  wide = false,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  const previousFocus = useRef<HTMLElement | null>(null);
  return (
    <UiDialog.Dialog open={open} onOpenChange={onOpenChange}>
      <UiDialog.DialogContent
        className={cn(
          "approval-react ar-dialog-panel approval:flex approval:max-h-[calc(100dvh-2rem)] approval:flex-col approval:gap-0 approval:overflow-hidden approval:p-0",
          wide ? "approval:sm:max-w-[1100px]" : "approval:sm:max-w-[560px]",
        )}
        showCloseButton={false}
        aria-describedby={undefined}
        onOpenAutoFocus={() => {
          previousFocus.current = document.activeElement as HTMLElement;
        }}
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          if (previousFocus.current?.isConnected) previousFocus.current.focus();
        }}
      >
        <UiDialog.DialogHeader className="approval:relative approval:shrink-0 approval:p-4 approval:pr-12">
          <UiDialog.DialogTitle>{title}</UiDialog.DialogTitle>
          <UiDialog.DialogClose asChild>
            <UiButton
              variant="ghost"
              size="icon-sm"
              className="approval:absolute approval:top-2 approval:right-2"
              aria-label="关闭"
            >
              <X data-icon="inline-start" />
            </UiButton>
          </UiDialog.DialogClose>
        </UiDialog.DialogHeader>
        <div className="ar-stack ar-dialog-body approval:min-h-0 approval:overflow-y-auto approval:p-4">
          {children}
        </div>
        {footer && (
          <UiDialog.DialogFooter className="approval:m-0 approval:shrink-0">
            {footer}
          </UiDialog.DialogFooter>
        )}
      </UiDialog.DialogContent>
    </UiDialog.Dialog>
  );
}
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  children,
  confirm,
  busy,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  children: ReactNode;
  confirm: () => void;
  busy?: boolean;
}) {
  const previousFocus = useRef<HTMLElement | null>(null);
  return (
    <UiAlertDialog.AlertDialog open={open} onOpenChange={onOpenChange}>
      <UiAlertDialog.AlertDialogContent
        className="approval-react approval:flex approval:max-h-[calc(100dvh-2rem)] approval:flex-col approval:gap-0 approval:overflow-hidden approval:p-0"
        onOpenAutoFocus={() => {
          previousFocus.current = document.activeElement as HTMLElement;
        }}
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          if (previousFocus.current?.isConnected) previousFocus.current.focus();
        }}
      >
        <UiAlertDialog.AlertDialogHeader className="approval:shrink-0 approval:p-4">
          <UiAlertDialog.AlertDialogTitle>
            {title}
          </UiAlertDialog.AlertDialogTitle>
        </UiAlertDialog.AlertDialogHeader>
        <UiAlertDialog.AlertDialogDescription asChild>
          <div className="ar-stack approval:min-h-0 approval:overflow-y-auto approval:px-4 approval:pb-4">
            {children}
          </div>
        </UiAlertDialog.AlertDialogDescription>
        <UiAlertDialog.AlertDialogFooter className="approval:m-0 approval:shrink-0">
          <Button onClick={() => onOpenChange(false)} disabled={busy}>
            取消
          </Button>
          <Button variant="primary" busy={busy} onClick={confirm}>
            确认
          </Button>
        </UiAlertDialog.AlertDialogFooter>
      </UiAlertDialog.AlertDialogContent>
    </UiAlertDialog.AlertDialog>
  );
}
export function Tabs({
  value,
  onChange,
  tabs,
}: {
  value: string;
  onChange: (value: string) => void;
  tabs: { value: string; label: string }[];
}) {
  return (
    <UiTabs.Tabs
      value={value}
      onValueChange={onChange}
      className="approval:min-w-0"
    >
      <div className="ar-tab-strip" data-tab-scroll>
        <UiTabs.TabsList variant="line" className="ar-tabs">
          {tabs.map((tab) => (
            <UiTabs.TabsTrigger
              key={tab.value}
              value={tab.value}
              onFocus={(event) => revealTab(event.currentTarget)}
            >
              {tab.label}
            </UiTabs.TabsTrigger>
          ))}
        </UiTabs.TabsList>
      </div>
    </UiTabs.Tabs>
  );
}
export function Tooltip({
  text,
  children,
}: {
  text: string;
  children: ReactNode;
}) {
  return (
    <UiTooltip.TooltipProvider>
      <UiTooltip.Tooltip>
        <UiTooltip.TooltipTrigger asChild>{children}</UiTooltip.TooltipTrigger>
        <UiTooltip.TooltipContent className="approval-react">
          {text}
        </UiTooltip.TooltipContent>
      </UiTooltip.Tooltip>
    </UiTooltip.TooltipProvider>
  );
}
export function Badge({
  children,
  status,
}: {
  children: ReactNode;
  status?: string;
}) {
  const variant = ["Approved", "Succeeded", "Active"].includes(status ?? "")
    ? "success"
    : ["Rejected", "Failed"].includes(status ?? "")
      ? "destructive"
      : ["Pending", "Returned", "Draft"].includes(status ?? "")
        ? "warning"
        : ["Running", "Enabled", "Reviewing"].includes(status ?? "")
          ? "info"
          : "secondary";
  return (
    <UiBadge variant={variant} data-status={status}>
      {children}
    </UiBadge>
  );
}
export function Card({
  title,
  description,
  action,
  children,
  className,
  size,
  ...props
}: Omit<React.HTMLAttributes<HTMLDivElement>, "title"> & {
  title?: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  children: ReactNode;
  size?: "default" | "sm";
}) {
  return (
    <UiCard.Card {...props} size={size} className={cn("ar-card", className)}>
      {(title || description || action) && (
        <UiCard.CardHeader>
          {title && (
            <UiCard.CardTitle role="heading" aria-level={2}>
              {title}
            </UiCard.CardTitle>
          )}
          {description && (
            <UiCard.CardDescription>{description}</UiCard.CardDescription>
          )}
          {action && <UiCard.CardAction>{action}</UiCard.CardAction>}
        </UiCard.CardHeader>
      )}
      <UiCard.CardContent className="ar-stack">{children}</UiCard.CardContent>
    </UiCard.Card>
  );
}
export function ErrorState({
  error,
  retry,
}: {
  error: unknown;
  retry?: () => void;
}) {
  return error ? (
    <Alert variant="destructive">
      <AlertDescription>
        {error instanceof Error ? error.message : String(error)}
        {retry && <Button onClick={retry}>重试</Button>}
      </AlertDescription>
    </Alert>
  ) : null;
}
export function EmptyState({ text = "暂无数据" }: { text?: string }) {
  return (
    <UiEmpty.Empty>
      <UiEmpty.EmptyHeader>
        <UiEmpty.EmptyMedia variant="icon">
          <Inbox />
        </UiEmpty.EmptyMedia>
        <UiEmpty.EmptyDescription>{text}</UiEmpty.EmptyDescription>
      </UiEmpty.EmptyHeader>
    </UiEmpty.Empty>
  );
}
export function Loading() {
  return (
    <div
      role="status"
      className="approval:flex approval:items-center approval:justify-center approval:gap-2 approval:py-8 approval:text-muted-foreground"
    >
      <Spinner />
      加载中…
    </div>
  );
}
export function Pagination({
  page,
  pageSize,
  total,
  loading,
  onChange,
}: {
  page: number;
  pageSize: number;
  total: number | undefined;
  loading?: boolean;
  onChange: (page: number, size: number) => void;
}) {
  return (
    <div className="ar-pagination">
      <span role="status">
        {total === undefined
          ? loading
            ? "总数加载中…"
            : "总数未知"
          : `共 ${total} 条`}
      </span>
      <Select
        aria-label="每页条数"
        value={pageSize}
        disabled={loading}
        options={[20, 50, 100].map((size) => ({
          value: String(size),
          label: `${size} / 页`,
        }))}
        onValueChange={(nextValue) => onChange(1, Number(nextValue))}
      />
      <UiPagination.Pagination>
        <UiPagination.PaginationContent>
          <UiPagination.PaginationItem>
            <Button
              disabled={loading || total === undefined || page <= 1}
              onClick={() => onChange(page - 1, pageSize)}
            >
              上一页
            </Button>
          </UiPagination.PaginationItem>
          <UiPagination.PaginationItem>
            <UiButton variant="outline" aria-current="page">
              {page}
            </UiButton>
          </UiPagination.PaginationItem>
          <UiPagination.PaginationItem>
            <Button
              disabled={
                loading || total === undefined || page * pageSize >= total
              }
              onClick={() => onChange(page + 1, pageSize)}
            >
              下一页
            </Button>
          </UiPagination.PaginationItem>
        </UiPagination.PaginationContent>
      </UiPagination.Pagination>
    </div>
  );
}
export function JsonEditor({
  value,
  onChange,
  kind = "object",
  label = "JSON",
  optional = false,
}: {
  value: unknown;
  onChange: (value: unknown) => void;
  kind?: "object" | "array";
  label?: string;
  optional?: boolean;
}) {
  const serialized = JSON.stringify(
    value ?? (kind === "array" ? [] : {}),
    null,
    2,
  );
  const [text, setText] = useState(serialized);
  const [error, setError] = useState("");
  const [previous, setPrevious] = useState(serialized);
  if (previous !== serialized) {
    setPrevious(serialized);
    setText(serialized);
    setError("");
  }
  function commit(next: string) {
    setText(next);
    try {
      const parsed: unknown = JSON.parse(next);
      if (
        kind === "array"
          ? !Array.isArray(parsed)
          : !parsed || typeof parsed !== "object" || Array.isArray(parsed)
      )
        throw new Error(`必须是 JSON ${kind === "array" ? "数组" : "对象"}`);
      setError("");
      onChange(parsed);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }
  return (
    <Field label={label} error={error}>
      <Textarea
        aria-label={label}
        rows={5}
        value={text}
        onChange={(event) => commit(event.target.value)}
        spellCheck={false}
      />
      {optional && <Button onClick={() => onChange(undefined)}>清除</Button>}
    </Field>
  );
}
