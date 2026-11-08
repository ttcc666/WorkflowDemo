import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"
import { Slot } from "radix-ui"

import { useIsMobile } from "../../hooks/use-mobile"
import { Button } from "./button"
import { Input } from "./input"
import { Separator } from "./separator"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "./sheet"
import { Skeleton } from "./skeleton"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "./tooltip"
import { PanelLeftIcon } from "lucide-react"

const SIDEBAR_COOKIE_NAME = "sidebar_state"
const SIDEBAR_COOKIE_MAX_AGE = 60 * 60 * 24 * 7
const SIDEBAR_WIDTH = "16rem"
const SIDEBAR_WIDTH_MOBILE = "18rem"
const SIDEBAR_WIDTH_ICON = "3rem"
const SIDEBAR_KEYBOARD_SHORTCUT = "b"

type SidebarContextProps = {
  state: "expanded" | "collapsed"
  open: boolean
  setOpen: (open: boolean) => void
  openMobile: boolean
  setOpenMobile: (open: boolean) => void
  isMobile: boolean
  toggleSidebar: () => void
}

const SidebarContext = React.createContext<SidebarContextProps | null>(null)

function useSidebar() {
  const context = React.useContext(SidebarContext)
  if (!context) {
    throw new Error("useSidebar must be used within a SidebarProvider.")
  }

  return context
}

function SidebarProvider({
  defaultOpen = true,
  open: openProp,
  onOpenChange: setOpenProp,
  className,
  style,
  children,
  ...props
}: React.ComponentProps<"div"> & {
  defaultOpen?: boolean
  open?: boolean
  onOpenChange?: (open: boolean) => void
}) {
  const isMobile = useIsMobile()
  const [openMobile, setOpenMobile] = React.useState(false)

  // This is the internal state of the sidebar.
  // We use openProp and setOpenProp for control from outside the component.
  const [_open, _setOpen] = React.useState(defaultOpen)
  const open = openProp ?? _open
  const setOpen = React.useCallback(
    (value: boolean | ((value: boolean) => boolean)) => {
      const openState = typeof value === "function" ? value(open) : value
      if (setOpenProp) {
        setOpenProp(openState)
      } else {
        _setOpen(openState)
      }

      // This sets the cookie to keep the sidebar state.
      document.cookie = `${SIDEBAR_COOKIE_NAME}=${openState}; path=/; max-age=${SIDEBAR_COOKIE_MAX_AGE}`
    },
    [setOpenProp, open]
  )

  // Helper to toggle the sidebar.
  const toggleSidebar = React.useCallback(() => {
    return isMobile ? setOpenMobile((open) => !open) : setOpen((open) => !open)
  }, [isMobile, setOpen, setOpenMobile])

  // Adds a keyboard shortcut to toggle the sidebar.
  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (
        event.key === SIDEBAR_KEYBOARD_SHORTCUT &&
        (event.metaKey || event.ctrlKey)
      ) {
        event.preventDefault()
        toggleSidebar()
      }
    }

    window.addEventListener("keydown", handleKeyDown)
    return () => window.removeEventListener("keydown", handleKeyDown)
  }, [toggleSidebar])

  // We add a state so that we can do data-state="expanded" or "collapsed".
  // This makes it easier to style the sidebar with Tailwind classes.
  const state = open ? "expanded" : "collapsed"

  const contextValue = React.useMemo<SidebarContextProps>(
    () => ({
      state,
      open,
      setOpen,
      isMobile,
      openMobile,
      setOpenMobile,
      toggleSidebar,
    }),
    [state, open, setOpen, isMobile, openMobile, setOpenMobile, toggleSidebar]
  )

  return (
    <SidebarContext.Provider value={contextValue}>
      <div
        data-slot="sidebar-wrapper"
        style={
          {
            "--sidebar-width": SIDEBAR_WIDTH,
            "--sidebar-width-icon": SIDEBAR_WIDTH_ICON,
            ...style,
          } as React.CSSProperties
        }
        className={cn(
          "approval:group/sidebar-wrapper approval:flex approval:min-h-svh approval:w-full approval:has-data-[variant=inset]:bg-sidebar",
          className
        )}
        {...props}
      >
        {children}
      </div>
    </SidebarContext.Provider>
  )
}

