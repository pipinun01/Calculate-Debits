# pg_dump of the same major version as the server, plus curl for the optional copy to Telegram.
FROM postgres:16-alpine
RUN apk add --no-cache curl tzdata
COPY backup.sh /usr/local/bin/backup.sh
RUN chmod +x /usr/local/bin/backup.sh
ENTRYPOINT ["/usr/local/bin/backup.sh"]
