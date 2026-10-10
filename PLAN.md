# Debits — цифровая «долговая тетрадь» для махаллинских магазинов

## Context

Локальные магазины в махаллях Узбекистана дают товары в долг и записывают это в тетрадь. Найти запись месячной давности долго. Продукт: Telegram-бот + Telegram Mini App. Продавец голосом говорит «Ахмад ака 550 минг қарз», бот распознаёт, показывает карточку подтверждения и записывает в историю клиента. Магазин = тенант, у магазина несколько владельцев и работников. Бот напоминает сотрудникам о долгах за 3 дня до срока и в день срока.

В проекте только папка `design/` с шестью экранами Stitch. На машине есть Node 22, **нет .NET SDK и Docker** — ставим в фазе 0.

## Подтверждённые решения

| Вопрос | Решение |
|---|---|
| Стек | .NET 10 (LTS) Web API (бот внутри того же процесса) + React/Vite/TS Mini App + PostgreSQL 16; VPS + Docker Compose + Caddy (HTTPS) |
| STT | Groq API `whisper-large-v3` (бесплатный лимит), за интерфейсом `ISpeechToText`. Каждое голосовое распознаётся двумя проходами параллельно: автоопределение языка с русской подсказкой и принудительный узбекский с узбекской подсказкой и именами клиентов магазина; выбор по содержимому (см. заметки фазы 4) |
| Парсинг фразы | Groq LLM `openai/gpt-oss-120b` (запасная `openai/gpt-oss-20b`; `llama-3.3-70b` из исходного плана в Groq больше нет) с JSON-выводом + regex (`RegexEntryParser` на `UzNumberParser`, `DuePhraseParser`): regex работает всегда, заполняет пропуски LLM и сверяет сумму |
| Дубли имён | Клиент = имя + тег/прозвище + телефон (опц.). Нечёткий поиск pg_trgm по нормализованному ключу (кириллица→латиница). >1 совпадения → кнопки выбора, 0 → создать нового |
| Роли | Owner: всё + удаление, отчёты, сотрудники, настройки. Worker: долг/оплата, поиск, история; свою запись может править/удалять 10 минут |
| Операция | Сумма (UZS, целое `bigint`) + комментарий + срок (для долга) + кто записал |
| Оплаты | FIFO: оплата гасит самый старый открытый долг. В Mini App опционально можно выбрать конкретный долг (`target_debt_id`) |
| Срок | Из голоса («бир ҳафтага») / кнопками в карточке / по умолчанию магазина (30 дней) |
| Напоминания | 09:00 Asia/Tashkent, за 3 дня и в день срока, одно сгруппированное сообщение. Всем сотрудникам, владелец может переключить на «только владельцы» |
| Клиентская сторона | Бот с клиентом не общается. Есть ручная кнопка «Eslatma yuborish»: share-текст в Telegram или звонок |
| Из дизайна в MVP | Голос из Mini App, Excel-экспорт, сводка отчёта в чат бота. PIN / Face ID скрыт |
| Онбординг | `/start` → мастер создания магазина; `t.me/<bot>?start=inv_<token>` для работников. Юзер может состоять в нескольких магазинах, есть «активный магазин» |
| Языки | uz-Latn, uz-Cyrl, ru; по умолчанию из `language_code`, переключатель; одна настройка для бота и Mini App |
| Дизайн | Готов: 6 экранов Stitch в `design/` (PNG + HTML), см. раздел «Дизайн» |
| Удаление | Только soft delete, `audit_events` на правки/удаления/смену ролей |

## Структура репозитория

```
Debits/
├── backend/
│   ├── Debits.sln, Directory.Build.props, Directory.Packages.props
│   ├── src/
│   │   ├── Debits.Domain/          # сущности, UzTranslit, SearchKey, UzNumberParser,
│   │   │                           # DuePhraseParser, AllocationRecalculator (чистый C#)
│   │   ├── Debits.Application/     # сервисы, DTO, интерфейсы (ITenantContext, IClock,
│   │   │                           # ISpeechToText, IEntryParser), валидаторы, ключи локализации
│   │   ├── Debits.Infrastructure/  # EF Core + Npgsql, миграции, Groq-клиенты, поиск, JSON-локализатор
│   │   ├── Debits.Bot/             # Telegram.Bot хендлеры, клавиатуры, состояния, ReminderService
│   │   └── Debits.Api/             # хост: контроллеры, auth, TenantMiddleware, webhook, SPA, Serilog
│   └── tests/ Debits.Domain.Tests, Debits.Application.Tests, Debits.Integration.Tests (Testcontainers)
├── miniapp/                        # Vite + React + TS + Tailwind, i18next, TanStack Query,
│   └── src/ api/ auth/ i18n/ components/ pages/ lib/ ; public/locales/{uz-Latn,uz-Cyrl,ru}
├── deploy/ docker-compose.yml, docker-compose.dev.yml, Dockerfile.app, Caddyfile, backup.sh
├── design/                         # 6 экранов Stitch: N.png + N.html (референс)
└── .env.example, README.md, .gitignore, .editorconfig
```

Пакеты: `Telegram.Bot`, `Microsoft.EntityFrameworkCore` 10, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `FluentValidation`, `JwtBearer`, `Serilog.AspNetCore`, `Microsoft.Extensions.Http.Resilience`; тесты: xunit, FluentAssertions, NSubstitute, Testcontainers.PostgreSql, Mvc.Testing. Groq через голый `HttpClient` (OpenAI-совместимые эндпоинты). Frontend: `@telegram-apps/sdk-react`, `react-router-dom`, `@tanstack/react-query`, `i18next`, `tailwindcss`, `dayjs`, `zod`.

