# Stage 1: Build Vue 3 Frontend
FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY Sanctum.Web/package*.json ./
RUN npm ci
COPY Sanctum.Web/ ./
RUN npm run build

# Stage 2: Build .NET 11 Backend
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS backend-build
WORKDIR /src/backend
COPY NuGet.config ./
COPY Sanctum.Api/*.csproj ./
RUN dotnet restore --configfile NuGet.config
COPY Sanctum.Api/ ./
# Embed compiled Vue SPA into ASP.NET Core wwwroot
COPY --from=frontend-build /src/frontend/dist ./wwwroot
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 3: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app

# Create non-root data directory for SQLite
RUN mkdir -p /app/data && chown -R app:app /app
USER app

COPY --from=backend-build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/sanctum.db"

EXPOSE 8080
ENTRYPOINT ["dotnet", "Sanctum.Api.dll"]
