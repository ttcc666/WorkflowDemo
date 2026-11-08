import { useTheme } from "next-themes"
import { Toaster as Sonner, type ToasterProps } from "sonner"
import { CircleCheckIcon, InfoIcon, TriangleAlertIcon, OctagonXIcon, Loader2Icon } from "lucide-react"

const Toaster = ({ ...props }: ToasterProps) => {
  const { theme = "system" } = useTheme()

  return (
    <Sonner
      theme={theme as ToasterProps["theme"]}
      className="approval:toaster approval:group"
      icons={{
        success: (
          <CircleCheckIcon className="approval:size-4" />
        ),
        info: (
          <InfoIcon className="approval:size-4" />
        ),
        warning: (
          <TriangleAlertIcon className="approval:size-4" />
        ),
        error: (
          <OctagonXIcon className="approval:size-4" />
        ),
        loading: (
          <Loader2Icon className="approval:size-4 approval:animate-spin" />
        ),
      }}
      style={
        {
          "--normal-bg": "var(--popover)",
          "--normal-text": "var(--popover-foreground)",
          "--normal-border": "var(--border)",
          "--border-radius": "var(--radius)",
        } as React.CSSProperties
      }
      toastOptions={{
        classNames: {
          toast: "cn-toast",
        },
      }}
      {...props}
    />
  )
}

export { Toaster }
