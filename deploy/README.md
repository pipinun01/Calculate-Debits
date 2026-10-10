# Деплой Debits

Один сервер, один `docker compose`. Образы собираются прямо на сервере, поэтому подходит и обычный процессор, и ARM (Oracle Ampere).

| Контейнер | Что делает | Обязателен |
|---|---|---|
| `app` | бэкенд, бот и собранный Mini App | да |
| `db` | PostgreSQL 16, данные в томе `pgdata` | да |
| `caddy` | HTTPS, сертификат получает и продлевает сам | да |
| `backup` | ежедневный дамп базы | да |
| `stt` | своя модель распознавания узбекской речи | нет, выключен |

Приложение рассчитано на один экземпляр `app`: бот и напоминания нельзя запускать копиями.

## Что нужно заранее

- Сервер с Ubuntu 24.04. Для работы через Groq хватает 2 ГБ памяти. Со своей моделью речи нужно от 4 ГБ и 4 ядра.
- Домен или поддомен, A-запись которого указывает на IP сервера. Без него не будет HTTPS, а без HTTPS Telegram не откроет Mini App и не пришлёт webhook.
- Открытые входящие порты 80 и 443.
- Новый токен бота из BotFather и новый ключ Groq. Старые проходили через переписку, для прода их нужно перевыпустить.

На Oracle Cloud порты закрыты в двух местах. В панели: Networking, Virtual Cloud Networks, сеть, Security List, добавить Ingress для TCP 80 и 443 с источником `0.0.0.0/0`. На самой машине с образом Ubuntu:

```bash
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

## Первый запуск

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker

git clone https://github.com/pipinun01/Calculate-Debits.git debits
cd debits
cp .env.example .env
nano .env
```

Что заполнить в `.env`:

| Переменная | Значение |
|---|---|
| `DOMAIN` | домен без `https://`, например `debits.example.com` |
| `BOT_TOKEN` | токен из BotFather |
| `BOT_WEBHOOK_SECRET` | `openssl rand -hex 32` |
| `JWT_SECRET` | `openssl rand -hex 32` |
| `POSTGRES_PASSWORD` | `openssl rand -hex 24`, задать до первого запуска и потом не менять |
| `GROQ_API_KEY` | ключ из console.groq.com |
| `BACKUP_CHAT_ID` | Telegram ID того, кому слать дампы, можно заполнить позже |

`BOT_MODE`, `PUBLIC_URL` и `MINIAPP_URL` нужны только для разработки, в проде они не читаются.

Запуск:

```bash
./deploy/run.sh up -d --build
```

Первая сборка занимает 5–10 минут. Приложение само применяет миграции базы, ставит webhook и кнопку меню бота.

## Проверка

```bash
./deploy/run.sh ps                 # app и db должны быть healthy
./deploy/run.sh logs --tail 50 app # «Database migrations applied», «Webhook set to …»
curl https://ВАШ_ДОМЕН/health      # Healthy
```

Затем в Telegram отправить боту `/start` и `/menu`, открыть Mini App.

## Обновление

```bash
cd debits && git pull && ./deploy/run.sh up -d --build
```

Данные лежат в томах и обновление переживают. Новые миграции применяются при старте.

## Резервные копии

Контейнер `backup` каждый день в `BACKUP_HOUR` часов по Ташкенту делает дамп в том `backups` и удаляет дампы старше `BACKUP_KEEP_DAYS` дней. Дамп на том же сервере не спасает от потери сервера, поэтому нужна копия снаружи.

Копия в Telegram: записать свой Telegram ID в `BACKUP_CHAT_ID` и перезапустить `backup`. Бот будет присылать каждый дамп файлом. ID видно в базе после первого `/start`:

```bash
./deploy/run.sh exec db psql -U debits -d debits -c "select telegram_user_id, first_name from users"
./deploy/run.sh up -d backup
```

Telegram принимает от бота файлы до 50 МБ. Если дамп вырастет больше, бот пришлёт предупреждение, и копию придётся забирать иначе.

Дамп вручную и список дампов:

```bash
./deploy/run.sh exec backup backup.sh once
./deploy/run.sh exec backup ls -la /backups
```

Восстановление из дампа:

```bash
./deploy/run.sh stop app
./deploy/run.sh exec backup pg_restore --clean --if-exists --no-owner -d debits /backups/ИМЯ_ФАЙЛА.dump
./deploy/run.sh start app
```

Файл из Telegram сначала скопировать в контейнер: `docker cp файл.dump debits-backup-1:/backups/`.

## Своя модель распознавания узбекской речи

По умолчанию всё распознаёт Groq. Контейнер `stt` запускает модель Whisper, дообученную на узбекском, и берёт на себя только узбекскую речь. Русская остаётся на Groq: узбекские модели её не понимают.

Включить: раскомментировать в `.env` строку `COMPOSE_PROFILES=stt` и выполнить `./deploy/run.sh up -d --build`. Выключить: закомментировать её и выполнить `./deploy/run.sh stop stt`.

- При первом запуске модель скачивается с Hugging Face, для модели по умолчанию это около 3 ГБ. Готовность видна в логе: `./deploy/run.sh logs -f stt`, строка `model ready`.
- Пока контейнер не готов или остановлен, приложение само переходит на Groq и перепроверяет его раз в минуту. Ломаться ничего не должно.
- `STT_MODEL` принимает любую модель в формате CTranslate2. `STT_THREADS` ограничивает число ядер.

## Если что-то не так

| Симптом | Куда смотреть |
|---|---|
| Сертификат не выдаётся | `./deploy/run.sh logs caddy`: чаще всего домен ещё не указывает на сервер или закрыт порт 80 |
| `app` перезапускается | `./deploy/run.sh logs app`: неверный `BOT_TOKEN` или нет связи с Telegram при старте |
| Бот молчит | в логе `app` должна быть строка `Webhook set to https://…/tg/webhook` |
| Mini App не открывается | `curl https://ВАШ_ДОМЕН/health`, затем закрыть и открыть чат с ботом: Telegram запоминает старую кнопку меню |
| Не пришло напоминание | в `.env` не нужен часовой пояс, он задан в compose; проверить час напоминаний в настройках магазина |