function Sidebar({
  side = "left",
  variant = "sidebar",
  collapsible = "offcanvas",
  className,
  children,
  dir,
  ...props
}: React.ComponentProps<"div"> & {
  side?: "left" | "right"
  variant?: "sidebar" | "floating" | "inset"
  collapsible?: "offcanvas" | "icon" | "none"
}) {
  const { isMobile, state, openMobile, setOpenMobile } = useSidebar()

  if (collapsible === "none") {
    return (
      <div
        data-slot="sidebar"
        className={cn(
          "approval:flex approval:h-full approval:w-(--sidebar-width) approval:flex-col approval:bg-sidebar approval:text-sidebar-foreground",
          className
        )}
        {...props}
      >
        {children}
      </div>
    )
  }

  if (isMobile) {
    return (
      <Sheet open={openMobile} onOpenChange={setOpenMobile} {...props}>
        <SheetContent
          dir={dir}
          data-sidebar="sidebar"
          data-slot="sidebar"
          data-mobile="true"
          className="approval:w-(--sidebar-width) approval:bg-sidebar approval:p-0 approval:text-sidebar-foreground approval:[&>button]:hidden"
          style={
            {
              "--sidebar-width": SIDEBAR_WIDTH_MOBILE,
            } as React.CSSProperties
          }
          side={side}
        >
          <SheetHeader className="approval:sr-only">
            <SheetTitle>导航菜单</SheetTitle>
            <SheetDescription>切换审批中心功能页面。</SheetDescription>
          </SheetHeader>
          <div className="approval:flex approval:h-full approval:w-full approval:flex-col">{children}</div>
        </SheetContent>
      </Sheet>
    )
  }

  return (
    <div
      className="approval:group approval:peer approval:hidden approval:text-sidebar-foreground approval:md:block"
      data-state={state}
      data-collapsible={state === "collapsed" ? collapsible : ""}
      data-variant={variant}
      data-side={side}
      data-slot="sidebar"
    >
      {/* This is what handles the sidebar gap on desktop */}
      <div
        data-slot="sidebar-gap"
        className={cn(
          "approval:relative approval:w-(--sidebar-width) approval:bg-transparent approval:transition-[width] approval:duration-200 approval:ease-linear",
          "approval:group-data-[collapsible=offcanvas]:w-0",
          "approval:group-data-[side=right]:rotate-180",
          variant === "floating" || variant === "inset"
            ? "approval:group-data-[collapsible=icon]:w-[calc(var(--sidebar-width-icon)+(--spacing(4)))]"
            : "approval:group-data-[collapsible=icon]:w-(--sidebar-width-icon)"
        )}
      />
      <div
        data-slot="sidebar-container"
        data-side={side}
        className={cn(
          "approval:fixed approval:inset-y-0 approval:z-10 approval:hidden approval:h-svh approval:w-(--sidebar-width) approval:transition-[left,right,width] approval:duration-200 approval:ease-linear approval:data-[side=left]:left-0 approval:data-[side=left]:group-data-[collapsible=offcanvas]:left-[calc(var(--sidebar-width)*-1)] approval:data-[side=right]:right-0 approval:data-[side=right]:group-data-[collapsible=offcanvas]:right-[calc(var(--sidebar-width)*-1)] approval:md:flex",
          // Adjust the padding for floating and inset variants.
          variant === "floating" || variant === "inset"
            ? "approval:p-2 approval:group-data-[collapsible=icon]:w-[calc(var(--sidebar-width-icon)+(--spacing(4))+2px)]"
            : "approval:group-data-[collapsible=icon]:w-(--sidebar-width-icon) approval:group-data-[side=left]:border-r approval:group-data-[side=right]:border-l",
          className
        )}
        {...props}
      >
        <div
          data-sidebar="sidebar"
          data-slot="sidebar-inner"
          className="approval:flex approval:size-full approval:flex-col approval:bg-sidebar approval:group-data-[variant=floating]:rounded-lg approval:group-data-[variant=floating]:shadow-sm approval:group-data-[variant=floating]:ring-1 approval:group-data-[variant=floating]:ring-sidebar-border"
        >
          {children}
        </div>
      </div>
    </div>
  )
}

