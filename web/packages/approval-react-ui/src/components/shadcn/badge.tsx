import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "cn";
import { Slot } from "radix-ui";

const badgeVariants = cva(
  "approval:group/badge approval:inline-flex approval:h-5 approval:w-fit approval:shrink-0 approval:items-center approval:justify-center approval:gap-1 approval:overflow-hidden approval:rounded-4xl approval:border approval:border-transparent approval:px-2 approval:py-0.5 approval:text-xs approval:font-medium approval:whitespace-nowrap approval:transition-all approval:focus-visible:border-ring approval:focus-visible:ring-[3px] approval:focus-visible:ring-ring/50 approval:has-data-[icon=inline-end]:pr-1.5 approval:has-data-[icon=inline-start]:pl-1.5 approval:aria-invalid:border-destructive approval:aria-invalid:ring-destructive/20 approval:dark:aria-invalid:ring-destructive/40 approval:[&>svg]:pointer-events-none approval:[&>svg]:size-3!",
  {
    variants: {
      variant: {
        success: "approval:bg-success approval:text-success-foreground",
        warning: "approval:bg-warning approval:text-warning-foreground",
        info: "approval:bg-info approval:text-info-foreground",
        default:
          "approval:bg-primary approval:text-primary-foreground approval:[a]:hover:bg-primary/80",
        secondary:
          "approval:bg-secondary approval:text-secondary-foreground approval:[a]:hover:bg-secondary/80",
        destructive:
          "approval:bg-destructive/10 approval:text-destructive approval:focus-visible:ring-destructive/20 approval:dark:bg-destructive/20 approval:dark:focus-visible:ring-destructive/40 approval:[a]:hover:bg-destructive/20",
        outline:
          "approval:border-border approval:text-foreground approval:[a]:hover:bg-muted approval:[a]:hover:text-muted-foreground",
        ghost:
          "approval:hover:bg-muted approval:hover:text-muted-foreground approval:dark:hover:bg-muted/50",
        link: "approval:text-primary approval:underline-offset-4 approval:hover:underline",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
);

function Badge({
  className,
  variant = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"span"> &
  VariantProps<typeof badgeVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "span";

  return (
    <Comp
      data-slot="badge"
      data-variant={variant}
      className={cn(badgeVariants({ variant }), className)}
      {...props}
    />
  );
}

export { Badge, badgeVariants };
