"use client"

import * as React from "react"
import { cn } from "cn"
import { DropdownMenu as DropdownMenuPrimitive } from "radix-ui"
import { CheckIcon, ChevronRightIcon } from "lucide-react"

function DropdownMenu({
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Root>) {
  return <DropdownMenuPrimitive.Root data-slot="dropdown-menu" {...props} />
}

function DropdownMenuPortal({
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Portal>) {
  return (
    <DropdownMenuPrimitive.Portal data-slot="dropdown-menu-portal" {...props} />
  )
}

function DropdownMenuTrigger({
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Trigger>) {
  return (
    <DropdownMenuPrimitive.Trigger
      data-slot="dropdown-menu-trigger"
      {...props}
    />
  )
}

function DropdownMenuContent({
  className,
  align = "start",
  sideOffset = 4,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Content>) {
  return (
    <DropdownMenuPrimitive.Portal>
      <DropdownMenuPrimitive.Content
        data-slot="dropdown-menu-content"
        sideOffset={sideOffset}
        align={align}
        className={cn("approval-react approval:z-50 approval:max-h-(--radix-dropdown-menu-content-available-height) approval:w-(--radix-dropdown-menu-trigger-width) approval:min-w-32 approval:origin-(--radix-dropdown-menu-content-transform-origin) approval:overflow-x-hidden approval:overflow-y-auto approval:rounded-lg approval:bg-popover approval:p-1 approval:text-popover-foreground approval:shadow-md approval:ring-1 approval:ring-foreground/10 approval:duration-100 approval:data-[side=bottom]:slide-in-from-top-2 approval:data-[side=left]:slide-in-from-right-2 approval:data-[side=right]:slide-in-from-left-2 approval:data-[side=top]:slide-in-from-bottom-2 approval:data-[state=closed]:overflow-hidden approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-open:zoom-in-95 approval:data-closed:animate-out approval:data-closed:fade-out-0 approval:data-closed:zoom-out-95", className )}
        {...props}
      />
    </DropdownMenuPrimitive.Portal>
  )
}

function DropdownMenuGroup({
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Group>) {
  return (
    <DropdownMenuPrimitive.Group data-slot="dropdown-menu-group" {...props} />
  )
}

function DropdownMenuItem({
  className,
  inset,
  variant = "default",
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Item> & {
  inset?: boolean
  variant?: "default" | "destructive"
}) {
  return (
    <DropdownMenuPrimitive.Item
      data-slot="dropdown-menu-item"
      data-inset={inset}
      data-variant={variant}
      className={cn(
        "approval:group/dropdown-menu-item approval:relative approval:flex approval:cursor-default approval:items-center approval:gap-1.5 approval:rounded-md approval:px-1.5 approval:py-1 approval:text-sm approval:outline-hidden approval:select-none approval:focus:bg-accent approval:focus:text-accent-foreground approval:not-data-[variant=destructive]:focus:**:text-accent-foreground approval:data-inset:pl-7 approval:data-[variant=destructive]:text-destructive approval:data-[variant=destructive]:focus:bg-destructive/10 approval:data-[variant=destructive]:focus:text-destructive approval:dark:data-[variant=destructive]:focus:bg-destructive/20 approval:data-disabled:pointer-events-none approval:data-disabled:opacity-50 approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4 approval:data-[variant=destructive]:*:[svg]:text-destructive",
        className
      )}
      {...props}
    />
  )
}

function DropdownMenuCheckboxItem({
  className,
  children,
  checked,
  inset,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.CheckboxItem> & {
  inset?: boolean
}) {
  return (
    <DropdownMenuPrimitive.CheckboxItem
      data-slot="dropdown-menu-checkbox-item"
      data-inset={inset}
      className={cn(
        "approval:relative approval:flex approval:cursor-default approval:items-center approval:gap-1.5 approval:rounded-md approval:py-1 approval:pr-8 approval:pl-1.5 approval:text-sm approval:outline-hidden approval:select-none approval:focus:bg-accent approval:focus:text-accent-foreground approval:focus:**:text-accent-foreground approval:data-inset:pl-7 approval:data-disabled:pointer-events-none approval:data-disabled:opacity-50 approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4",
        className
      )}
      checked={checked}
      {...props}
    >
      <span
        className="approval:pointer-events-none approval:absolute approval:right-2 approval:flex approval:items-center approval:justify-center"
        data-slot="dropdown-menu-checkbox-item-indicator"
      >
        <DropdownMenuPrimitive.ItemIndicator>
          <CheckIcon
          />
        </DropdownMenuPrimitive.ItemIndicator>
      </span>
      {children}
    </DropdownMenuPrimitive.CheckboxItem>
  )
}

function DropdownMenuRadioGroup({
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.RadioGroup>) {
  return (
    <DropdownMenuPrimitive.RadioGroup
      data-slot="dropdown-menu-radio-group"
      {...props}
    />
  )
}

function DropdownMenuRadioItem({
  className,
  children,
  inset,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.RadioItem> & {
  inset?: boolean
}) {
  return (
    <DropdownMenuPrimitive.RadioItem
      data-slot="dropdown-menu-radio-item"
      data-inset={inset}
      className={cn(
        "approval:relative approval:flex approval:cursor-default approval:items-center approval:gap-1.5 approval:rounded-md approval:py-1 approval:pr-8 approval:pl-1.5 approval:text-sm approval:outline-hidden approval:select-none approval:focus:bg-accent approval:focus:text-accent-foreground approval:focus:**:text-accent-foreground approval:data-inset:pl-7 approval:data-disabled:pointer-events-none approval:data-disabled:opacity-50 approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4",
        className
      )}
      {...props}
    >
      <span
        className="approval:pointer-events-none approval:absolute approval:right-2 approval:flex approval:items-center approval:justify-center"
        data-slot="dropdown-menu-radio-item-indicator"
      >
        <DropdownMenuPrimitive.ItemIndicator>
          <CheckIcon
          />
        </DropdownMenuPrimitive.ItemIndicator>
      </span>
      {children}
    </DropdownMenuPrimitive.RadioItem>
  )
}

function DropdownMenuLabel({
  className,
  inset,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Label> & {
  inset?: boolean
}) {
  return (
    <DropdownMenuPrimitive.Label
      data-slot="dropdown-menu-label"
      data-inset={inset}
      className={cn(
        "approval:px-1.5 approval:py-1 approval:text-xs approval:font-medium approval:text-muted-foreground approval:data-inset:pl-7",
        className
      )}
      {...props}
    />
  )
}

function DropdownMenuSeparator({
  className,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Separator>) {
  return (
    <DropdownMenuPrimitive.Separator
      data-slot="dropdown-menu-separator"
      className={cn("approval:-mx-1 approval:my-1 approval:h-px approval:bg-border", className)}
      {...props}
    />
  )
}

function DropdownMenuShortcut({
  className,
  ...props
}: React.ComponentProps<"span">) {
  return (
    <span
      data-slot="dropdown-menu-shortcut"
      className={cn(
        "approval:ml-auto approval:text-xs approval:tracking-widest approval:text-muted-foreground approval:group-focus/dropdown-menu-item:text-accent-foreground",
        className
      )}
      {...props}
    />
  )
}

function DropdownMenuSub({
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Sub>) {
  return <DropdownMenuPrimitive.Sub data-slot="dropdown-menu-sub" {...props} />
}

function DropdownMenuSubTrigger({
  className,
  inset,
  children,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.SubTrigger> & {
  inset?: boolean
}) {
  return (
    <DropdownMenuPrimitive.SubTrigger
      data-slot="dropdown-menu-sub-trigger"
      data-inset={inset}
      className={cn(
        "approval:flex approval:cursor-default approval:items-center approval:gap-1.5 approval:rounded-md approval:px-1.5 approval:py-1 approval:text-sm approval:outline-hidden approval:select-none approval:focus:bg-accent approval:focus:text-accent-foreground approval:not-data-[variant=destructive]:focus:**:text-accent-foreground approval:data-inset:pl-7 approval:data-open:bg-accent approval:data-open:text-accent-foreground approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4",
        className
      )}
      {...props}
    >
      {children}
      <ChevronRightIcon className="approval:ml-auto" />
    </DropdownMenuPrimitive.SubTrigger>
  )
}

function DropdownMenuSubContent({
  className,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.SubContent>) {
  return (
    <DropdownMenuPrimitive.SubContent
      data-slot="dropdown-menu-sub-content"
      className={cn("approval-react approval:z-50 approval:min-w-[96px] approval:origin-(--radix-dropdown-menu-content-transform-origin) approval:overflow-hidden approval:rounded-lg approval:bg-popover approval:p-1 approval:text-popover-foreground approval:shadow-lg approval:ring-1 approval:ring-foreground/10 approval:duration-100 approval:data-[side=bottom]:slide-in-from-top-2 approval:data-[side=left]:slide-in-from-right-2 approval:data-[side=right]:slide-in-from-left-2 approval:data-[side=top]:slide-in-from-bottom-2 approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-open:zoom-in-95 approval:data-closed:animate-out approval:data-closed:fade-out-0 approval:data-closed:zoom-out-95", className )}
      {...props}
    />
  )
}

export {
  DropdownMenu,
  DropdownMenuPortal,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuItem,
  DropdownMenuCheckboxItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuShortcut,
  DropdownMenuSub,
  DropdownMenuSubTrigger,
  DropdownMenuSubContent,
}
