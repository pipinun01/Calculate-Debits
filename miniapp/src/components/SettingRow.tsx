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
  tone?: 'default' | 'danger' | 'accent'
}

/** A settings line: icon, title + hint, a value / control on the right, optional controls underneath. */
export function SettingRow({ icon, title, hint, right, onClick, children, tone = 'default' }: Props) {
  const Wrapper = onClick ? 'button' : 'div'
  return (
    <div className={`border-line border-b px-4 py-3.5 last:border-b-0 ${onClick ? 'active:bg-field transition' : ''}`}>
      <Wrapper {...(onClick ? { type: 'button' as const, onClick } : {})} className="flex w-full items-center gap-2.5 text-left">
        <div className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${tone === 'danger' ? 'bg-danger-soft text-danger' : tone === 'accent' ? 'bg-primary-soft text-accent' : 'bg-field text-accent'}`}>{icon}</div>
        <div className="min-w-0 flex-1">
          <p className={`text-sm font-semibold ${tone === 'danger' ? 'text-danger' : tone === 'accent' ? 'text-accent font-bold' : ''}`}>{title}</p>
          {hint && <p className="text-muted text-xs leading-snug">{hint}</p>}
        </div>
        {right ?? (onClick && <ChevronRight className={`h-4 w-4 shrink-0 ${tone === 'accent' ? 'text-accent' : 'text-muted'}`} />)}
      </Wrapper>
      {children && <div className="mt-3">{children}</div>}
    </div>
  )
}

export function SettingGroup({ children }: { children: ReactNode }) {
  return <div className="bg-card border-line shadow-soft rounded-card overflow-hidden border">{children}</div>
}

export function MemberRow({ member, roleLabel, youLabel, onClick }: { member: Member; roleLabel: string; youLabel: string; onClick?(): void }) {
  const Wrapper = onClick ? 'button' : 'div'
  return (
    <Wrapper {...(onClick ? { type: 'button' as const, onClick } : {})} className="border-line flex w-full items-center gap-2.5 border-b px-4 py-3 text-left last:border-b-0">
      <Avatar name={member.firstName} seed={member.userId} size="sm" round />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-semibold">
          {member.firstName}
          {member.isMe && <span className="text-muted font-medium"> · {youLabel}</span>}
        </p>
        {member.username && <p className="text-muted truncate text-xs">@{member.username}</p>}
      </div>
      <span className={`shrink-0 rounded-md px-2 py-0.5 text-[11px] font-bold ${member.role === 'owner' ? 'bg-success-soft text-success' : 'bg-field text-muted'}`}>{roleLabel}</span>
    </Wrapper>
  )
}
