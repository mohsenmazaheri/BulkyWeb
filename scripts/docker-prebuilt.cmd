@echo off
rem Compiles BulkyWeb on this PC, then starts it in Docker with MariaDB, without NuGet access inside Docker.
rem Use it when "docker compose up --build" fails at "dotnet restore" (e.g. behind a VPN).
rem Run it from the repository root:  scripts\docker-prebuilt.cmd

cd /d "%~dp0.."

if not exist .env (
    echo .env is missing. Create it first:  copy .env.example .env   and set the passwords.
    exit /b 1
)

echo === 1/2 Compiling the app on this PC (dotnet publish)
if exist publish rmdir /s /q publish
dotnet publish BulkyWeb\BulkyWeb.csproj --configuration Release --output publish
if errorlevel 1 (
    echo dotnet publish failed: see the errors above. The .NET 10 SDK must be listed by: dotnet --list-sdks
    exit /b 1
)

echo === 2/2 Building the image and starting app + MariaDB
docker compose -f docker-compose.yml -f docker-compose.prebuilt.yml up --build %*
