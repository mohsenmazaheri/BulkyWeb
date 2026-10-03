# ---------- Build stage: the full .NET SDK, only used to compile ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the project files. Docker caches this layer, so packages are only
# downloaded again when a .csproj changes, not on every code change.
COPY global.json ./
COPY BulkyWeb/BulkyWeb.csproj BulkyWeb/
COPY Bulky.DataAccess/Bulky.DataAccess.csproj Bulky.DataAccess/
COPY Bulky.Models/Bulky.Models.csproj Bulky.Models/
COPY Bulky.Utility/Bulky.Utility.csproj Bulky.Utility/
COPY Bulky.Migrations.SqlServer/Bulky.Migrations.SqlServer.csproj Bulky.Migrations.SqlServer/
COPY Bulky.Migrations.MariaDb/Bulky.Migrations.MariaDb.csproj Bulky.Migrations.MariaDb/
RUN dotnet restore BulkyWeb/BulkyWeb.csproj

COPY . .
RUN dotnet publish BulkyWeb/BulkyWeb.csproj --configuration Release --no-restore --output /app/publish

# ---------- Runtime stage: only the ASP.NET runtime and the published app ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Folders the app writes to at runtime. They become Docker volumes (see docker-compose.yml);
# they must belong to the non-root app user, or uploads and key storage fail with "permission denied".
RUN mkdir -p /app/wwwroot/Images/Product /app/keys \
    && chown -R "$APP_UID" /app/wwwroot/Images/Product /app/keys

# Never run as root: if the app were compromised, the attacker would not get root in the container.
# APP_UID is the built-in non-root user of the official .NET images.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/app/keys
EXPOSE 8080

ENTRYPOINT ["dotnet", "BulkyWeb.dll"]