## Схема БД (snake_case, расширения `pg_trgm`, `unaccent`)

Все тенантные таблицы имеют `shop_id` + EF global query filter через `ITenantContext`; `SaveChanges` проставляет `ShopId` и бросает исключение при несовпадении.

- `users` (telegram_user_id unique, first_name, username, language, active_shop_id)
- `shops` (name, default_due_days=30, language, timezone='Asia/Tashkent', reminder_recipients 'all'|'owners', reminder_hour=9)
- `shop_members` (shop_id, user_id, role 'owner'|'worker', is_active, notifications_blocked_at; unique(shop_id,user_id))
- `invites` (shop_id, token unique, role, expires_at, max_uses=1, used_count, revoked_at)
- `customers` (shop_id, name, tag, phone, notes, search_key, is_archived, created_by_member_id). GIN `search_key gin_trgm_ops`, unique(shop_id, search_key)
- `transactions` (shop_id, customer_id, type 'debt'|'payment', amount bigint>0, comment, due_date date (NOT NULL при debt, CHECK), target_debt_id null, source 'manual'|'voice'|'text', voice_file_id, transcript, created_by_member_id, created_at, updated_*, is_deleted, deleted_by_member_id, deleted_at, delete_reason). Индексы: (shop_id, customer_id, created_at desc); partial (shop_id, due_date) where debt and not deleted
- `payment_allocations` (payment_id, debt_id, amount; unique(payment_id,debt_id)) — производная, пересчитывается целиком по клиенту при любом изменении
- `pending_entries` (bigserial id для callback_data, shop_id, user_id, chat_id, message_id, payload jsonb, status, expires_at)
- `conversation_states` (user_id pk, state, data jsonb)
- `reminder_log` (transaction_id, kind 'due_in_3'|'due_today', local_date; unique(transaction_id, kind, local_date))
- `audit_events` (shop_id, member_id, action, entity, entity_id, data jsonb)

Views (keyless EF): `customer_balances` = Σdebt − Σpayment; `open_debts` = долги с `remaining = amount − Σallocations > 0`.

## Ключевая логика

**Tenant.** API: JWT (`tg_user_id`) + заголовок `X-Shop-Id` → `TenantMiddleware` проверяет членство → `ITenantContext {ShopId, MemberId, Role, Language}`. Бот: `BotTenantResolver` по `users.active_shop_id`.

**AllocationRecalculator (Domain, чистая функция).** Вход: все не удалённые транзакции клиента по времени. Проход: очередь открытых долгов + счётчик нераспределённого кредита; оплата с `target_debt_id` сначала гасит целевой долг, остаток и обычные оплаты — FIFO; новый долг сначала съедает нераспределённый кредит. Мутации леджера в транзакции с `SELECT ... FOR UPDATE` строки клиента, затем полный пересчёт.

**Поиск клиента.** `SearchKey.Normalize(name + ' ' + tag)`: lowercase, `UzTranslit` кириллица→латиница, унификация апострофов. `similarity(search_key, q)`; «сильное» совпадение ≥0.6 с отрывом ≥0.15 от следующего либо точное равенство → автоподстановка; иначе Ambiguous/None.

**Права.** Политика `OwnerOnly` на удаление/правку чужих записей, сотрудников, настройки. Worker: PATCH/DELETE своей транзакции, если `created_at > now − 10 min`.

## Бот-флоу

- `/start` без payload: upsert user → нет магазинов → мастер (название → срок по умолчанию [15][30][45][свой] → язык) → shop + owner → меню. Есть магазины → меню.
- `/start inv_<token>`: валидация (срок, revoke, max_uses, не член) → `shop_members` → активный магазин → уведомить владельца.
- `/menu`: WebApp-кнопка, активный магазин, язык. `setChatMenuButton` на старте. `/shops`, `/language`, `/invite`, `/help`.
- **Голос/текст → запись:**
  1. Голос: `GetFile` → скачать ogg (лимит 120 с) → Groq transcriptions (`whisper-large-v3`, `verbose_json`, `temperature=0`, prompt со словарём: nasiya, qarz, to'ladi, berdi, ming, million, so'm, aka, opa, hafta, oy oxirigacha, долг, оплатил, тысяч, миллион). Без `language` (смешанная речь); A/B `language=uz` в фазе 4.
  2. `EntryParsingService`: пренормализация → LLM JSON `{intent, customer, amount_uzs, due_in_days, due_date, comment, confidence}` с 8–10 few-shot (uz-Latn, uz-Cyrl, ru, mix, числа словами, оплаты, «oy oxirigacha») → regex-фолбэк при ошибке/таймауте; расхождение сумм → строка «⚠️ проверь сумму»; сумма не найдена → кнопка `[💰 Сумма]`.
  3. `CustomerService.Resolve` → Exact / Ambiguous (кнопки `Ahmad aka (stomatolog)`…) / None («Создать: Ahmad aka», затем опциональный тег, [Пропустить]).
  4. `pending_entries` (TTL 30 мин) + карточка: транскрипт, магазин, клиент, тип, сумма, срок, комментарий; кнопки `[✅] [✏️ Клиент] [📅 Срок] [↔ Долг/Оплата] [💰 Сумма] [❌]`. Срок: `[3 дня][неделя][месяц][конец месяца][дата]`. callback_data ≤ 64 байт: `pe:{id}:ok|cu:{i}|cu:new|due:3|7|30|eom|pick|type|amt|x`.
  5. Подтверждение → `LedgerService` → карточка превращается в чек «✅ Записал Сарвар · баланс Ахмад ака: 1 250 000». Повторный/просроченный колбэк → toast. Владельцу и автору (10 мин) — `[🗑 Удалить]`.
  6. Часовой свип: `expired`, удаление строк старше 24 ч.

