import {
  useEffect,
  useId,
  useRef,
  useState,
  type ReactNode,
  type InputHTMLAttributes,
} from "react";
import { CalendarDays, ChevronDown, X } from "lucide-react";
import { parseISO, isValid } from "date-fns";
import { toast } from "sonner";
import { zhCN } from "date-fns/locale";
import type { FormField as FormFieldModel } from "@approval/shared";
import { Field, Input, Button, ConfirmDialog } from "./primitives";
import { useFieldAria } from "./fieldContext";
import { Skeleton as UiSkeleton } from "../shadcn/skeleton";
import * as UiSheet from "../shadcn/sheet";
import * as UiPopover from "../shadcn/popover";
import * as UiMenu from "../shadcn/dropdown-menu";
import * as UiRadio from "../shadcn/radio-group";
import * as UiField from "../shadcn/field";
import * as UiCommand from "../shadcn/command";
import { Calendar as UiCalendar } from "../shadcn/calendar";
import { Toaster } from "../shadcn/sonner";
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
} from "../shadcn/input-group";

export function SuggestionInput({
  value,
  onValueChange,
  suggestions,
  ...props
}: Omit<InputHTMLAttributes<HTMLInputElement>, "value" | "onChange"> & {
  value: string;
  onValueChange: (value: string) => void;
  suggestions: string[];
}) {
  const [open, setOpen] = useState(false);
  const label = props["aria-label"] ?? "选项";
  useEffect(() => {
    if (props.disabled) setOpen(false);
  }, [props.disabled]);
  return (
    <UiPopover.Popover open={open && !props.disabled} onOpenChange={setOpen}>
      <InputGroup>
        <Input
          {...props}
          data-slot="input-group-control"
          value={value}
          onChange={(event) => onValueChange(event.target.value)}
        />
        <InputGroupAddon align="inline-end">
          <UiPopover.PopoverTrigger asChild>
            <InputGroupButton
              size="icon-xs"
              aria-label={`${label}选项`}
              disabled={props.disabled}
            >
              <ChevronDown />
            </InputGroupButton>
          </UiPopover.PopoverTrigger>
        </InputGroupAddon>
      </InputGroup>
      <UiPopover.PopoverContent
        className="approval-react approval:p-0"
        align="start"
        collisionPadding={16}
      >
        <UiCommand.Command label={`${label}搜索`}>
          <UiCommand.CommandInput aria-label={`${label}搜索`} />
          <UiCommand.CommandList>
            <UiCommand.CommandEmpty>
              没有匹配项，可在输入框填写自定义值。
            </UiCommand.CommandEmpty>
            <UiCommand.CommandGroup>
              {suggestions.map((suggestion) => (
                <UiCommand.CommandItem
                  key={suggestion}
                  value={suggestion}
                  onSelect={() => {
                    onValueChange(suggestion);
                    setOpen(false);
                  }}
                >
                  {suggestion}
                </UiCommand.CommandItem>
              ))}
            </UiCommand.CommandGroup>
          </UiCommand.CommandList>
        </UiCommand.Command>
      </UiPopover.PopoverContent>
    </UiPopover.Popover>
  );
}

