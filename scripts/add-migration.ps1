# Adds an EF Core migration for BOTH database providers, so SQL Server and MariaDB stay in sync.
# Usage (from the repo root):  .\scripts\add-migration.ps1 AddProductStock
param(
    [Parameter(Mandatory = $true)]
    [string]$Name
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

dotnet tool restore | Out-Null
dotnet build Bulky.sln
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

foreach ($provider in 'SqlServer', 'MariaDb') {
    Write-Host "`n=== $provider ===" -ForegroundColor Cyan
    # Everything after "--" is passed to the app, so it uses this provider at design time
    dotnet ef migrations add $Name --project "Bulky.Migrations.$provider" --startup-project BulkyWeb --no-build -- --DatabaseProvider $provider
    if ($LASTEXITCODE -ne 0) { throw "Adding the $provider migration failed" }
}

Write-Host "`nReview both new migrations before committing them." -ForegroundColor Green
