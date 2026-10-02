# One-time local setup: creates the "platform" database, saves the connection string
# in user-secrets, applies migrations and loads the placeholder products.

$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
$project = Join-Path $PSScriptRoot "..\src\Platform.Web"

$secure = Read-Host "Password for the Postgres user 'postgres'" -AsSecureString
$password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

$env:PGPASSWORD = $password
try {
    $exists = & $psql -U postgres -h localhost -tAc "select 1 from pg_database where datname = 'platform'"
    if ($LASTEXITCODE -ne 0) { Write-Host "Could not connect to Postgres. Check the password and try again." -ForegroundColor Red; exit 1 }

    if ($exists -ne '1') {
        & $psql -U postgres -h localhost -c "create database platform"
        if ($LASTEXITCODE -ne 0) { Write-Host "Could not create the database." -ForegroundColor Red; exit 1 }
    }
}
finally {
    Remove-Item Env:PGPASSWORD
}

$quoted = "'" + $password.Replace("'", "''") + "'"
dotnet user-secrets set "ConnectionStrings:Platform" "Host=localhost;Database=platform;Username=postgres;Password=$quoted" --project $project | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "Could not save the connection string." -ForegroundColor Red; exit 1 }

dotnet run --project $project -- migrate
if ($LASTEXITCODE -ne 0) { Write-Host "Migration failed." -ForegroundColor Red; exit 1 }

dotnet run --project $project -- seed
if ($LASTEXITCODE -ne 0) { Write-Host "Seeding failed." -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "Setup complete. Start the site with:" -ForegroundColor Green
Write-Host "  dotnet run --project `"$project`""
Write-Host "then open http://localhost:5080 in a browser on this server."
