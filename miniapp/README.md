# Debits Mini App

Vite + React 19 + TypeScript + Tailwind 4 + TanStack Query + i18next. Общая картина — в корневом README.md и PLAN.md.

```bash
npm install
npm run dev      # :5173, /api проксируется на :5080
npm run build    # tsc -b && vite build → dist/
npm run lint     # oxlint
npm test         # vitest
```

```
src/
  api/         client.ts (JWT, X-Shop-Id, ApiError), queries.ts (хуки TanStack Query), types.ts (DTO бэкенда)
  auth/        Session.tsx — вход, активный магазин, язык
  components/  карточки, листы, чипы, графики
  hooks/       toast, back button, запись голоса, бесконечная прокрутка
  i18n/        инициализация и тест паритета ключей
  lib/         telegram.ts, деньги, даты, имена, текст напоминания
  pages/       Customers, Customer, Debts, Reports, Settings, Staff, Shops
  styles/      tokens.css — токены дизайна и тёмная тема из --tg-theme-*
public/locales/{uz-Latn,uz-Cyrl,ru}/translation.json
```

Вне Telegram в dev-режиме вход идёт через заголовок `X-Dev-Telegram-Id`: значение из `VITE_DEV_TELEGRAM_ID` в `.env.local` или из `localStorage.dev_tg_id`.
Ошибки бэкенда приходят с полем `code`; текст берётся по ключу `errors.<code>`.
