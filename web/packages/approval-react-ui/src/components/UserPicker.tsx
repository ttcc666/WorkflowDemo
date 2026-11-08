import { useEffect, useRef, useState } from 'react'
import type { DirectoryUser } from '@approval/shared'
import { useApprovalIdentity, useApprovalClient, useDirectory } from '../client'
import { Button, Badge, Loading } from './ui/primitives'
import { useFieldAria } from './ui/fieldContext'
import { Popover, PopoverAnchor, PopoverContent } from './shadcn/popover'
import {
  Command,
  CommandInput,
  CommandList,
  CommandGroup,
  CommandItem,
  CommandEmpty,
} from './shadcn/command'

export interface UserPickerProps {
  value: string[]
  onChange: (ids: string[]) => void
  multiple?: boolean
  excludedUsers?: string[]
  disabled?: boolean
  label?: string
}
export function UserPicker(props: UserPickerProps) {
  const Custom = useApprovalClient().options().userPicker
  return Custom ? <Custom {...props} /> : <DirectoryPicker {...props} />
}
function DirectoryPicker({
  value,
  onChange,
  multiple = false,
  excludedUsers = [],
  disabled,
  label = '搜索人员',
}: UserPickerProps) {
  const directory = useDirectory()
  const aria = useFieldAria({ 'aria-label': label })
  const identity = useApprovalIdentity()
  const [keyword, setKeyword] = useState('')
  const [result, setResult] = useState<DirectoryUser[]>([])
  const [selected, setSelected] = useState<DirectoryUser[]>([])
  const [loading, setLoading] = useState(false)
  const [open, setOpen] = useState(false)
  const revision = useRef(0)
  useEffect(() => {
    let active = true
    setSelected([])
    void directory.resolve(value).then((users) => {
      if (active) setSelected(users)
    })
    return () => {
      active = false
    }
  }, [identity, JSON.stringify(value)])
  useEffect(() => {
    const current = ++revision.current
    setResult([])
    if (!open || disabled) return
    setLoading(true)
    const timer = window.setTimeout(() => {
      void directory.search(keyword).then((users) => {
        if (revision.current === current) {
          setResult(users)
          setLoading(false)
        }
      })
    }, 180)
    return () => {
      window.clearTimeout(timer)
      revision.current++
    }
  }, [identity, keyword, open, disabled])
  useEffect(() => {
    setKeyword('')
    setOpen(false)
    setResult([])
  }, [identity])
  function choose(user: DirectoryUser) {
    if (!user.isActive || excludedUsers.includes(user.id)) return
    onChange(multiple ? [...new Set([...value, user.id])] : [user.id])
    setKeyword('')
    setOpen(false)
  }
  return (
    <div className="ar-stack" style={{ gap: 6 }}>
      <div className="ar-row">
        {selected.map((user) => (
          <Badge key={user.id}>
            {user.name}
            <Button
              variant="ghost"
              size="sm"
              disabled={disabled}
              aria-label={`删除 ${user.name}`}
              onClick={() => onChange(value.filter((id) => id !== user.id))}
            >
              ×
            </Button>
          </Badge>
        ))}
      </div>
      {!disabled && (
        <Command label={label} shouldFilter={false}>
          <Popover open={open} onOpenChange={setOpen}>
            <PopoverAnchor asChild>
              <div>
                <CommandInput
                  {...aria}
                  aria-label={label}
                  aria-expanded={open}
                  placeholder={label}
                  value={keyword}
                  onFocus={() => setOpen(true)}
                  onValueChange={(value) => {
                    setKeyword(value)
                    setOpen(true)
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'ArrowDown' && !open) setOpen(true)
                    if (event.key === 'Escape' && open) {
                      event.preventDefault()
                      event.stopPropagation()
                      setOpen(false)
                    }
                  }}
                />
              </div>
            </PopoverAnchor>
            <PopoverContent
              className="approval-react approval:w-[var(--radix-popper-anchor-width)]"
              align="start"
              onOpenAutoFocus={(event) => event.preventDefault()}
            >
              <CommandList label="人员搜索结果" aria-label="人员搜索结果">
                {loading ? (
                  <Loading />
                ) : (
                  <>
                    <CommandEmpty>没有匹配人员</CommandEmpty>
                    <CommandGroup>
                      {result.map((user) => (
                        <CommandItem
                          key={user.id}
                          value={user.id}
                          disabled={
                            !user.isActive ||
                            excludedUsers.includes(user.id) ||
                            (multiple && value.includes(user.id))
                          }
                          onSelect={() => choose(user)}
                        >
                          {user.name} · {user.id} {user.roles.join('、')}
                        </CommandItem>
                      ))}
                    </CommandGroup>
                  </>
                )}
              </CommandList>
              <Button variant="ghost" onClick={() => setOpen(false)}>
                收起
              </Button>
            </PopoverContent>
          </Popover>
        </Command>
      )}
      {!directory.available && <small role="status">宿主未提供用户目录</small>}
    </div>
  )
}
