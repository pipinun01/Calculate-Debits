# Debits: backend + bot + built Mini App in one image. Build context is the repository root:
#   docker build -f deploy/Dockerfile .
# Nothing here is architecture-specific, so the same file builds on x86-64 and on ARM (Oracle Ampere) servers.

FROM node:22-alpine AS web
WORKDIR /src/miniapp
COPY miniapp/package.json miniapp/package-lock.json ./
RUN npm ci
COPY miniapp/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src/backend
# Restore first so that the package layer is reused while only code changes.
COPY backend/Directory.Build.props backend/Directory.Packages.props ./
COPY backend/src/Debits.Domain/Debits.Domain.csproj src/Debits.Domain/
COPY backend/src/Debits.Application/Debits.Application.csproj src/Debits.Application/
COPY backend/src/Debits.Infrastructure/Debits.Infrastructure.csproj src/Debits.Infrastructure/
COPY backend/src/Debits.Bot/Debits.Bot.csproj src/Debits.Bot/
COPY backend/src/Debits.Api/Debits.Api.csproj src/Debits.Api/
RUN dotnet restore src/Debits.Api/Debits.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/Debits.Api/Debits.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# tzdata: shop-local dates and the reminder hour (Asia/Tashkent). curl: the container health check.
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /src/miniapp/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Debits.Api.dll"]
