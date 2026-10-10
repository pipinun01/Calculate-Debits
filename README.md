# Debits

Цифровая «долговая тетрадь» для махаллинских магазинов: Telegram-бот + Mini App.
План и решения: [PLAN.md](PLAN.md).

## Стек

.NET 10 Web API (бот внутри того же процесса) · React 19 / Vite / TypeScript / Tailwind 4 · PostgreSQL 16 · Groq (Whisper + Llama).

## Разработка

```bash
cp .env.example .env                       # заполнить BOT_TOKEN и т.д.
docker compose -f deploy/docker-compose.dev.yml up -d   # Postgres 16 на :5432
cd backend && dotnet build && dotnet test    # интеграционные тесты поднимают Postgres через Testcontainers (нужен Docker), dev-базу не трогают
cd backend && dotnet ef database update -p src/Debits.Infrastructure   # применить миграции к dev-базе
cd backend/src/Debits.Api && dotnet run    # http://localhost:5080/health, бот стартует в polling, если в .env есть BOT_TOKEN
cd miniapp && npm install && npm run dev   # http://localhost:5173
```

## Бот

`.env` в корне подхватывается автоматически (`BOT_TOKEN`, `BOT_MODE=polling|webhook`, `PUBLIC_URL`, `BOT_WEBHOOK_SECRET`, `MINIAPP_URL`, `GROQ_API_KEY`, `JWT_SECRET`).
В dev бот работает через long polling. В prod `BOT_MODE=webhook`: на старте ставится webhook `PUBLIC_URL/tg/webhook` с секретом.
Команды: `/start` (мастер магазина или вход по `?start=inv_<token>`), `/menu`, `/shops`, `/invite` (владелец), `/language`, `/help`, `/cancel`.
Записи: голосовое или текст («Ahmad aka 550 ming qarz, bir haftaga», «Ахмад ака оплатил 200 тысяч») → карточка подтверждения → после ✅ запись в тетради и чек с балансом. Нужен `GROQ_API_KEY`; без него голос отключён, а текст разбирается regex-парсером.
Модели в `appsettings.json` → `Groq` (`SttModel`, `LlmModel`, `LlmFallbackModel`). Золотой набор фраз для regex-парсера: `backend/tests/parser-cases.jsonl`.
Голос распознаётся двумя проходами Whisper (автоопределение для русского, принудительный узбекский с именами клиентов магазина), выбор по содержимому; причины описаны в `GroqSpeechToText`. Проверить свои записи: `DEBITS_VOICE_DIR=папка GROQ_API_KEY=... dotnet test --filter Stt_Samples --logger "console;verbosity=detailed"`.
Живые тесты Groq запускаются только с переменной окружения: `GROQ_API_KEY=... dotnet test --filter GroqLiveTests` (тратят лимит бесплатного тарифа).
Напоминания: каждый день в час `reminder_hour` магазина (по умолчанию 09:00 Asia/Tashkent) сотрудники получают список долгов со сроком сегодня и отдельно со сроком через 3 дня. Получатели задаются в настройках магазина (все или только владельцы). Каждый долг попадает в `reminder_log` и повторно за тот же день не приходит; долг, записанный после момента напоминания, в этот день не учитывается. Если API был выключен в нужный час, напоминание уйдёт при запуске в тот же день. Код: `backend/src/Debits.Bot/Reminders`.
Строки бота: `backend/src/Debits.Bot/Localization/Resources/bot.{uz-Latn,uz-Cyrl,ru}.json`, набор ключей должен совпадать (есть тест).

## Mini App

```bash
cd backend/src/Debits.Api && dotnet run       # API на :5080
cd miniapp && npm install && npm run dev      # http://localhost:5173, /api проксируется на :5080 (другой адрес: API_URL=...)
```

В обычном браузере (вне Telegram) `npm run dev` входит через dev-заголовок бэкенда. Пользователь берётся из `VITE_DEV_TELEGRAM_ID` / `VITE_DEV_FIRST_NAME` (файл `miniapp/.env.local`) или из `localStorage.dev_tg_id` / `dev_first_name`.
Для телефона нужен HTTPS: `cloudflared tunnel --url http://localhost:5173`, полученный адрес записать в `MINIAPP_URL` и перезапустить API, затем `/menu` в боте.
Экспорт и отчёты: `GET /api/export/customers/{id}` и `GET /api/export/shop?from&to` отдают .xlsx (второй только владельцу), их пары `POST …/send` присылают файл в чат с ботом, `POST /api/reports/send` присылает текстовую сводку за период. Внутри Telegram Mini App всегда шлёт в чат, в браузере скачивает файл. `GET /api/audit` (владелец) отдаёт журнал действий, экран `/audit`.
Экраны: клиенты, профиль клиента с листом новой операции, долги по срокам, отчёты, настройки, сотрудники, магазины. Переводы: `miniapp/public/locales/{uz-Latn,uz-Cyrl,ru}/translation.json` (ключи должны совпадать, есть тест).
Проверки: `npm run build` (tsc + vite), `npm run lint`, `npm test`.

## REST API (Mini App)

1. `POST /api/auth/telegram` `{ "initData": "<initDataRaw>" }` → `{ token, expiresAt, user }`. Подпись Telegram проверяется HMAC-ом с `BOT_TOKEN`, `auth_date` не старше 24 ч. JWT живёт 12 ч, подписан `JWT_SECRET` (в Development без него используется встроенный dev-секрет, в prod приложение не стартует).
2. Дальше `Authorization: Bearer <token>`. Эндпоинты магазина требуют `X-Shop-Id: <guid>` (нет → 400 `shop.required`, не член → 403 `shop.not_member`); `/api/me` и `/api/shops` работают без него.
3. В браузере без Telegram (только Development): `POST /api/auth/telegram` с заголовком `X-Dev-Telegram-Id: 123` (+ `X-Dev-First-Name`).

Ошибки всегда ProblemDetails с полем `code` (`customer.exists`, `transaction.forbidden`, `member.last_owner`, `validation` + `errors`, …) — ключ для i18n в Mini App.
Права: owner — всё; worker — свои записи 10 минут после создания, без настроек/сотрудников/инвайтов.
Список эндпоинтов — в разделе «Mini App» [PLAN.md](PLAN.md).

## Миграции

```bash
cd backend
dotnet tool install -g dotnet-ef                                   # один раз
dotnet ef migrations add <Name> -p src/Debits.Infrastructure -o Persistence/Migrations
dotnet ef database update -p src/Debits.Infrastructure
```

Views `customer_balances` и `open_debts` создаются SQL-ом из `Persistence/Configurations/ViewConfigurations.cs`;
если меняется схема `transactions` или `payment_allocations`, в новой миграции нужно выполнить `Views.DropSql` + `Views.CreateSql`.

## Структура

```
backend/   Debits.slnx: Domain → Application → Infrastructure / Bot → Api; tests/
miniapp/   Vite + React + TS Mini App
deploy/    docker-compose, Dockerfile, Caddy, backup
design/    6 экранов-референсов (png + html)
```
