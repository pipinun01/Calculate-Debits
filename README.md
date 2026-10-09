# Debits

Цифровая «долговая тетрадь» для махаллинских магазинов: Telegram-бот + Mini App.
План и решения: [PLAN.md](PLAN.md).

## Стек

.NET 10 Web API (бот внутри того же процесса) · React 19 / Vite / TypeScript / Tailwind 4 · PostgreSQL 16 · Groq (Whisper + Llama).

## Разработка

```bash
cp .env.example .env                       # заполнить BOT_TOKEN и т.д.
docker compose -f deploy/docker-compose.dev.yml up -d   # Postgres 16 на :5432
cd backend && dotnet build && dotnet test    # интеграционные тесты поднимают Postgres через Testcontainers (нужен Docker)
cd backend && dotnet ef database update -p src/Debits.Infrastructure   # применить миграции к dev-базе
cd backend/src/Debits.Api && dotnet run    # http://localhost:5080/health
cd miniapp && npm install && npm run dev   # http://localhost:5173
```

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
