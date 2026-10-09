# Debits — цифровая «долговая тетрадь» для махаллинских магазинов

## Context

Локальные магазины в махаллях Узбекистана дают товары в долг и записывают это в тетрадь. Найти запись месячной давности долго. Продукт: Telegram-бот + Telegram Mini App. Продавец голосом говорит «Ахмад ака 550 минг қарз», бот распознаёт, показывает карточку подтверждения и записывает в историю клиента. Магазин = тенант, у магазина несколько владельцев и работников. Бот напоминает сотрудникам о долгах за 3 дня до срока и в день срока.

В проекте только папка `design/` с шестью экранами Stitch. На машине есть Node 22, **нет .NET SDK и Docker** — ставим в фазе 0.

## Подтверждённые решения

| Вопрос | Решение |
|---|---|
| Стек | .NET 10 (LTS) Web API (бот внутри того же процесса) + React/Vite/TS Mini App + PostgreSQL 16; VPS + Docker Compose + Caddy (HTTPS) |
| STT | Groq API `whisper-large-v3` (бесплатный лимит), за интерфейсом `ISpeechToText` |
| Парсинг фразы | Groq LLM `llama-3.3-70b-versatile` с JSON-выводом + regex-фолбэк (`UzNumberParser`, `DuePhraseParser`) |
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
GET /api/customers?q&sort&cursor · POST · GET/PATCH /api/customers/{id} · GET /api/customers/{id}/ledger · GET /api/customers/search?q
POST /api/transactions {customerId,type,amount,comment?,dueDate?,targetDebtId?} · PATCH/DELETE /api/transactions/{id}
GET /api/debts?status=open|overdue|due_soon
GET /api/reports/summary?month · top-debtors · collections?from&to · POST /api/reports/send-telegram {period}
POST /api/entries/parse (multipart audio | text) → ParsedEntry + кандидаты клиентов  (голос из Mini App, тот же пайплайн, без записи)
GET /api/export/customers/{id}.xlsx · GET /api/export/shop.xlsx [owner]   (ClosedXML)
POST /tg/webhook (X-Telegram-Bot-Api-Secret-Token) · GET /health
```
Ошибки: ProblemDetails с `code` → ключ i18n.

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

- Точность Whisper на узбекских именах и числах — главная неопределённость. Карточка подтверждения обязательна, транскрипты сохраняются для тюнинга промпта, кнопки правки суммы/клиента.
- Лимиты Groq free tier (429): retry с backoff, фолбэк на `llama-3.1-8b-instant`, regex, «напиши текстом».
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
| 2 — Бот-фундамент | ✅ код готов, ручная проверка с двух аккаунтов за пользователем | 2026-10-09 | `Debits.Bot`: `UpdateDispatcher` (scope на апдейт, upsert юзера, тенант из active_shop), `UpdateRouter`, `ConversationStore` (`conversation_states`, jsonb), мастер магазина (`shop_wizard:name/due/due_custom/lang`), `/start inv_<token>`, `/menu` (WebApp-кнопка, если `MINIAPP_URL`), `/shops` (+ создать новый), `/language`, `/invite` (owner, 7 дней, 1 раз, share-кнопка), `/help`, `/cancel`. Polling в dev, webhook `POST /tg/webhook` с секретом, `BotSetupHostedService` (getMe, setMyCommands uz/ru/default, menu button, webhook on/off). Строки `bot.{uz-Latn,uz-Cyrl,ru}.json` embedded (`WithCulture=false`!), тест паритета ключей. `.env` подхватывается через `DotEnv` (после json, ниже env vars). Логи HttpClient для Telegram отключены (токен в URL). Application: `UserService`, `ShopService`, `MembershipService`. Тесты: 11 новых интеграционных (fake `ITelegramBotClient` через NSubstitute). Запуск с реальным токеном проверен: getMe → @Debitttbot, команды установлены, polling идёт |
| 3 — Сервисы + REST + auth | ⏳ следующая | | |
| 4 — Голос и текст | | | Нужен `GROQ_API_KEY` |
| 5 — Mini App | | | |
| 6 — Напоминания | | | |
| 7 — Отчёты, экспорт, i18n | | | |
| 8 — Деплой | | | Перед деплоем сделать revoke токена в BotFather, он проходил через чат |

**Следующий шаг:** ручная проверка фазы 2 (запустить `dotnet run` в `backend/src/Debits.Api`, с двух аккаунтов: `/start` → мастер → `/invite` → вступить по ссылке → `/shops`, `/language`), затем фаза 3 — `LedgerService`, `ReportService`, REST-контроллеры, `TelegramInitDataValidator`, JWT, `TenantMiddleware`, политики.
