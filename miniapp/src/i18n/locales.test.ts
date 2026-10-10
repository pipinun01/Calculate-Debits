import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { LANGUAGES } from './index'

/** Every action the backend writes to audit_events (AuditWriter.Write call sites). */
const AUDIT_ACTIONS = [
  'transaction.create',
  'transaction.update',
  'transaction.delete',
  'customer.create',
  'customer.update',
  'member.role',
  'member.deactivate',
  'invite.revoke',
  'shop.update',
  'export.customer',
  'export.shop',
]

const root = join(process.cwd(), 'public/locales')
const load = (lang: string): Record<string, string> => JSON.parse(readFileSync(join(root, lang, 'translation.json'), 'utf8'))
const placeholders = (s: string) => [...s.matchAll(/\{\{(\w+)\}\}/g)].map((m) => m[1]).sort()

/** Every t('…') / t(`…`) literal in the sources, plus the keys built from a known set of values. */
function usedKeys(): string[] {
  const keys = new Set<string>()
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name)
      if (entry.isDirectory()) walk(path)
      else if (/\.tsx?$/.test(entry.name) && !entry.name.includes('.test.')) {
        const source = readFileSync(path, 'utf8')
        for (const m of source.matchAll(/['"]((?:tabs|common|role|lang|hero|customers?|ledger|due|entry|voice|debts|reports|settings|staff|shops|start|errors|audit)\.[A-Za-z0-9_.-]+)['"]/g)) {
          keys.add(m[1])
        }
      }
    }
  }
  walk(join(process.cwd(), 'src'))
  return [...keys]
}

describe('locales', () => {
  const reference = load('uz-Latn')

  it('have the same keys in every language', () => {
    for (const lang of LANGUAGES) {
      expect(Object.keys(load(lang)).sort(), lang).toEqual(Object.keys(reference).sort())
    }
  })

  it('use the same placeholders in every language', () => {
    for (const lang of LANGUAGES) {
      const table = load(lang)
      for (const key of Object.keys(reference)) {
        expect(placeholders(table[key]), `${key} in ${lang}`).toEqual(placeholders(reference[key]))
      }
    }
  })

  it('have no empty strings', () => {
    for (const lang of LANGUAGES) {
      for (const [key, value] of Object.entries(load(lang))) expect(value.trim(), `${key} in ${lang}`).not.toBe('')
    }
  })

  it('contain every key the code refers to', () => {
    const missing = usedKeys().filter((key) => !(key in reference))
    expect(missing).toEqual([])
  })

  it('cover the dynamic key families', () => {
    const dynamic = [
      ...LANGUAGES.map((l) => `lang.${l}`),
      'role.owner',
      'role.worker',
      ...['open', 'overdue', 'due_soon', 'on_time'].map((s) => `debts.section.${s}`),
      'settings.recipients.all',
      'settings.recipients.owners',
      ...AUDIT_ACTIONS.map((a) => `audit.action.${a}`),
      ...['bot.blocked', 'bot.not_configured', 'bot.send_failed'].map((c) => `errors.${c}`),
    ]
    expect(dynamic.filter((key) => !(key in reference))).toEqual([])
  })
})