export type FormField = FormFieldModel;
export const FormField = Field;
export const Skeleton = () => (
  <UiSkeleton
    role="status"
    aria-label="正在加载内容"
    className="approval:h-6 approval:w-full"
  />
);
export function AlertDialog(props: {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  title: string;
  children: ReactNode;
  confirm: () => void;
  busy?: boolean;
}) {
  return <ConfirmDialog {...props} />;
}
export function Drawer({
  open,
  onOpenChange,
  title,
  children,
}: {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  title: string;
  children: ReactNode;
}) {
  const previousFocus = useRef<HTMLElement | null>(null);
  return (
    <UiSheet.Sheet open={open} onOpenChange={onOpenChange}>
      <UiSheet.SheetContent
        className="approval-react approval:gap-0 approval:data-[side=right]:w-[min(600px,95vw)] approval:sm:max-w-[600px]"
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
        <UiSheet.SheetHeader className="approval:relative approval:shrink-0 approval:border-b approval:pr-14">
          <UiSheet.SheetTitle>{title}</UiSheet.SheetTitle>
          <UiSheet.SheetClose asChild>
            <Button
              variant="ghost"
              size="icon"
              aria-label="关闭抽屉"
              className="approval:absolute approval:top-2 approval:right-2"
            >
              <X data-icon="inline-start" />
            </Button>
          </UiSheet.SheetClose>
        </UiSheet.SheetHeader>
        <div className="approval:min-h-0 approval:flex-1 approval:overflow-y-auto approval:p-4">
          {children}
        </div>
      </UiSheet.SheetContent>
    </UiSheet.Sheet>
  );
}
export function Popover({
  trigger,
  children,
}: {
  trigger: ReactNode;
  children: ReactNode;
}) {
  return (
    <UiPopover.Popover>
      <UiPopover.PopoverTrigger asChild>{trigger}</UiPopover.PopoverTrigger>
      <UiPopover.PopoverContent className="approval-react">
        {children}
      </UiPopover.PopoverContent>
    </UiPopover.Popover>
  );
}
export function DropdownMenu({
  trigger,
  items,
}: {
  trigger: ReactNode;
  items: { label: string; action: () => void; disabled?: boolean }[];
}) {
  return (
    <UiMenu.DropdownMenu>
      <UiMenu.DropdownMenuTrigger asChild>{trigger}</UiMenu.DropdownMenuTrigger>
      <UiMenu.DropdownMenuContent className="approval-react">
        <UiMenu.DropdownMenuGroup>
          {items.map((item, index) => (
            <UiMenu.DropdownMenuItem
              key={index}
              disabled={item.disabled}
              onSelect={item.action}
            >
              {item.label}
            </UiMenu.DropdownMenuItem>
          ))}
        </UiMenu.DropdownMenuGroup>
      </UiMenu.DropdownMenuContent>
    </UiMenu.DropdownMenu>
  );
}
export function RadioGroup({
  value,
  onChange,
  options,
  label,
  disabled,
}: {
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
  label: string;
  disabled?: boolean;
}) {
  const id = useId();
  const aria = useFieldAria({ "aria-label": label });
  return (
    <UiField.FieldSet data-disabled={disabled}>
      <UiRadio.RadioGroup
        {...aria}
        name={id}
        className="approval:flex approval:flex-wrap approval:gap-4"
        value={value}
        onValueChange={onChange}
        aria-label={label}
        disabled={disabled}
      >
        {options.map((option, index) => (
          <UiField.Field key={option.value} orientation="horizontal">
            <UiRadio.RadioGroupItem id={id + index} value={option.value} />
            <UiField.FieldLabel htmlFor={id + index}>
              {option.label}
            </UiField.FieldLabel>
          </UiField.Field>
        ))}
      </UiRadio.RadioGroup>
    </UiField.FieldSet>
  );
}
export function Toast({
  open,
  onOpenChange,
  title,
  description,
}: {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  title: string;
  description?: string;
}) {
  const id = useId();
  const change = useRef(onOpenChange);
  change.current = onOpenChange;
  useEffect(() => {
    if (open)
      toast(title, {
        id,
        description,
        duration: 5000,
        onDismiss: () => change.current(false),
        onAutoClose: () => change.current(false),
      });
    else toast.dismiss(id);
    return () => {
      toast.dismiss(id);
    };
  }, [open, title, description, id]);
  return (
    <Toaster
      theme="light"
      className="approval-react"
      position="bottom-right"
      closeButton
    />
  );
}
export function DatePicker({
  value,
  onChange,
  label,
  min,
  max,
  disabled,
  showInputError = true,
}: {
  value: string;
  onChange: (value: string) => void;
  label: string;
  min?: string;
  max?: string;
  disabled?: boolean;
  showInputError?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState(false);
  const selected = value ? localDate(value) : undefined;
  const errorId = useId();
  useEffect(() => {
    if (disabled) {
      setOpen(false);
      setEditing(false);
    }
  }, [disabled]);
  const minimum = min ? localDate(min) : undefined;
  const maximum = max ? localDate(max) : undefined;
  const invalid =
    !!value &&
    (!selected ||
      (!!minimum && selected < minimum) ||
      (!!maximum && selected > maximum));
  return (
    <div className="approval:flex approval:min-w-0 approval:flex-col approval:gap-2">
      <UiPopover.Popover open={open && !disabled} onOpenChange={setOpen}>
        <InputGroup>
          <Input
            data-slot="input-group-control"
            aria-label={label}
            value={value}
            placeholder="YYYY-MM-DD"
            disabled={disabled}
            aria-invalid={(showInputError && !editing && invalid) || undefined}
            aria-describedby={
              showInputError && !editing && invalid ? errorId : undefined
            }
            onFocus={() => setEditing(true)}
            onBlur={() => setEditing(false)}
            onChange={(event) => onChange(event.target.value)}
          />
          <InputGroupAddon align="inline-end">
            <UiPopover.PopoverTrigger asChild>
              <InputGroupButton
                size="icon-xs"
                aria-label={`选择${label}`}
                disabled={disabled}
              >
                <CalendarDays />
              </InputGroupButton>
            </UiPopover.PopoverTrigger>
          </InputGroupAddon>
        </InputGroup>
        <UiPopover.PopoverContent
          className="approval-react approval:w-auto approval:p-0"
          align="start"
          collisionPadding={16}
          aria-label={`${label}日历`}
        >
          <Calendar
            value={value}
            min={min}
            max={max}
            onChange={(day) => {
              onChange(day);
              setOpen(false);
            }}
          />
          <div className="approval:flex approval:justify-end approval:p-2">
            <Button
              size="sm"
              onClick={() => {
                onChange("");
                setOpen(false);
              }}
            >
              <X data-icon="inline-start" />
              清空日期
            </Button>
          </div>
        </UiPopover.PopoverContent>
      </UiPopover.Popover>
      {showInputError && !editing && invalid && (
        <UiField.FieldError id={errorId}>
          请输入有效日期
          {min || max
            ? `（${min || "不限"} 至 ${max || "不限"}）`
            : "（YYYY-MM-DD）"}
        </UiField.FieldError>
      )}
    </div>
  );
}
export function DateRangePicker({
  value,
  onChange,
  label,
}: {
  value: [string, string];
  onChange: (value: [string, string]) => void;
  label: string;
}) {
  return (
    <UiField.FieldGroup className="approval:flex-row approval:gap-3">
      <DatePicker
        label={label + "开始"}
        value={value[0]}
        max={value[1] || undefined}
        onChange={(date) => onChange([date, value[1]])}
      />
      <DatePicker
        label={label + "结束"}
        value={value[1]}
        min={value[0] || undefined}
        onChange={(date) => onChange([value[0], date])}
      />
    </UiField.FieldGroup>
  );
}
function localDate(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || Number(value.slice(0, 4)) < 1)
    return undefined;
  const parsed = parseISO(value);
  return isValid(parsed) && canonicalDay(parsed) === value ? parsed : undefined;
}
function canonicalDay(date: Date) {
  return `${String(date.getFullYear()).padStart(4, "0")}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
export function Calendar({
  value,
  onChange,
  min,
  max,
}: {
  value: string;
  onChange: (value: string) => void;
  min?: string;
  max?: string;
}) {
  const selected = value ? localDate(value) : undefined;
  const minimum = min ? localDate(min) : undefined;
  const maximum = max ? localDate(max) : undefined;
  const disabled = [
    ...(minimum ? [{ before: minimum }] : []),
    ...(maximum ? [{ after: maximum }] : []),
  ];
  return (
    <UiCalendar
      mode="single"
      selected={selected}
      defaultMonth={selected}
      locale={zhCN}
      disabled={disabled}
      onSelect={(date) => {
        if (date) onChange(canonicalDay(date));
      }}
      labels={{
        labelPrevious: () => "上个月",
        labelNext: () => "下个月",
        labelDayButton: (date) => canonicalDay(date),
      }}
    />
  );
}
export function Command({
  options,
  onSelect,
  label = "搜索",
  value = "",
}: {
  options: { value: string; label: string }[];
  onSelect: (value: string) => void;
  label?: string;
  value?: string;
}) {
  const [query, setQuery] = useState(value);
  return (
    <UiCommand.Command label={label}>
      <UiCommand.CommandInput
        aria-label={label}
        value={query}
        onValueChange={setQuery}
      />
      <UiCommand.CommandList>
        <UiCommand.CommandEmpty>没有匹配项</UiCommand.CommandEmpty>
        <UiCommand.CommandGroup>
          {options.map((option) => (
            <UiCommand.CommandItem
              key={option.value}
              value={option.value}
              keywords={[option.label]}
              onSelect={() => onSelect(option.value)}
            >
              {option.label}
            </UiCommand.CommandItem>
          ))}
        </UiCommand.CommandGroup>
      </UiCommand.CommandList>
    </UiCommand.Command>
  );
}
export function FileUpload({
  label,
  accept,
  multiple,
  onFiles,
  disabled,
}: {
  label: string;
  accept?: string;
  multiple?: boolean;
  onFiles: (files: File[]) => void;
  disabled?: boolean;
}) {
  return (
    <Input
      type="file"
      aria-label={label}
      accept={accept}
      multiple={multiple}
      disabled={disabled}
      onChange={(event) => {
        onFiles(Array.from(event.target.files ?? []));
        event.target.value = "";
      }}
    />
  );
}
