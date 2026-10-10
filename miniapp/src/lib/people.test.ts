import { describe, expect, it } from 'vitest'
import { avatarColors, displayName, initials, telLink } from './people'
import { reminderText } from './reminder'

describe('people', () => {
  it('builds initials for Latin and Cyrillic names', () => {
    expect(initials('Ahmad aka')).toBe('AA')
    expect(initials('Jasur')).toBe('JA')
    expect(initials('  Нодира   опа ')).toBe('НО')
    expect(initials('')).toBe('?')
  })

  it('gives the same avatar colour for the same seed', () => {
    expect(avatarColors('abc')).toEqual(avatarColors('abc'))
  })

  it('formats the display name', () => {
    expect(displayName('Ahmad aka', 'stomatolog')).toBe('Ahmad aka (stomatolog)')
    expect(displayName('Ahmad aka', null)).toBe('Ahmad aka')
  })

  it('makes tel: links only from real numbers', () => {
    expect(telLink('+998 90 123-45-67')).toBe('tel:+998901234567')
    expect(telLink('123')).toBeNull()
    expect(telLink(null)).toBeNull()
  })
})

describe('reminderText', () => {
  const base = { customerName: 'Ahmad aka', shopName: 'Aloqa bozori', amount: 550000, dueDate: '2026-10-16' }

  it('uses the shop language and the overdue / upcoming wording', () => {
    const upcoming = reminderText({ ...base, language: 'uz-Latn', daysLeft: 3 })
    expect(upcoming).toContain('Ahmad aka')
    expect(upcoming).toContain('«Aloqa bozori»')
    expect(upcoming).toContain("550 000 so'm")
    expect(upcoming).toContain('16.10.2026 gacha')

    const overdue = reminderText({ ...base, language: 'ru', daysLeft: -2 })
    expect(overdue).toContain('истёк 16.10.2026')
    expect(overdue).toContain('550 000 сум')

    expect(reminderText({ ...base, language: 'uz-Cyrl', daysLeft: 0 })).toContain('16.10.2026 гача')
  })

  it('leaves no unfilled placeholders', () => {
    for (const language of ['uz-Latn', 'uz-Cyrl', 'ru'] as const) {
      for (const daysLeft of [-1, 5]) expect(reminderText({ ...base, language, daysLeft })).not.toMatch(/[{}]/)
    }
  })
})