## Напоминания

`ReminderService : BackgroundService`: ждёт `reminder_hour` магазина по Asia/Tashkent (перепроверка каждые 5 мин), на старте догоняет пропущенный день. По каждому магазину: `open_debts` с `due_date = today+3` (`due_in_3`) и `= today` (`due_today`), минус уже в `reminder_log`. Одно сообщение на магазин на вид со списком «клиент — остаток (срок) · записал X» и кнопкой «Открыть Debits». Получатели по `reminder_recipients`. `reminder_log` пишется в той же транзакции до отправки; 403 от Telegram → `notifications_blocked_at`. Образ должен содержать `tzdata`.

## Mini App

**Auth:** `retrieveLaunchParams().initDataRaw` → `POST /api/auth/telegram` → `TelegramInitDataValidator` (HMAC: secret = HMAC_SHA256("WebAppData", bot_token), data_check_string, `auth_date` ≤ 24 ч) → JWT 12 ч + membership’ы. В Development заголовок `X-Dev-Telegram-Id` для браузера.

**REST:**
```
POST /api/auth/telegram · GET/PATCH /api/me
GET/POST /api/shops · GET/PATCH /api/shop
GET /api/members · PATCH/DELETE /api/members/{id} [owner] (защита последнего owner)
GET/POST /api/invites · DELETE /api/invites/{id} [owner]
GET /api/customers?q&filter=all|debtors|due_soon|archived&sort=name|balance|due&cursor&limit · POST · GET/PATCH /api/customers/{id} · GET /api/customers/{id}/ledger?cursor&limit&type · GET /api/customers/search?q
POST /api/transactions {customerId,type,amount,comment?,dueDate?,targetDebtId?} · GET/PATCH/DELETE /api/transactions/{id}?reason   (DELETE → новый баланс клиента)
GET /api/debts?status=open|overdue|due_soon|on_time&cursor&limit → {summary, items, nextCursor} · GET /api/debts/summary
GET /api/reports/summary?from&to (по умолчанию текущий месяц в TZ магазина) · top-debtors?limit · collections?from&to · POST /api/reports/send {from,to}
POST /api/entries/parse (multipart audio | text) → ParsedEntry + кандидаты клиентов  (голос из Mini App, тот же пайплайн, без записи)
GET /api/export/customers/{id} · GET /api/export/shop?from&to [owner] · POST …/send (файл в чат с ботом)   (ClosedXML)
GET /api/audit?cursor&limit [owner]
POST /tg/webhook (X-Telegram-Bot-Api-Secret-Token) · GET /health
```
Ошибки: ProblemDetails с `code` → ключ i18n. Заголовок `X-Shop-Id` на всех тенантных эндпоинтах (нет → 400 `shop.required`, не член → 403 `shop.not_member`); `Authorization: Bearer <jwt>`. Пагинация: непрозрачный `cursor` (offset) + `nextCursor` в ответе. PATCH-семантика: `null` = не менять, `""` очищает строковое поле, `targetDebtId = 00000000-…` снимает привязку.

**Экраны/маршруты:** `/customers` (поиск, балансы, FAB), `/customers/:id` (баланс, вкладки Леджер/Открытые долги, bottom sheet добавления с переключателем тип, суммой с разделителями, сроком, выбором долга для оплаты), `/debts` (по сроку; просроченные красным, ≤3 дня янтарным), `/reports`, `/staff` (owner: члены, invite-ссылка, share), `/settings`, `/shops`. Нижний таббар. Telegram `BackButton`/`MainButton`. Tailwind с токенами дизайна; `@telegram-apps/telegram-ui` не используем, дизайн кастомный.

## Дизайн (готов, `design/`)

Шесть экранов Stitch, у каждого `N.png` + `N.html` (Tailwind CDN, Material Symbols Outlined, Plus Jakarta Sans). Это референс, а не код для копирования: HTML статичный, конфиги Tailwind в файлах расходятся, их сводим к одной палитре.

| Файл | Экран | Маршрут | Что на экране |
|---|---|---|---|
| `3` | Mijozlar (главная) | `/customers` | шапка с выбором магазина, градиентная карточка «Jami nasiyalar» (сумма, активных клиентов, просрочено), поиск с микрофоном, чипы Hammasi/Qarzdorlar/Yaqinda to'laydiganlar, список клиентов с аватаром-инициалами, тегом, телефоном, бейджем срока, балансом, FAB «+» |
| `1` | Mijoz profili | `/customers/:id` | аватар, имя, тег, телефон, кнопка звонка; тёмная карточка баланса с бейджем просрочки, последний долг/оплата; вкладки Tarix (Ledger) / Ochiq qarzlar; список операций (тип, дата, кто записал, сумма, комментарий); две кнопки снизу «Qarz yozish» / «To'lov olish» |
| `2` | Yangi operatsiya | bottom sheet на `/customers/:id` | чип клиента, переключатель Qarz/To'lov, крупное поле суммы с чипами +50 000/+100 000/+500 000/To'liq summa, срок 3 kun/1 hafta/1 oy/Oy oxirigacha/Boshqa sana с датой, поле комментария с кнопкой «Ovozli yozish», кнопка «Tasdiqlash va saqlash (сумма)», «Bekor qilish» |
| `4` | Qarzlar monitoringi | `/debts` | donut «Jami qarz» (просрочено / 3 дня / в срок), три сводные строки, чипы фильтра, секции «Muddati o'tgan (qizil zona)» с днями просрочки и кнопкой «Eslatma yuborish» |
| `5` | Hisobotlar | `/reports` | период Bu oy/O'tgan oy/Chorak/Yillik, карточка «Jami yig'ilgan to'lovlar» с дельтой, «Yangi qarzlar» и «Qaytarish darajasi», столбчатый график «Pul oqimi dinamikasi» (долги vs оплаты по неделям), «Eng yirik qarzdorlar», кнопки «Excel» и «Telegram orqali jo'natish» |
| `6` | Sozlamalar | `/settings` | карточка магазина + «Do'konni o'zgartirish», срок по умолчанию 15/30/45, время напоминаний, уровень напоминаний, валюта, список сотрудников с ролями + «Yangi xodim taklif qilish», тумблеры уведомлений, язык Latin/Кирилл/Русский, Telegram Bot, PIN/Face ID, экспорт Excel, аудит, «Do'kondan chiqish» |

