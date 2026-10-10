import type { ReactNode } from 'react'
import { ChevronRight } from 'lucide-react'
import type { Member } from '../api/types'
import { Avatar } from './Avatar'

interface Props {
  icon: ReactNode
  title: string
  hint?: string
  right?: ReactNode
  onClick?(): void
  children?: ReactNode
  tone?: 'default' | 'danger'
}

/** A settings line: icon, title + hint, a value / control on the right, optional controls underneath. */
export function SettingRow({ icon, title, hint, right, onClick, children, tone = 'default' }: Props) {
  const Wrapper = onClick ? 'button' : 'div'
  return (
    <div className="border-line border-b px-4 py-3.5 last:border-b-0">
      <Wrapper {...(onClick ? { type: 'button' as const, onClick } : {})} className="flex w-full items-center gap-3 text-left">
        <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${tone === 'danger' ? 'bg-danger-soft text-danger' : 'bg-primary-soft text-primary'}`}>{icon}</div>
        <div className="min-w-0 flex-1">
          <p className={`text-sm font-bold ${tone === 'danger' ? 'text-danger' : ''}`}>{title}</p>
          {hint && <p className="text-muted truncate text-xs">{hint}</p>}
        </div>
        {right ?? (onClick && <ChevronRight className="text-muted h-4 w-4 shrink-0" />)}
      </Wrapper>
      {children && <div className="mt-3">{children}</div>}
    </div>
  )
}

export function SettingGroup({ children }: { children: ReactNode }) {
  return <div className="bg-card shadow-soft rounded-card overflow-hidden">{children}</div>
}

export function MemberRow({ member, roleLabel, youLabel, onClick }: { member: Member; roleLabel: string; youLabel: string; onClick?(): void }) {
  const Wrapper = onClick ? 'button' : 'div'
  return (
    <Wrapper {...(onClick ? { type: 'button' as const, onClick } : {})} className="border-line flex w-full items-center gap-3 border-b px-4 py-3 text-left last:border-b-0">
      <Avatar name={member.firstName} seed={member.userId} size="sm" />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-bold">
          {member.firstName}
          {member.isMe && <span className="text-muted font-semibold"> · {youLabel}</span>}
        </p>
        {member.username && <p className="text-muted truncate text-xs">@{member.username}</p>}
      </div>
      <span className={`rounded-lg px-2 py-1 text-[11px] font-bold ${member.role === 'owner' ? 'bg-primary-soft text-primary' : 'bg-field text-muted'}`}>{roleLabel}</span>
    </Wrapper>
  )
}
