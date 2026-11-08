import { cn } from "cn"

function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton"
      className={cn("approval:animate-pulse approval:rounded-md approval:bg-muted", className)}
      {...props}
    />
  )
}

export { Skeleton }