**Токены (`miniapp/src/styles/tokens.css`, Tailwind `theme.extend`):**
- primary `#7C3AED`, primary-light `#8B5CF6`, primary-soft `#EDE9FE`, primary-tint `#F5F3FF`; градиент шапки `#7C3AED → #A78BFA`
- bg `#F7F8FC`, card `#FFFFFF`, dark-card `#18191E`, text `#1E293B`, muted `#64748B`
- success `#10B981` (оплата), warning `#F59E0B` (≤3 дня), danger `#EF4444` (просрочка)
- радиусы 16–24px, тени `soft`/`card`/`elevated` из `1.html`; шрифт Plus Jakarta Sans (fallback системный); иконки Material Symbols Outlined (self-host woff2, не CDN)
- тёмная тема: те же токены переопределяются из Telegram `themeParams` (`bg_color`, `secondary_bg_color`, `text_color`, `hint_color`, `button_color`)

**Унификация:** таббар везде 4 вкладки Mijozlar / Qarzlar / Hisobotlar / Sozlamalar (на `5.png` пять вкладок, убираем «Daftar»). Шапка с выбором магазина одна на всех вкладках. Строки UI в дизайне на uz-Latn, выносим в `uz-Latn/translation.json` как базовую локаль, переводим на uz-Cyrl и ru.

**Компоненты для `miniapp/src/components/`:** `ShopHeader`, `BalanceHero`, `SearchBar`, `FilterChips`, `CustomerRow`, `Avatar`, `DueBadge`, `LedgerItem`, `SegmentedTabs`, `AmountInput` (+чипы), `DuePicker`, `BottomSheet`, `DonutChart`, `BarChart`, `StatCard`, `SettingRow`, `Toggle`, `MemberRow`, `BottomTabs`, `PrimaryButton`, `FAB`.

## Фазы и проверка

**Фаза 0 — Окружение и скаффолд.** Установить .NET 10 SDK (`sudo apt install dotnet-sdk-10.0`, пакет есть в репозитории Ubuntu 26.04) и Docker (`sudo apt`, потребуется подтверждение пользователя). Целевой фреймворк `net10.0`, при выходе новых версий .NET обновляем без смены мажора LTS. `dotnet new sln` + проекты, Vite, Tailwind, ESLint/Prettier, `docker-compose.dev.yml` (postgres 16 + init-скрипт расширений), `.env.example` (`BOT_TOKEN, BOT_USERNAME, BOT_WEBHOOK_SECRET, BOT_MODE, PUBLIC_URL, MINIAPP_URL, GROQ_API_KEY, JWT_SECRET, POSTGRES_PASSWORD, DOMAIN`). Проверка: `dotnet build`, `dotnet test`, `docker compose up -d`, `curl /health`.

**Фаза 1 — Domain, БД, тенантность.** Сущности, DbContext, миграции, индексы, views; `UzTranslit`, `SearchKey`, `UzNumberParser`, `DuePhraseParser`, `AllocationRecalculator`; `CustomerService.Search/Resolve`. Проверка: unit-тесты (≥40 кейсов чисел: «550 ming», «besh yuz ellik ming», «пятьсот тысяч», «1,5 mln»; транслит «Ахмад ака»→«ahmad aka»; FIFO: переплата, удаление оплаты, правка суммы, target_debt); интеграционные на Testcontainers: изоляция тенантов, нечёткий поиск, пересчёт после soft delete.

**Фаза 2 — Бот-фундамент.** Polling в dev / webhook в prod, `UpdateDispatcher`, `conversation_states`, `/start` (+invite), мастер магазина, `/menu`, `/shops`, `/language`, `/invite`, строки в 3 локалях. Проверка: два Telegram-аккаунта: создать магазин, пригласить, вступить, сменить язык; строки в БД.

**Фаза 3 — Сервисы + REST + auth.** `LedgerService`, `ShopService`, `MembershipService`, `ReportService`, валидаторы, контроллеры, `TelegramInitDataValidator`, JWT, `TenantMiddleware`, политики. Проверка: `WebApplicationFactory`: HMAC ±, worker DELETE чужого → 403, worker свою в 10 мин → ok, owner delete soft + пересчёт, пагинация.

