import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"

import { Button } from "./button"
import { Input } from "./input"
import { Textarea } from "./textarea"

function InputGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="input-group"
      role="group"
      className={cn(
        "approval:group/input-group approval:relative approval:flex approval:h-8 approval:w-full approval:min-w-0 approval:items-center approval:rounded-lg approval:border approval:border-input approval:transition-colors approval:outline-none approval:in-data-[slot=combobox-content]:focus-within:border-inherit approval:in-data-[slot=combobox-content]:focus-within:ring-0 approval:has-disabled:bg-input/50 approval:has-disabled:opacity-50 approval:has-[[data-slot=input-group-control]:focus-visible]:border-ring approval:has-[[data-slot=input-group-control]:focus-visible]:ring-3 approval:has-[[data-slot=input-group-control]:focus-visible]:ring-ring/50 approval:has-[[data-slot][aria-invalid=true]]:border-destructive approval:has-[[data-slot][aria-invalid=true]]:ring-3 approval:has-[[data-slot][aria-invalid=true]]:ring-destructive/20 approval:has-[>[data-align=block-end]]:h-auto approval:has-[>[data-align=block-end]]:flex-col approval:has-[>[data-align=block-start]]:h-auto approval:has-[>[data-align=block-start]]:flex-col approval:has-[>textarea]:h-auto approval:dark:bg-input/30 approval:dark:has-disabled:bg-input/80 approval:dark:has-[[data-slot][aria-invalid=true]]:ring-destructive/40 approval:has-[>[data-align=block-end]]:[&>input]:pt-3 approval:has-[>[data-align=block-start]]:[&>input]:pb-3 approval:has-[>[data-align=inline-end]]:[&>input]:pr-1.5 approval:has-[>[data-align=inline-start]]:[&>input]:pl-1.5",
        className
      )}
      {...props}
    />
  )
}

const inputGroupAddonVariants = cva(
  "approval:flex approval:h-auto approval:cursor-text approval:items-center approval:justify-center approval:gap-2 approval:py-1.5 approval:text-sm approval:font-medium approval:text-muted-foreground approval:select-none approval:group-data-[disabled=true]/input-group:opacity-50 approval:[&>kbd]:rounded-[calc(var(--radius)-5px)] approval:[&>svg:not([class*=size-])]:size-4",
  {
    variants: {
      align: {
        "inline-start":
          "approval:order-first approval:pl-2 approval:has-[>button]:ml-[-0.3rem] approval:has-[>kbd]:ml-[-0.15rem]",
        "inline-end":
          "approval:order-last approval:pr-2 approval:has-[>button]:mr-[-0.3rem] approval:has-[>kbd]:mr-[-0.15rem]",
        "block-start":
          "approval:order-first approval:w-full approval:justify-start approval:px-2.5 approval:pt-2 approval:group-has-[>input]/input-group:pt-2 approval:[.border-b]:pb-2",
        "block-end":
          "approval:order-last approval:w-full approval:justify-start approval:px-2.5 approval:pb-2 approval:group-has-[>input]/input-group:pb-2 approval:[.border-t]:pt-2",
      },
    },
    defaultVariants: {
      align: "inline-start",
    },
  }
)

function InputGroupAddon({
  className,
  align = "inline-start",
  ...props
}: React.ComponentProps<"div"> & VariantProps<typeof inputGroupAddonVariants>) {
  return (
    <div
      role="group"
      data-slot="input-group-addon"
      data-align={align}
      className={cn(inputGroupAddonVariants({ align }), className)}
      onClick={(e) => {
        if ((e.target as HTMLElement).closest("button")) {
          return
        }
        e.currentTarget.parentElement?.querySelector("input")?.focus()
      }}
      {...props}
    />
  )
}

const inputGroupButtonVariants = cva(
  "approval:flex approval:items-center approval:gap-2 approval:text-sm approval:shadow-none",
  {
    variants: {
      size: {
        xs: "approval:h-6 approval:gap-1 approval:rounded-[calc(var(--radius)-3px)] approval:px-1.5 approval:[&>svg:not([class*=size-])]:size-3.5",
        sm: "approval:",
        "icon-xs":
          "approval:size-6 approval:rounded-[calc(var(--radius)-3px)] approval:p-0 approval:has-[>svg]:p-0",
        "icon-sm": "approval:size-8 approval:p-0 approval:has-[>svg]:p-0",
      },
    },
    defaultVariants: {
      size: "xs",
    },
  }
)

function InputGroupButton({
  className,
  type = "button",
  variant = "ghost",
  size = "xs",
  ...props
}: Omit<React.ComponentProps<typeof Button>, "size"> &
  VariantProps<typeof inputGroupButtonVariants>) {
  return (
    <Button
      type={type}
      data-size={size}
      variant={variant}
      className={cn(inputGroupButtonVariants({ size }), className)}
      {...props}
    />
  )
}

function InputGroupText({ className, ...props }: React.ComponentProps<"span">) {
  return (
    <span
      className={cn(
        "approval:flex approval:items-center approval:gap-2 approval:text-sm approval:text-muted-foreground approval:[&_svg]:pointer-events-none approval:[&_svg:not([class*=size-])]:size-4",
        className
      )}
      {...props}
    />
  )
}

function InputGroupInput({
  className,
  ...props
}: React.ComponentProps<"input">) {
  return (
    <Input
      data-slot="input-group-control"
      className={cn(
        "approval:flex-1 approval:rounded-none approval:border-0 approval:bg-transparent approval:shadow-none approval:ring-0 approval:focus-visible:ring-0 approval:disabled:bg-transparent approval:aria-invalid:ring-0 approval:dark:bg-transparent approval:dark:disabled:bg-transparent",
        className
      )}
      {...props}
    />
  )
}

function InputGroupTextarea({
  className,
  ...props
}: React.ComponentProps<"textarea">) {
  return (
    <Textarea
      data-slot="input-group-control"
      className={cn(
        "approval:flex-1 approval:resize-none approval:rounded-none approval:border-0 approval:bg-transparent approval:py-2 approval:shadow-none approval:ring-0 approval:focus-visible:ring-0 approval:disabled:bg-transparent approval:aria-invalid:ring-0 approval:dark:bg-transparent approval:dark:disabled:bg-transparent",
        className
      )}
      {...props}
    />
  )
}

export {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupText,
  InputGroupInput,
  InputGroupTextarea,
}
