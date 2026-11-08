import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"

function Empty({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="empty"
      className={cn(
        "approval:flex approval:w-full approval:min-w-0 approval:flex-1 approval:flex-col approval:items-center approval:justify-center approval:gap-4 approval:rounded-xl approval:border-dashed approval:p-6 approval:text-center approval:text-balance",
        className
      )}
      {...props}
    />
  )
}

function EmptyHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="empty-header"
      className={cn("approval:flex approval:max-w-sm approval:flex-col approval:items-center approval:gap-2", className)}
      {...props}
    />
  )
}

const emptyMediaVariants = cva(
  "approval:mb-2 approval:flex approval:shrink-0 approval:items-center approval:justify-center approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default: "approval:bg-transparent",
        icon: "approval:flex approval:size-8 approval:shrink-0 approval:items-center approval:justify-center approval:rounded-lg approval:bg-muted approval:text-foreground approval:[&_svg:not([class*=size-])]:size-4",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

function EmptyMedia({
  className,
  variant = "default",
  ...props
}: React.ComponentProps<"div"> & VariantProps<typeof emptyMediaVariants>) {
  return (
    <div
      data-slot="empty-icon"
      data-variant={variant}
      className={cn(emptyMediaVariants({ variant, className }))}
      {...props}
    />
  )
}

function EmptyTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="empty-title"
      className={cn(
        "approval:text-sm approval:font-medium approval:tracking-tight",
        className
      )}
      {...props}
    />
  )
}

function EmptyDescription({ className, ...props }: React.ComponentProps<"p">) {
  return (
    <div
      data-slot="empty-description"
      className={cn(
        "approval:text-sm/relaxed approval:text-muted-foreground approval:[&>a]:underline approval:[&>a]:underline-offset-4 approval:[&>a:hover]:text-primary",
        className
      )}
      {...props}
    />
  )
}

function EmptyContent({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="empty-content"
      className={cn(
        "approval:flex approval:w-full approval:max-w-sm approval:min-w-0 approval:flex-col approval:items-center approval:gap-2.5 approval:text-sm approval:text-balance",
        className
      )}
      {...props}
    />
  )
}

export {
  Empty,
  EmptyHeader,
  EmptyTitle,
  EmptyDescription,
  EmptyContent,
  EmptyMedia,
}