**Фаза 4 — Голос и текст.** `GroqSpeechToText`, `GroqEntryParser` + `RegexEntryParser` + `CompositeEntryParser`, `VoiceHandler`, `TextHandler`, `pending_entries`, карточка со всеми колбэками, создание клиента с тегом, чек, удаление, свип. Проверка: реальные голосовые uz-Latn / uz-Cyrl / ru / mix; два «Ahmad aka»; TTL 1 мин; двойное подтверждение; golden-файл `tests/parser-cases.jsonl` (≥60 кейсов) для regex, LLM-тест при наличии `GROQ_API_KEY`; логировать латентность Groq.

**Фаза 5 — Mini App.** Токены и компоненты из раздела «Дизайн»; каждый экран сверяется с `design/N.png` бок о бок. SDK init, `useAuth`, `ShopProvider`, Query, i18n, роутер. Экраны по порядку: Customers → CustomerDetail + AddEntry → Debts → Reports → Staff → Settings → Shops. «Eslatma yuborish» = `shareURL`/`openTelegramLink` с готовым текстом напоминания должнику на языке магазина + кнопка звонка по телефону. «Ovozli yozish» = MediaRecorder (webm/opus) → `POST /api/entries/parse` → заполняет форму; если `MediaRecorder` недоступен, кнопка скрыта и показывается подсказка «отправь голосовое боту». «Excel» = скачивание через `openLink` на `/api/export/...` с JWT в query-токене одноразового действия. «Telegram orqali jo'natish» = бот присылает текстовую сводку за выбранный период в чат пользователя. PIN / Face ID скрыт из настроек до следующей версии. Проверка: `npm run dev` + `cloudflared tunnel` → `MINIAPP_URL` → с телефона (Android/iOS, light/dark) и в браузере с dev-bypass; vitest на `formatUzs`, даты.

**Фаза 6 — Напоминания.** `ReminderService`, `reminder_log`, группировка, получатели, блокировки, догон на старте. Проверка: unit «следующий запуск» около полуночи; интеграционный `RunOnceAsync(date)` дважды → одна запись лога, одна отправка (fake `ITelegramBotClient`); вручную `reminder_hour` на следующую минуту.

**Фаза 7 — Отчёты, экспорт, персонал, i18n.** Excel-экспорт леджера клиента и сводки магазина (ClosedXML), сводка в Telegram, `EXPLAIN` отчётных запросов, `audit_events`, тест паритета ключей локалей (backend + frontend), матрица ролей owner/worker × эндпоинт, визуальная проверка в 3 языках.

**Фаза 8 — Деплой.** `Dockerfile.app` (node build → dotnet publish → aspnet:10.0 + tzdata + wwwroot), `docker-compose.yml` (app, postgres volume, caddy), `Caddyfile`, `Database.Migrate()` по флагу, `backup.sh` nightly pg_dump с ретеншном 14 дней, webhook + секрет, BotFather: menu button, команды. Проверка: `/health`, `getWebhookInfo`, полный smoke с телефона, восстановление бэкапа.

## Риски

- Точность Whisper на узбекском — главная неопределённость, частично снята: числа и ключевые слова читаются после перехода на два прохода с фразами-примерами (проверено на 8 записях одного человека; другие голоса, шум магазина и диалекты не проверялись). Карточка подтверждения обязательна, транскрипты сохраняются для тюнинга, кнопки правки суммы/клиента. Если точности не хватит — специализированный узбекский STT за тем же `ISpeechToText`.
- Лимиты Groq free tier (измерено 2026-10-10): 8000 токенов в минуту и 1000 запросов в сутки на модель. Запрос парсера с прежним промптом на 10 примеров стоил около 1750 токенов (4 записи в минуту); промпт сокращён примерно вдвое, точная цена нового не замерялась. Сверх лимита работает запасная модель, затем regex (golden-файл 70/70). Промпт намеренно короткий (4 примера), болтовня без суммы и без слов «долг/оплата» в LLM не идёт. STT: один повтор на 429/5xx, затем «не удалось распознать, напишите текстом». Если магазинов станет много — платный тариф Groq.
- «Ellik» = 50 или 50 000: никогда не писать без подтверждения, сумма в карточке полными цифрами.
- Не тот активный магазин у мульти-магазинного юзера: имя магазина в каждой карточке и чеке.
- Параллельные записи по одному клиенту: row lock на `customers`.
- Последний owner: защита в сервисе + «передать владение».
- Утечка invite: срок 7 дней, одноразовость, revoke, уведомление владельца.

## Вне MVP

Автоматические уведомления должнику через бота (в MVP только ручной share), PDF-экспорт, PIN / Face ID, общие клиенты между магазинами, супер-админ панель, еженедельный дайджест просрочек (легко добавить через тот же `reminder_log`).

## Прогресс