function SidebarTrigger({
  className,
  onClick,
  ...props
}: React.ComponentProps<typeof Button>) {
  const { toggleSidebar } = useSidebar()

  return (
    <Button
      data-sidebar="trigger"
      data-slot="sidebar-trigger"
      variant="ghost"
      size="icon-sm"
      className={cn(className)}
      onClick={(event) => {
        onClick?.(event)
        toggleSidebar()
      }}
      {...props}
    >
      <PanelLeftIcon />
      <span className="approval:sr-only">切换导航菜单</span>
    </Button>
  )
}

function SidebarRail({ className, ...props }: React.ComponentProps<"button">) {
  const { toggleSidebar } = useSidebar()

  return (
    <button
      data-sidebar="rail"
      data-slot="sidebar-rail"
      aria-label="切换导航菜单"
      tabIndex={-1}
      onClick={toggleSidebar}
      title="切换导航菜单"
      className={cn(
        "approval:absolute approval:inset-y-0 approval:z-20 approval:hidden approval:w-4 approval:transition-all approval:ease-linear approval:group-data-[side=left]:-right-4 approval:group-data-[side=right]:left-0 approval:after:absolute approval:after:inset-y-0 approval:after:start-1/2 approval:after:w-[2px] approval:hover:after:bg-sidebar-border approval:sm:flex approval:ltr:-translate-x-1/2 approval:rtl:-translate-x-1/2",
        "approval:in-data-[side=left]:cursor-w-resize approval:in-data-[side=right]:cursor-e-resize",
        "approval:[[data-side=left][data-state=collapsed]_&]:cursor-e-resize approval:[[data-side=right][data-state=collapsed]_&]:cursor-w-resize",
        "approval:group-data-[collapsible=offcanvas]:translate-x-0 approval:group-data-[collapsible=offcanvas]:after:left-full approval:hover:group-data-[collapsible=offcanvas]:bg-sidebar",
        "approval:[[data-side=left][data-collapsible=offcanvas]_&]:-right-2",
        "approval:[[data-side=right][data-collapsible=offcanvas]_&]:-left-2",
        className
      )}
      {...props}
    />
  )
}

function SidebarInset({ className, ...props }: React.ComponentProps<"main">) {
  return (
    <main
      data-slot="sidebar-inset"
      className={cn(
        "approval:relative approval:flex approval:w-full approval:flex-1 approval:flex-col approval:bg-background approval:md:peer-data-[variant=inset]:m-2 approval:md:peer-data-[variant=inset]:ml-0 approval:md:peer-data-[variant=inset]:rounded-xl approval:md:peer-data-[variant=inset]:shadow-sm approval:md:peer-data-[variant=inset]:peer-data-[state=collapsed]:ml-2",
        className
      )}
      {...props}
    />
  )
}

function SidebarInput({
  className,
  ...props
}: React.ComponentProps<typeof Input>) {
  return (
    <Input
      data-slot="sidebar-input"
      data-sidebar="input"
      className={cn("approval:h-8 approval:w-full approval:bg-background approval:shadow-none", className)}
      {...props}
    />
  )
}

function SidebarHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sidebar-header"
      data-sidebar="header"
      className={cn("approval:flex approval:flex-col approval:gap-2 approval:p-2", className)}
      {...props}
    />
  )
}

function SidebarFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sidebar-footer"
      data-sidebar="footer"
      className={cn("approval:flex approval:flex-col approval:gap-2 approval:p-2", className)}
      {...props}
    />
  )
}

function SidebarSeparator({
  className,
  ...props
}: React.ComponentProps<typeof Separator>) {
  return (
    <Separator
      data-slot="sidebar-separator"
      data-sidebar="separator"
      className={cn("approval:mx-2 approval:w-auto approval:bg-sidebar-border", className)}
      {...props}
    />
  )
}

