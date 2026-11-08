import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"
import { Slot } from "radix-ui"

const buttonVariants = cva(
  "approval:group/button approval:inline-flex approval:shrink-0 approval:items-center approval:justify-center approval:rounded-lg approval:border approval:border-transparent approval:bg-clip-padding approval:text-sm approval:font-medium approval:whitespace-nowrap approval:transition-all approval:outline-none approval:select-none approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:active:not-aria-[haspopup]:translate-y-px approval:disabled:pointer-events-none approval:disabled:opacity-50 approval:aria-invalid:border-destructive approval:aria-invalid:ring-3 approval:aria-invalid:ring-destructive/20 approval:dark:aria-invalid:border-destructive/50 approval:dark:aria-invalid:ring-destructive/40 approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4",
  {
    variants: {
      variant: {
        default: "approval:bg-primary approval:text-primary-foreground approval:hover:bg-primary/80",
        outline:
          "approval:border-border approval:bg-background approval:hover:bg-muted approval:hover:text-foreground approval:aria-expanded:bg-muted approval:aria-expanded:text-foreground approval:dark:border-input approval:dark:bg-input/30 approval:dark:hover:bg-input/50",
        secondary:
          "approval:bg-secondary approval:text-secondary-foreground approval:hover:bg-[color-mix(in_oklch,var(--secondary),var(--foreground)_5%)] approval:aria-expanded:bg-secondary approval:aria-expanded:text-secondary-foreground",
        ghost:
          "approval:hover:bg-muted approval:hover:text-foreground approval:aria-expanded:bg-muted approval:aria-expanded:text-foreground approval:dark:hover:bg-muted/50",
        destructive:
          "approval:bg-destructive/10 approval:text-destructive approval:hover:bg-destructive/20 approval:focus-visible:border-destructive/40 approval:focus-visible:ring-destructive/20 approval:dark:bg-destructive/20 approval:dark:hover:bg-destructive/30 approval:dark:focus-visible:ring-destructive/40",
        link: "approval:text-primary approval:underline-offset-4 approval:hover:underline",
      },
      size: {
        default:
          "approval:h-8 approval:gap-1.5 approval:px-2.5 approval:has-data-[icon=inline-end]:pr-2 approval:has-data-[icon=inline-start]:pl-2",
        xs: "approval:h-6 approval:gap-1 approval:rounded-[min(var(--radius-md),10px)] approval:px-2 approval:text-xs approval:in-data-[slot=button-group]:rounded-lg approval:has-data-[icon=inline-end]:pr-1.5 approval:has-data-[icon=inline-start]:pl-1.5 approval:[&_svg:not([class*=size-])]:size-3",
        sm: "approval:h-7 approval:gap-1 approval:rounded-[min(var(--radius-md),12px)] approval:px-2.5 approval:text-[0.8rem] approval:in-data-[slot=button-group]:rounded-lg approval:has-data-[icon=inline-end]:pr-1.5 approval:has-data-[icon=inline-start]:pl-1.5 approval:[&_svg:not([class*=size-])]:size-3.5",
        lg: "approval:h-9 approval:gap-1.5 approval:px-2.5 approval:has-data-[icon=inline-end]:pr-2 approval:has-data-[icon=inline-start]:pl-2",
        icon: "approval:size-8",
        "icon-xs":
          "approval:size-6 approval:rounded-[min(var(--radius-md),10px)] approval:in-data-[slot=button-group]:rounded-lg approval:[&_svg:not([class*=size-])]:size-3",
        "icon-sm":
          "approval:size-7 approval:rounded-[min(var(--radius-md),12px)] approval:in-data-[slot=button-group]:rounded-lg",
        "icon-lg": "approval:size-9",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

function Button({
  className,
  variant = "default",
  size = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean
  }) {
  const Comp = asChild ? Slot.Root : "button"

  return (
    <Comp
      data-slot="button"
      data-variant={variant}
      data-size={size}
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    />
  )
}

export { Button, buttonVariants }