| Фаза | Статус | Дата | Заметки |
|---|---|---|---|
| 0 — Окружение и скаффолд | ✅ готово | 2026-10-09 | .NET 10.0.112, Docker 29, `backend/Debits.slnx` (5 src + 3 test), `miniapp/` (Vite 8, React 19, Tailwind 4, oxlint, prettier, vitest), `deploy/docker-compose.dev.yml`. `dotnet build/test`, `npm run build/lint/test` зелёные. `docker compose up` проверен вечером. FluentAssertions зафиксирован на 7.2.2 (8.x платный). Коммит `b989e5f` |
| 1 — Домен, БД, тенантность | ✅ готово | 2026-10-09 | Сущности + enum'ы (в БД lowercase-текст), `UzTranslit`, `SearchKey` (x→h, q→k сворачиваются), `UzNumberParser` (uz-Latn/uz-Cyrl/ru, 70+ кейсов), `DuePhraseParser`, `AllocationRecalculator` (FIFO + target_debt + кредит), `CustomerMatcher` (Exact/Ambiguous/None, правило «имя без тега»). `DebitsDbContext`: именованные фильтры `Tenant` и `SoftDelete`, SaveChanges проставляет/проверяет `shop_id` и timestamps. Миграция `Initial` + views. `PostgresCustomerSearch` на `similarity()`. Тесты: 186 Domain, 4 Application, 15 Integration (Testcontainers). Отличия от плана: `IClock` → встроенный `TimeProvider`; `IDebitsDbContext` в Application (ссылка на EF Core); id сущностей — Guid v7, `pending_entries`/`reminder_log`/`audit_events`/`payment_allocations` — bigserial |
| 2 — Бот-фундамент | ✅ готово, проверено вручную с двух аккаунтов 2026-10-10 | 2026-10-09 | `Debits.Bot`: `UpdateDispatcher` (scope на апдейт, upsert юзера, тенант из active_shop), `UpdateRouter`, `ConversationStore` (`conversation_states`, jsonb), мастер магазина (`shop_wizard:name/due/due_custom/lang`), `/start inv_<token>`, `/menu` (WebApp-кнопка, если `MINIAPP_URL`), `/shops` (+ создать новый), `/language`, `/invite` (owner, 7 дней, 1 раз, share-кнопка), `/help`, `/cancel`. Polling в dev, webhook `POST /tg/webhook` с секретом, `BotSetupHostedService` (getMe, setMyCommands uz/ru/default, menu button, webhook on/off). Строки `bot.{uz-Latn,uz-Cyrl,ru}.json` embedded (`WithCulture=false`!), тест паритета ключей. `.env` подхватывается через `DotEnv` (после json, ниже env vars). Логи HttpClient для Telegram отключены (токен в URL). Application: `UserService`, `ShopService`, `MembershipService`. Тесты: 11 новых интеграционных (fake `ITelegramBotClient` через NSubstitute). Запуск с реальным токеном проверен: getMe → @Debitttbot, команды установлены, polling идёт |
| 3 — Сервисы + REST + auth | ✅ готово | 2026-10-10 | Application: `LedgerService` (create/update/soft-delete под `SELECT … FOR UPDATE` клиента + полный пересчёт аллокаций, права owner / автор-worker 10 мин, `CanEdit` в DTO), `CustomerDirectoryService` (список с балансом/просрочкой/«скоро», фильтры и сортировки, карточка с открытыми долгами, create/update с уникальностью `search_key` → 409 `customer.exists`), `DebtService` (сводка overdue / due_soon / on_time + список), `ReportService` (период с дельтой к предыдущему, недельные/месячные корзины, топ должников, дневные сборы; даты в TZ магазина), `MembershipService` (+ list / change role / deactivate, защита последнего owner), `ShopService` (+ get / update с валидацией TZ), `AuditWriter` (`audit_events` пишутся при каждой мутации). Api: `TelegramInitDataValidator` (HMAC, 24 ч), JWT HS256 12 ч (`sub`, `tg`; ключ = SHA256(JWT_SECRET), в Development без секрета — встроенный dev-секрет с warning, в prod — отказ старта), `TenantMiddleware` (`X-Shop-Id` → членство → `TenantContext`), политики `ShopMember` / `OwnerOnly` с ProblemDetails-ответами, `DomainExceptionHandler` (NotFound/Forbidden/Conflict/400 по типу исключения, всегда `code`), FluentValidation через `ValidationFilter` (`code: validation` + `errors`), enum'ы в JSON camelCase, `AppLanguage` как `uz-Latn`/`uz-Cyrl`/`ru`. Dev-bypass: `X-Dev-Telegram-Id` (+`X-Dev-First-Name`) на `POST /api/auth/telegram` только в Development. Отличия от плана: `reports/summary?from&to` вместо `?month`; `GET /api/debts/summary` для hero-карточки; пагинация offset-курсором (сотни клиентов, keyset не нужен); DELETE транзакции возвращает баланс. Тесты: 35 новых (`ApiFixture` = `WebApplicationFactory` + Testcontainers + `FakeTimeProvider`, HMAC ±, dev-bypass, 401/400/403 коды, worker/owner права, soft delete + пересчёт, target debt, пагинация леджера и списка, фильтры, изоляция тенантов, последний owner, инвайты, настройки, отчёты). Всего 251. Грабли: `WebApplicationFactory.ConfigureAppConfiguration` не перекрывает appsettings.*.json при minimal hosting — тестовые настройки идут через `ConfigureTestServices` (DbContext options + `PostConfigure`); Serilog `preserveStaticLogger: true`, иначе второй хост в процессе падает «logger is already frozen»; EF не транслирует Where/OrderBy по полям позиционного record в проекции — сначала анонимный тип, record в конце |
| 4 — Голос и текст | ✅ код готов; распознавание исправлено по первым реальным голосовым, нужна повторная проверка | 2026-10-10 | Application: `RegexEntryParser` (сумма, срок, намерение по ключевым словам, имя = оставшиеся слова, падежные окончания после обращений: «akaga» → «aka»), `CompositeEntryParser` (LLM + regex; при расхождении сумм берётся regex, если он ≥ 1000, и ставится ⚠️; LLM упал → regex), `EntryDraftService` (фраза → черновик с дефолтным сроком магазина и поиском клиента; общий для бота и API). Infrastructure: `GroqSpeechToText` (`whisper-large-v3`, verbose_json, temperature 0, словарь-подсказка, без `language`), `GroqEntryParser` (JSON mode, `reasoning_effort=low`, запасная модель при любой ошибке). Bot: `VoiceHandler` (лимит 120 с, voice и audio), `EntryHandler` — карточка в `pending_entries` (TTL `Bot:PendingTtlMinutes`, по умолчанию 30) с кнопками `pe:{id}:ok / cu / cu:{i} / cu:new / cu:type / tag:skip / due / due:3,7,30,eom,pick / type / amt / back / x / del`; ввод суммы, срока, имени и метки текстом через `conversation_states` (`entry:*`); новый клиент создаётся только при ✅; подтверждение атомарное (`UPDATE … WHERE status = pending`), повторное нажатие — toast; чек с балансом и `[🗑]` (права те же, что в API); карточка привязана к магазину, в котором создана, даже если активный магазин сменили; `PendingEntrySweeper` раз в час. Api: `POST /api/entries/parse` (JSON `{text}` или multipart `audio`) → черновик + кандидаты. Тесты: +107 (всего 358 зелёных и 8 живых по `GROQ_API_KEY` в переменной окружения): golden `tests/parser-cases.jsonl` 70 фраз, композитный парсер, клиенты Groq на заглушке HTTP, 14 сценариев бота, API. Живьём проверено: LLM 7/7 фраз (uz-Latn, uz-Cyrl, ru, числа словами, сроки, падежи), контракт STT синтетическим тоном, латентность LLM 0,5–1 с. Отличия от плана: модели (см. таблицу решений); отдельного `TextHandler` нет — текст идёт в тот же `EntryHandler`; A/B `language=uz` для Whisper не делался (есть настройка `Groq:SttLanguage`). Не проверено: точность Whisper на реальной узбекской речи — на машине нет инструментов для записи звука. **Правка после первой проверки голосом (2026-10-10):** узбекская речь распознавалась плохо. На 8 реальных записях пользователя выяснено: автоопределение Whisper принимает узбекский за казахский/турецкий («елік мін қарсы», «beşyus»); `language=uz` с подсказкой-словарём помогает слабо, а с подсказкой из фраз-примеров читает точно («Ahmad aka besh yuz ellik ming so'm qarz»); имена клиентов в начале узбекской подсказки чинят написание имён; русская речь через `language=uz` даёт мусор, автоопределение читает её хорошо; список имён в русской подсказке заставляет Whisper терять имя в начале фразы; русская подсказка на узбекской записи может дать перевод на русский. Итог: `GroqSpeechToText` делает два прохода параллельно и берёт узбекский, если в нём есть узбекские числа или слова долга (`SoundsUzbek`), иначе русский из автопрохода; третий проход `ru`, если русский приняли за украинский/болгарский. После правки все 8 записей (6 узбекских, 2 русских) читаются верно. Дополнительно: турецкие буквы (`beş` → besh), варианты `elik`, `milyon`, `қарс`, `сумм`; golden-файл 78 фраз. `ISpeechToText.TranscribeAsync` принимает имена клиентов (`EntryDraftService.RecentCustomerNamesAsync`, 30 последних). Цена: два запроса Whisper на голосовое. Инструмент настройки: `DEBITS_VOICE_DIR=… GROQ_API_KEY=… dotnet test --filter Stt_Samples` печатает распознавание и разбор каждой записи из папки. Осталось слабым: имя в русской речи приходит как услышано («Надыр» при клиенте «Надар») — бот покажет выбор клиента |
| 5 — Mini App | ✅ код готов, проверено в браузере; проверка с телефона в Telegram за пользователем | 2026-10-10 | `miniapp/src`: `lib/telegram.ts` (обёртка над официальным `telegram-web-app.js`: ready/expand, тема, BackButton, haptics, confirm, share), `api/client.ts` (JWT, `X-Shop-Id`, ProblemDetails → `ApiError{code}`, повторный вход при 401), `api/queries.ts` (TanStack Query, ключи с id магазина), `auth/Session.tsx` (вход, активный магазин, язык), i18n из `public/locales/*` (190 ключей × 3 языка, плоские ключи, `errors.<код бэкенда>`). Экраны: `/customers` (hero «Jami nasiyalar», поиск, чипы Hammasi/Qarzdorlar/Yaqinda/Arxiv, бесконечный список, FAB нового клиента), `/customers/:id` (профиль, тёмная карточка баланса, вкладки Tarix / Ochiq qarzlar, удаление записи, напоминание и оплата по конкретному долгу, правка и архив клиента), лист «Yangi operatsiya» (тип, сумма с разделителями и чипами, срок с пресетами и датой, выбор долга для оплаты, комментарий, «Ovozli yozish» через MediaRecorder → `/api/entries/parse`), `/debts` (donut, три строки, фильтры, «Eslatma yuborish» = share с текстом на языке магазина, звонок), `/reports` (периоды, сбор с дельтой, новые долги, возвращаемость, столбцы, топ должников), `/settings` (срок, час и получатели напоминаний, язык напоминаний клиенту, язык приложения, переименование, выход из магазина), `/staff` (сотрудники, роли, приглашения), `/shops` (переключение, создание; он же экран для пользователя без магазина). Тёмная тема из `--tg-theme-*`. Бэкенд: добавлен `POST /api/members/leave`. Проверка: `tsc`, oxlint (2 предупреждения fast-refresh), vitest 37 тестов (деньги, даты, имена, текст напоминания, API-клиент, паритет и покрытие ключей локалей), сборка 135 КБ gzip; сценарий в headless Chromium на реальном API и временной базе: онбординг → магазин → клиент → долг → дубль клиента (409 с переводом) → поиск кириллицей → фильтр → удаление → настройки → русский язык, 15 проверок из 15, скриншоты сверены с `design/*.png` в светлой и тёмной теме. Отличия от плана: вместо `@telegram-apps/sdk-react` официальный скрипт Telegram (стабильный API, пакет удалён); иконки `lucide-react` вместо шрифта Material Symbols (не тянем мегабайтный шрифт); Plus Jakarta Sans self-host через `@fontsource-variable` (в шрифте нет базовой кириллицы, русский и узбекская кириллица рисуются системным шрифтом); «1 oy» = 30 дней, как в боте; тумблеры уведомлений, PIN, микрофон в поиске и аудит из дизайна не делались (нет бэкенда); кнопки «Excel» и «Telegram orqali jo'natish» появятся в фазе 7. Не проверено: работа внутри Telegram (BackButton, haptics, share, тема клиента), запись голоса с реального микрофона, iOS/Android |
| 6 — Напоминания | ✅ код готов, тесты зелёные; проверка на живом боте за пользователем | 2026-10-10 | `Debits.Bot/Reminders`: `ReminderSchedule` (локальная дата, за которую пора слать; момент срабатывания в UTC; сон до границы 5 минут), `ReminderService` (`RunDueAsync` по всем магазинам, `RunShopAsync(shopId, date)`), `ReminderMessage` (одно сообщение на магазин на вид: заголовок, магазин, нумерованный список «клиент — остаток · кто записал», итог; больше 30 строк сворачиваются в «и ещё N»), `Hosting/ReminderHostedService` (старт через 5 секунд, дальше каждые 5 минут по границам часов). Текст на языке получателя, кнопка открывает Mini App на `/debts?shop=<id>` (Mini App переключает активный магазин по этому параметру). `reminder_log` пишется до отправки, уникальный индекс не даёт повтора. 403 от Telegram → `notifications_blocked_at`; флаг снимается, когда пользователь снова пишет боту (`MembershipService.MarkReachableAsync` в диспетчере). 429 → одна повторная попытка после `retry_after`. Тесты: 11 на расписание (полночь, час напоминания, неизвестная зона), 7 интеграционных (двойной запуск → одно сообщение и одна строка лога, язык и кнопка, оплаченный и частично оплаченный долг, «только владельцы», до часа напоминания, блокировка и возврат, длинный список). Всего бэкенд 397 тестов. Отличия от плана: долг, записанный после сегодняшнего момента напоминания, в этот день не попадает (иначе бот пинговал бы о записи пятиминутной давности); догон работает до местной полуночи, пропущенные целиком дни не досылаются; лог и отправка не в одной транзакции с Telegram, при сбое сети сообщение теряется, но не дублируется. Не проверено: работа на живом боте по расписанию |
| 7 — Отчёты, экспорт, i18n | ✅ код готов, проверено в браузере; отправка в чат на живом боте за пользователем | 2026-10-10 | Бэкенд: `Application/Export/ExportService` (данные леджера клиента с балансом после каждой записи; данные магазина за период: сводка, все должники, все записи), `Infrastructure/Excel/ExcelExporter` (ClosedXML: 1 лист для клиента, 3 листа для магазина, подписи на языке пользователя из строк бота `xl.*`), `Bot/Reports/ReportSender` (файл для скачивания, файл документом в чат, текстовая сводка в чат; 403 → `bot.blocked`, нет токена → `bot.not_configured`), `Application/Audit/AuditService`. Эндпоинты: `GET /api/export/customers/{id}`, `POST …/send`, `GET /api/export/shop?from&to`, `POST /api/export/shop/send` (только владелец), `POST /api/reports/send`, `GET /api/audit` (только владелец). Экспорт пишется в `audit_events` (`export.customer`, `export.shop`). Миграция `ReportIndex`: индекс `transactions(shop_id, created_at)`. `EXPLAIN ANALYZE` на 350 тыс. записей в 50 магазинах: отчёт за период, открытые долги, напоминания, топ должников, журнал и леджер клиента идут по индексам, самый тяжёлый запрос (все открытые долги магазина с 4000 долгов) около 150 мс. Mini App: кнопки «Excel» и «Telegram orqali jo'natish» в отчётах, кнопка экспорта в профиле клиента, экран `/audit` «Amallar jurnali» из настроек (владелец), 28 новых ключей × 3 языка. Тесты: 5 на экспорт, отправку и аудит, матрица ролей (30 эндпоинтов × владелец и работник, чужие записи, посторонний и аноним). Всего бэкенд 410, Mini App 37. Сценарий в headless Chromium на трёх языках: 19 проверок, скриншоты отчётов, профиля и журнала. Отличия от плана: вместо одноразового токена в ссылке `openLink` файл внутри Telegram присылает бот в чат (токен не попадает в URL, файл легко переслать), в обычном браузере файл скачивается; экспорт магазина доступен только владельцу. Не проверено: отправка файла и сводки в чат на живом боте |
| 8 — Деплой | ⏳ следующая | | Перед деплоем перевыпустить токен бота в BotFather и ключ Groq в console.groq.com: оба проходили через чат |

**Следующий шаг:** применить миграцию к dev-базе (`cd backend && dotnet ef database update -p src/Debits.Infrastructure`), перезапустить API и проверить в Telegram: «Excel» и «Telegram orqali jo'natish» в отчётах, экспорт из профиля клиента, журнал действий в настройках. Затем фаза 8, деплой: Dockerfile (с `tzdata`), раздача собранного Mini App из API и SPA-fallback, webhook, Caddy, бэкапы. Перед деплоем перевыпустить токен бота и ключ Groq.