function SidebarContent({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sidebar-content"
      data-sidebar="content"
      className={cn(
        "approval:no-scrollbar approval:flex approval:min-h-0 approval:flex-1 approval:flex-col approval:gap-0 approval:overflow-auto approval:group-data-[collapsible=icon]:overflow-hidden",
        className
      )}
      {...props}
    />
  )
}

function SidebarGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sidebar-group"
      data-sidebar="group"
      className={cn("approval:relative approval:flex approval:w-full approval:min-w-0 approval:flex-col approval:p-2", className)}
      {...props}
    />
  )
}

function SidebarGroupLabel({
  className,
  asChild = false,
  ...props
}: React.ComponentProps<"div"> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "div"

  return (
    <Comp
      data-slot="sidebar-group-label"
      data-sidebar="group-label"
      className={cn(
        "approval:flex approval:h-8 approval:shrink-0 approval:items-center approval:rounded-md approval:px-2 approval:text-xs approval:font-medium approval:text-sidebar-foreground/70 approval:ring-sidebar-ring approval:outline-hidden approval:transition-[margin,opacity] approval:duration-200 approval:ease-linear approval:group-data-[collapsible=icon]:-mt-8 approval:group-data-[collapsible=icon]:opacity-0 approval:focus-visible:ring-2 approval:[&>svg]:size-4 approval:[&>svg]:shrink-0",
        className
      )}
      {...props}
    />
  )
}

function SidebarGroupAction({
  className,
  asChild = false,
  ...props
}: React.ComponentProps<"button"> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "button"

  return (
    <Comp
      data-slot="sidebar-group-action"
      data-sidebar="group-action"
      className={cn(
        "approval:absolute approval:top-3.5 approval:right-3 approval:flex approval:aspect-square approval:w-5 approval:items-center approval:justify-center approval:rounded-md approval:p-0 approval:text-sidebar-foreground approval:ring-sidebar-ring approval:outline-hidden approval:transition-transform approval:group-data-[collapsible=icon]:hidden approval:after:absolute approval:after:-inset-2 approval:hover:bg-sidebar-accent approval:hover:text-sidebar-accent-foreground approval:focus-visible:ring-2 approval:md:after:hidden approval:[&>svg]:size-4 approval:[&>svg]:shrink-0",
        className
      )}
      {...props}
    />
  )
}

function SidebarGroupContent({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sidebar-group-content"
      data-sidebar="group-content"
      className={cn("approval:w-full approval:text-sm", className)}
      {...props}
    />
  )
}

function SidebarMenu({ className, ...props }: React.ComponentProps<"ul">) {
  return (
    <ul
      data-slot="sidebar-menu"
      data-sidebar="menu"
      className={cn("approval:flex approval:w-full approval:min-w-0 approval:flex-col approval:gap-0", className)}
      {...props}
    />
  )
}

function SidebarMenuItem({ className, ...props }: React.ComponentProps<"li">) {
  return (
    <li
      data-slot="sidebar-menu-item"
      data-sidebar="menu-item"
      className={cn("approval:group/menu-item approval:relative", className)}
      {...props}
    />
  )
}

