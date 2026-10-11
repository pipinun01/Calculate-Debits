import { useState } from 'react'
import { Crown, Share2, Trash2, UserPlus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useChangeRole, useCreateInvite, useInvites, useMembers, useRemoveMember, useRevokeInvite } from '../api/queries'
import type { Invite, Member } from '../api/types'
import { useSession } from '../auth/Session'
import { BottomSheet } from '../components/BottomSheet'
import { Button } from '../components/Button'
import { MemberRow, SettingGroup } from '../components/SettingRow'
import { PageHeader } from '../components/ShopHeader'
import { ErrorState, Loading, SectionTitle } from '../components/States'
import { useBackButton } from '../hooks/useBackButton'
import { useToast } from '../hooks/useToast'
import { formatDate } from '../lib/dates'
import { confirmDialog, shareToChat } from '../lib/telegram'

export default function StaffPage() {
  const { t } = useTranslation()
  const toast = useToast()
  const { shop, isOwner } = useSession()
  useBackButton(true)

  const members = useMembers()
  const invites = useInvites(isOwner)
  const createInvite = useCreateInvite()
  const revokeInvite = useRevokeInvite()
  const changeRole = useChangeRole()
  const removeMember = useRemoveMember()
  const [selected, setSelected] = useState<Member | null>(null)

  const onError = (e: unknown) => toast.error(e)
  const share = (invite: Invite) => shareToChat(t('staff.inviteText', { shop: shop?.shopName ?? '' }), invite.link)

  const invite = () => createInvite.mutate('worker', { onSuccess: share, onError })

  const remove = async (member: Member) => {
    if (!(await confirmDialog(t('staff.removeConfirm', { name: member.firstName })))) return
    removeMember.mutate(member.id, { onSuccess: () => setSelected(null), onError })
  }

  return (
    <div className="pb-8">
      <PageHeader title={t('settings.staffList')} subtitle={shop?.shopName} />
      <div className="app-x pt-1">
        {members.isPending ? (
          <Loading full />
        ) : members.isError ? (
          <ErrorState onRetry={() => void members.refetch()} />
        ) : (
          <>
            <SectionTitle right={t('settings.members', { count: members.data.length })}>{t('staff.members')}</SectionTitle>
            <SettingGroup>
              {members.data.map((m) => (
                <MemberRow key={m.id} member={m} roleLabel={t(`role.${m.role}`)} youLabel={t('staff.you')} onClick={isOwner && !m.isMe ? () => setSelected(m) : undefined} />
              ))}
            </SettingGroup>

            {isOwner ? (
              <>
                <Button full variant="soft" className="mt-4" icon={<UserPlus className="h-4 w-4" />} loading={createInvite.isPending} onClick={invite}>
                  {t('staff.invite')}
                </Button>
                <p className="text-muted mt-2 px-1 text-[11px]">{t('staff.inviteHint')}</p>

                {(invites.data?.length ?? 0) > 0 && (
                  <>
                    <SectionTitle>{t('staff.activeInvites')}</SectionTitle>
                    <SettingGroup>
                      {invites.data!.map((i) => (
                        <div key={i.id} className="border-line flex items-center gap-2 border-b px-4 py-3 last:border-b-0">
                          <div className="min-w-0 flex-1">
                            <p className="text-sm font-bold">{t(`role.${i.role}`)}</p>
                            <p className="text-muted text-xs">{t('staff.validUntil', { date: formatDate(i.expiresAt.slice(0, 10)) })}</p>
                          </div>
                          <button type="button" onClick={() => share(i)} aria-label={t('staff.share')} className="bg-primary-soft text-accent flex h-9 w-9 items-center justify-center rounded-xl">
                            <Share2 className="h-4 w-4" />
                          </button>
                          <button type="button" onClick={() => revokeInvite.mutate(i.id, { onError })} aria-label={t('staff.revoke')} className="bg-danger-soft text-danger flex h-9 w-9 items-center justify-center rounded-xl">
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      ))}
                    </SettingGroup>
                  </>
                )}
              </>
            ) : (
              <p className="text-muted mt-3 px-1 text-xs">{t('staff.ownerOnly')}</p>
            )}
          </>
        )}
      </div>

      <BottomSheet open={selected !== null} onClose={() => setSelected(null)} title={selected?.firstName}>
        {selected && (
          <div className="space-y-2.5">
            <p className="text-muted px-1 text-xs">{t(selected.role === 'owner' ? 'staff.ownerCan' : 'staff.workerCan')}</p>
            <Button
              full
              variant="outline"
              icon={<Crown className="h-4 w-4" />}
              loading={changeRole.isPending}
              onClick={() => changeRole.mutate({ id: selected.id, role: selected.role === 'owner' ? 'worker' : 'owner' }, { onSuccess: () => setSelected(null), onError })}
            >
              {t(selected.role === 'owner' ? 'staff.makeWorker' : 'staff.makeOwner')}
            </Button>
            <Button full variant="danger" icon={<Trash2 className="h-4 w-4" />} loading={removeMember.isPending} onClick={() => void remove(selected)}>
              {t('staff.remove')}
            </Button>
          </div>
        )}
      </BottomSheet>
    </div>
  )
}
