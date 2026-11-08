import * as React from "react";
import { cn } from "cn";
import { Select as SelectPrimitive } from "radix-ui";
import { ChevronDownIcon, CheckIcon, ChevronUpIcon } from "lucide-react";

function Select({
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Root>) {
  return <SelectPrimitive.Root data-slot="select" {...props} />;
}

function SelectGroup({
  className,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Group>) {
  return (
    <SelectPrimitive.Group
      data-slot="select-group"
      className={cn("approval:scroll-my-1 approval:p-1", className)}
      {...props}
    />
  );
}

function SelectValue({
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Value>) {
  return <SelectPrimitive.Value data-slot="select-value" {...props} />;
}

function SelectTrigger({
  className,
  size = "default",
  children,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Trigger> & {
  size?: "sm" | "default";
}) {
  return (
    <SelectPrimitive.Trigger
      data-slot="select-trigger"
      data-size={size}
      className={cn(
        "approval:flex approval:w-fit approval:items-center approval:justify-between approval:gap-1.5 approval:rounded-lg approval:border approval:border-input approval:bg-transparent approval:py-2 approval:pr-2 approval:pl-2.5 approval:text-sm approval:whitespace-nowrap approval:transition-colors approval:outline-none approval:select-none approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:disabled:cursor-not-allowed approval:disabled:opacity-50 approval:aria-invalid:border-destructive approval:aria-invalid:ring-3 approval:aria-invalid:ring-destructive/20 approval:data-placeholder:text-muted-foreground approval:data-[size=default]:h-8 approval:data-[size=sm]:h-7 approval:data-[size=sm]:rounded-[min(var(--radius-md),10px)] approval:*:data-[slot=select-value]:line-clamp-1 approval:*:data-[slot=select-value]:flex approval:*:data-[slot=select-value]:items-center approval:*:data-[slot=select-value]:gap-1.5 approval:dark:bg-input/30 approval:dark:hover:bg-input/50 approval:dark:aria-invalid:border-destructive/50 approval:dark:aria-invalid:ring-destructive/40 approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4",
        className,
      )}
      {...props}
    >
      {children}
      <SelectPrimitive.Icon asChild>
        <ChevronDownIcon className="approval:pointer-events-none approval:size-4 approval:text-muted-foreground" />
      </SelectPrimitive.Icon>
    </SelectPrimitive.Trigger>
  );
}

function SelectContent({
  className,
  children,
  position = "item-aligned",
  align = "center",
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Content>) {
  return (
    <SelectPrimitive.Portal>
      <SelectPrimitive.Content
        data-slot="select-content"
        data-align-trigger={position === "item-aligned"}
        className={cn(
          "approval:relative approval:z-50 approval:max-h-(--radix-select-content-available-height) approval:min-w-36 approval:origin-(--radix-select-content-transform-origin) approval:overflow-x-hidden approval:overflow-y-auto approval:rounded-lg approval:bg-popover approval:text-popover-foreground approval:shadow-md approval:ring-1 approval:ring-foreground/10 approval:duration-100 approval:data-[align-trigger=true]:animate-none approval:data-[side=bottom]:slide-in-from-top-2 approval:data-[side=left]:slide-in-from-right-2 approval:data-[side=right]:slide-in-from-left-2 approval:data-[side=top]:slide-in-from-bottom-2 approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-open:zoom-in-95 approval:data-closed:animate-out approval:data-closed:fade-out-0 approval:data-closed:zoom-out-95",
          position === "popper" &&
            "approval:data-[side=bottom]:translate-y-1 approval:data-[side=left]:-translate-x-1 approval:data-[side=right]:translate-x-1 approval:data-[side=top]:-translate-y-1",
          className,
        )}
        position={position}
        align={align}
        {...props}
      >
        <SelectScrollUpButton />
        <SelectPrimitive.Viewport
          data-position={position}
          className={cn(
            "approval:data-[position=popper]:h-(--radix-select-trigger-height) approval:data-[position=popper]:w-full approval:data-[position=popper]:min-w-(--radix-select-trigger-width)",
            position === "popper" && "approval:",
          )}
        >
          {children}
        </SelectPrimitive.Viewport>
        <SelectScrollDownButton />
      </SelectPrimitive.Content>
    </SelectPrimitive.Portal>
  );
}

function SelectLabel({
  className,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Label>) {
  return (
    <SelectPrimitive.Label
      data-slot="select-label"
      className={cn(
        "approval:px-1.5 approval:py-1 approval:text-xs approval:text-muted-foreground",
        className,
      )}
      {...props}
    />
  );
}

function SelectItem({
  className,
  children,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Item>) {
  return (
    <SelectPrimitive.Item
      data-slot="select-item"
      className={cn(
        "approval:relative approval:flex approval:w-full approval:cursor-default approval:items-center approval:gap-1.5 approval:rounded-md approval:py-1 approval:pr-8 approval:pl-1.5 approval:text-sm approval:outline-hidden approval:select-none approval:focus:bg-accent approval:focus:text-accent-foreground approval:not-data-[variant=destructive]:focus:**:text-accent-foreground approval:data-disabled:pointer-events-none approval:data-disabled:opacity-50 approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4 approval:*:[span]:last:flex approval:*:[span]:last:items-center approval:*:[span]:last:gap-2",
        className,
      )}
      {...props}
    >
      <span className="approval:pointer-events-none approval:absolute approval:right-2 approval:flex approval:size-4 approval:items-center approval:justify-center">
        <SelectPrimitive.ItemIndicator>
          <CheckIcon className="approval:pointer-events-none" />
        </SelectPrimitive.ItemIndicator>
      </span>
      <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
    </SelectPrimitive.Item>
  );
}

function SelectSeparator({
  className,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Separator>) {
  return (
    <SelectPrimitive.Separator
      data-slot="select-separator"
      className={cn(
        "approval:pointer-events-none approval:-mx-1 approval:my-1 approval:h-px approval:bg-border",
        className,
      )}
      {...props}
    />
  );
}

function SelectScrollUpButton({
  className,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.ScrollUpButton>) {
  return (
    <SelectPrimitive.ScrollUpButton
      data-slot="select-scroll-up-button"
      className={cn(
        "approval:z-10 approval:flex approval:cursor-default approval:items-center approval:justify-center approval:bg-popover approval:py-1 approval:[&_svg:not([class*=size-])]:size-4",
        className,
      )}
      {...props}
    >
      <ChevronUpIcon />
    </SelectPrimitive.ScrollUpButton>
  );
}

function SelectScrollDownButton({
  className,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.ScrollDownButton>) {
  return (
    <SelectPrimitive.ScrollDownButton
      data-slot="select-scroll-down-button"
      className={cn(
        "approval:z-10 approval:flex approval:cursor-default approval:items-center approval:justify-center approval:bg-popover approval:py-1 approval:[&_svg:not([class*=size-])]:size-4",
        className,
      )}
      {...props}
    >
      <ChevronDownIcon />
    </SelectPrimitive.ScrollDownButton>
  );
}

export {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectScrollDownButton,
  SelectScrollUpButton,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
};