const sidebarMenuButtonVariants = cva(
  "approval:peer/menu-button approval:group/menu-button approval:flex approval:w-full approval:items-center approval:gap-2 approval:overflow-hidden approval:rounded-md approval:p-2 approval:text-left approval:text-sm approval:ring-sidebar-ring approval:outline-hidden approval:transition-[width,height,padding] approval:group-has-data-[sidebar=menu-action]/menu-item:pr-8 approval:group-data-[collapsible=icon]:size-8! approval:group-data-[collapsible=icon]:p-2! approval:hover:bg-sidebar-accent approval:hover:text-sidebar-accent-foreground approval:focus-visible:ring-2 approval:active:bg-sidebar-accent approval:active:text-sidebar-accent-foreground approval:disabled:pointer-events-none approval:disabled:opacity-50 approval:aria-disabled:pointer-events-none approval:aria-disabled:opacity-50 approval:data-open:hover:bg-sidebar-accent approval:data-open:hover:text-sidebar-accent-foreground approval:data-active:bg-sidebar-accent approval:data-active:font-medium approval:data-active:text-sidebar-accent-foreground approval:[&_svg]:size-4 approval:[&_svg]:shrink-0 approval:[&>span:last-child]:truncate",
  {
    variants: {
      variant: {
        default: "approval:hover:bg-sidebar-accent approval:hover:text-sidebar-accent-foreground",
        outline:
          "approval:bg-background approval:shadow-[0_0_0_1px_var(--sidebar-border)] approval:hover:bg-sidebar-accent approval:hover:text-sidebar-accent-foreground approval:hover:shadow-[0_0_0_1px_var(--sidebar-accent)]",
      },
      size: {
        default: "approval:h-8 approval:text-sm",
        sm: "approval:h-7 approval:text-xs",
        lg: "approval:h-12 approval:text-sm approval:group-data-[collapsible=icon]:p-0!",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

function SidebarMenuButton({
  asChild = false,
  isActive = false,
  variant = "default",
  size = "default",
  tooltip,
  className,
  ...props
}: React.ComponentProps<"button"> & {
  asChild?: boolean
  isActive?: boolean
  tooltip?: string | React.ComponentProps<typeof TooltipContent>
} & VariantProps<typeof sidebarMenuButtonVariants>) {
  const Comp = asChild ? Slot.Root : "button"
  const { isMobile, state } = useSidebar()

  const button = (
    <Comp
      data-slot="sidebar-menu-button"
      data-sidebar="menu-button"
      data-size={size}
      data-active={isActive}
      className={cn(sidebarMenuButtonVariants({ variant, size }), className)}
      {...props}
    />
  )

  if (!tooltip) {
    return button
  }

  if (typeof tooltip === "string") {
    tooltip = {
      children: tooltip,
    }
  }

  return (
    <Tooltip>
      <TooltipTrigger asChild>{button}</TooltipTrigger>
      <TooltipContent
        side="right"
        align="center"
        hidden={state !== "collapsed" || isMobile}
        {...tooltip}
      />
    </Tooltip>
  )
}

function SidebarMenuAction({
  className,
  asChild = false,
  showOnHover = false,
  ...props
}: React.ComponentProps<"button"> & {
  asChild?: boolean
  showOnHover?: boolean
}) {
  const Comp = asChild ? Slot.Root : "button"

  return (
    <Comp
      data-slot="sidebar-menu-action"
      data-sidebar="menu-action"
      className={cn(
        "approval:absolute approval:top-1.5 approval:right-1 approval:flex approval:aspect-square approval:w-5 approval:items-center approval:justify-center approval:rounded-md approval:p-0 approval:text-sidebar-foreground approval:ring-sidebar-ring approval:outline-hidden approval:transition-transform approval:group-data-[collapsible=icon]:hidden approval:peer-hover/menu-button:text-sidebar-accent-foreground approval:peer-data-[size=default]/menu-button:top-1.5 approval:peer-data-[size=lg]/menu-button:top-2.5 approval:peer-data-[size=sm]/menu-button:top-1 approval:after:absolute approval:after:-inset-2 approval:hover:bg-sidebar-accent approval:hover:text-sidebar-accent-foreground approval:focus-visible:ring-2 approval:md:after:hidden approval:[&>svg]:size-4 approval:[&>svg]:shrink-0",
        showOnHover &&
          "approval:group-focus-within/menu-item:opacity-100 approval:group-hover/menu-item:opacity-100 approval:peer-data-active/menu-button:text-sidebar-accent-foreground approval:aria-expanded:opacity-100 approval:md:opacity-0",
        className
      )}
      {...props}
    />
  )
}

function SidebarMenuBadge({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sidebar-menu-badge"
      data-sidebar="menu-badge"
      className={cn(
        "approval:pointer-events-none approval:absolute approval:right-1 approval:flex approval:h-5 approval:min-w-5 approval:items-center approval:justify-center approval:rounded-md approval:px-1 approval:text-xs approval:font-medium approval:text-sidebar-foreground approval:tabular-nums approval:select-none approval:group-data-[collapsible=icon]:hidden approval:peer-hover/menu-button:text-sidebar-accent-foreground approval:peer-data-[size=default]/menu-button:top-1.5 approval:peer-data-[size=lg]/menu-button:top-2.5 approval:peer-data-[size=sm]/menu-button:top-1 approval:peer-data-active/menu-button:text-sidebar-accent-foreground",
        className
      )}
      {...props}
    />
  )
}

function SidebarMenuSkeleton({
  className,
  showIcon = false,
  ...props
}: React.ComponentProps<"div"> & {
  showIcon?: boolean
}) {
  // Random width between 50 to 90%.
  const [width] = React.useState(() => {
    return `${Math.floor(Math.random() * 40) + 50}%`
  })

  return (
    <div
      data-slot="sidebar-menu-skeleton"
      data-sidebar="menu-skeleton"
      className={cn("approval:flex approval:h-8 approval:items-center approval:gap-2 approval:rounded-md approval:px-2", className)}
      {...props}
    >
      {showIcon && (
        <Skeleton
          className="approval:size-4 approval:rounded-md"
          data-sidebar="menu-skeleton-icon"
        />
      )}
      <Skeleton
        className="approval:h-4 approval:max-w-(--skeleton-width) approval:flex-1"
        data-sidebar="menu-skeleton-text"
        style={
          {
            "--skeleton-width": width,
          } as React.CSSProperties
        }
      />
    </div>
  )
}

function SidebarMenuSub({ className, ...props }: React.ComponentProps<"ul">) {
  return (
    <ul
      data-slot="sidebar-menu-sub"
      data-sidebar="menu-sub"
      className={cn(
        "approval:mx-3.5 approval:flex approval:min-w-0 approval:translate-x-px approval:flex-col approval:gap-1 approval:border-l approval:border-sidebar-border approval:px-2.5 approval:py-0.5 approval:group-data-[collapsible=icon]:hidden",
        className
      )}
      {...props}
    />
  )
}

function SidebarMenuSubItem({
  className,
  ...props
}: React.ComponentProps<"li">) {
  return (
    <li
      data-slot="sidebar-menu-sub-item"
      data-sidebar="menu-sub-item"
      className={cn("approval:group/menu-sub-item approval:relative", className)}
      {...props}
    />
  )
}

function SidebarMenuSubButton({
  asChild = false,
  size = "md",
  isActive = false,
  className,
  ...props
}: React.ComponentProps<"a"> & {
  asChild?: boolean
  size?: "sm" | "md"
  isActive?: boolean
}) {
  const Comp = asChild ? Slot.Root : "a"

  return (
    <Comp
      data-slot="sidebar-menu-sub-button"
      data-sidebar="menu-sub-button"
      data-size={size}
      data-active={isActive}
      className={cn(
        "approval:flex approval:h-7 approval:min-w-0 approval:-translate-x-px approval:items-center approval:gap-2 approval:overflow-hidden approval:rounded-md approval:px-2 approval:text-sidebar-foreground approval:ring-sidebar-ring approval:outline-hidden approval:group-data-[collapsible=icon]:hidden approval:hover:bg-sidebar-accent approval:hover:text-sidebar-accent-foreground approval:focus-visible:ring-2 approval:active:bg-sidebar-accent approval:active:text-sidebar-accent-foreground approval:disabled:pointer-events-none approval:disabled:opacity-50 approval:aria-disabled:pointer-events-none approval:aria-disabled:opacity-50 approval:data-[size=md]:text-sm approval:data-[size=sm]:text-xs approval:data-active:bg-sidebar-accent approval:data-active:text-sidebar-accent-foreground approval:[&>span:last-child]:truncate approval:[&>svg]:size-4 approval:[&>svg]:shrink-0 approval:[&>svg]:text-sidebar-accent-foreground",
        className
      )}
      {...props}
    />
  )
}

export {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupAction,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInput,
  SidebarInset,
  SidebarMenu,
  SidebarMenuAction,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSkeleton,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
  SidebarProvider,
  SidebarRail,
  SidebarSeparator,
  SidebarTrigger,
  useSidebar,
}

