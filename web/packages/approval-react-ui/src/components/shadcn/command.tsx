"use client"

import * as React from "react"
import { Command as CommandPrimitive } from "cmdk"
import { cn } from "cn"

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "./dialog"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from "./input-group"
import { SearchIcon, CheckIcon } from "lucide-react"

function Command({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive>) {
  return (
    <CommandPrimitive
      data-slot="command"
      className={cn(
        "approval:flex approval:size-full approval:flex-col approval:overflow-hidden approval:rounded-xl! approval:bg-popover approval:p-1 approval:text-popover-foreground",
        className
      )}
      {...props}
    />
  )
}

function CommandDialog({
  title = "Command Palette",
  description = "Search for a command to run...",
  children,
  className,
  showCloseButton = false,
  ...props
}: React.ComponentProps<typeof Dialog> & {
  title?: string
  description?: string
  className?: string
  showCloseButton?: boolean
}) {
  return (
    <Dialog {...props}><DialogContent
        className={cn(
          "approval:top-1/3 approval:translate-y-0 approval:overflow-hidden approval:rounded-xl! approval:p-0",
          className
        )}
        showCloseButton={showCloseButton}
      >
      <DialogHeader className="approval:sr-only">
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>
        {children}
      </DialogContent>
    </Dialog>
  )
}

function CommandInput({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Input>) {
  return (
    <div
      data-slot="command-input-wrapper"
      className="approval:p-1 approval:pb-0"
    >
      <InputGroup className="approval:h-8! approval:rounded-lg! approval:border-input/30 approval:bg-input/30 approval:shadow-none! approval:*:data-[slot=input-group-addon]:pl-2!">
        <CommandPrimitive.Input
          data-slot="command-input"
          className={cn(
            "approval:w-full approval:text-sm approval:outline-hidden approval:disabled:cursor-not-allowed approval:disabled:opacity-50",
            className,
          )}
          {...props}
          asChild
        >
          <InputGroupInput
            {...(props["aria-expanded"] === undefined
              ? {}
              : {
                  "aria-expanded": props["aria-expanded"],
                  ...(props["aria-expanded"]
                    ? {}
                    : {
                        "aria-controls": undefined,
                        "aria-activedescendant": undefined,
                      }),
                })}
          />
        </CommandPrimitive.Input>
        <InputGroupAddon>
          <SearchIcon className="approval:size-4 approval:shrink-0 approval:opacity-50" />
        </InputGroupAddon>
      </InputGroup>
    </div>
  )
}

function CommandList({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.List>) {
  return (
    <CommandPrimitive.List
      data-slot="command-list"
      className={cn(
        "approval:no-scrollbar approval:max-h-72 approval:scroll-py-1 approval:overflow-x-hidden approval:overflow-y-auto approval:outline-none",
        className
      )}
      {...props}
    />
  )
}

function CommandEmpty({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Empty>) {
  return (
    <CommandPrimitive.Empty
      data-slot="command-empty"
      className={cn("approval:py-6 approval:text-center approval:text-sm", className)}
      {...props}
    />
  )
}

function CommandGroup({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Group>) {
  return (
    <CommandPrimitive.Group
      data-slot="command-group"
      className={cn(
        "approval:overflow-hidden approval:p-1 approval:text-foreground approval:**:[[cmdk-group-heading]]:px-2 approval:**:[[cmdk-group-heading]]:py-1.5 approval:**:[[cmdk-group-heading]]:text-xs approval:**:[[cmdk-group-heading]]:font-medium approval:**:[[cmdk-group-heading]]:text-muted-foreground",
        className
      )}
      {...props}
    />
  )
}

function CommandSeparator({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Separator>) {
  return (
    <CommandPrimitive.Separator
      data-slot="command-separator"
      className={cn("approval:-mx-1 approval:h-px approval:bg-border", className)}
      {...props}
    />
  )
}

function CommandItem({
  className,
  children,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Item>) {
  return (
    <CommandPrimitive.Item
      data-slot="command-item"
      className={cn(
        "approval:group/command-item approval:relative approval:flex approval:cursor-default approval:items-center approval:gap-2 approval:rounded-sm approval:px-2 approval:py-1.5 approval:text-sm approval:outline-hidden approval:select-none approval:in-data-[slot=dialog-content]:rounded-lg! approval:data-[disabled=true]:pointer-events-none approval:data-[disabled=true]:opacity-50 approval:data-selected:bg-muted approval:data-selected:text-foreground approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4 approval:data-selected:*:[svg]:text-foreground",
        className
      )}
      {...props}
    >
      {children}
      <CheckIcon className="approval:ml-auto approval:opacity-0 approval:group-has-data-[slot=command-shortcut]/command-item:hidden approval:group-data-[checked=true]/command-item:opacity-100" />
    </CommandPrimitive.Item>
  )
}

function CommandShortcut({
  className,
  ...props
}: React.ComponentProps<"span">) {
  return (
    <span
      data-slot="command-shortcut"
      className={cn(
        "approval:ml-auto approval:text-xs approval:tracking-widest approval:text-muted-foreground approval:group-data-selected/command-item:text-foreground",
        className
      )}
      {...props}
    />
  )
}

export {
  Command,
  CommandDialog,
  CommandInput,
  CommandList,
  CommandEmpty,
  CommandGroup,
  CommandItem,
  CommandShortcut,
  CommandSeparator,
}
