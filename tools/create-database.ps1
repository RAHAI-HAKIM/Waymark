<#
.SYNOPSIS
    Drops and recreates waymark-store.db from the canonical schema.

.DESCRIPTION
    Development convenience. Under decisions.md D-016 the hand-written schema
    creates the database and EF migrations evolve it; this script is the first
    half of that, run by hand.

    It is NOT the store install path. A real install goes through the
    bootstrapper in Waymark.Persistence, which also stamps the migration
    baseline. This script deliberately does not, so a database it creates is a
    scratch database.

.PARAMETER Path
    Where to write the database. Defaults to the location in D-013.

.PARAMETER Force
    Required to overwrite an existing file. Without it the script refuses,
    because the default path is also the real one.

.EXAMPLE
    ./tools/create-database.ps1 -Force

.EXAMPLE
    ./tools/create-database.ps1 -Path "$env:TEMP\scratch.db" -Force
#>
[CmdletBinding()]
param(
    [string] $Path = "$env:ProgramData\Waymark\data\waymark-store.db",
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$schema = Join-Path $PSScriptRoot '..\src\Waymark.Persistence\schema_v7_1.sql' | Resolve-Path
Write-Host "Schema   : $schema"
Write-Host "Database : $Path"

$sqlite = (Get-Command sqlite3 -ErrorAction SilentlyContinue).Source
if (-not $sqlite) {
    throw "sqlite3 is not on PATH. Install it with: winget install SQLite.SQLite"
}

# Note which SQLite this is. The CLI is newer than the SQLCipher build the
# application loads (3.39.2), so DDL that works here can still fail at runtime.
$version = & $sqlite --version
Write-Host "sqlite3  : $version"
Write-Host "           (the application runs SQLite 3.39.2 via SQLCipher - see D-015)" -ForegroundColor DarkGray

if (Test-Path $Path) {
    if (-not $Force) {
        throw "$Path already exists. Re-run with -Force to replace it."
    }

    Write-Host "Removing existing database and its WAL siblings." -ForegroundColor Yellow
    Remove-Item $Path -Force
    Remove-Item "$Path-wal", "$Path-shm" -Force -ErrorAction SilentlyContinue
}

$directory = Split-Path $Path -Parent
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

# sqlite3's .read wants forward slashes. Built with char codes rather than
# a regex replace, which needs an escaped backslash and is easy to get wrong.
$schemaForRead = $schema.Path.Replace([char]92, [char]47)
# The schema's PRAGMAs echo their results; keep them unless something fails.
$readOutput = & $sqlite $Path ".read $schemaForRead" 2>&1
if ($LASTEXITCODE -ne 0) {
    $readOutput | ForEach-Object { Write-Host $_ }
    throw "sqlite3 exited with $LASTEXITCODE. The database may be incomplete."
}

$counts = & $sqlite $Path @"
SELECT (SELECT count(*) FROM sqlite_schema WHERE type='table')   || ' tables, ' ||
       (SELECT count(*) FROM sqlite_schema WHERE type='index')   || ' indexes, ' ||
       (SELECT count(*) FROM sqlite_schema WHERE type='trigger') || ' triggers';
"@
$integrity = & $sqlite $Path "PRAGMA integrity_check;"
$lax = & $sqlite $Path "SELECT count(*) FROM pragma_table_list WHERE schema='main' AND type='table' AND strict=0 AND name NOT LIKE 'sqlite_%';"

Write-Host ""
Write-Host "Created  : $counts"
Write-Host "Integrity: $integrity"

if ($lax -ne '0') {
    Write-Host "WARNING  : $lax table(s) are not STRICT. Money columns are unprotected." -ForegroundColor Red
    exit 1
}

Write-Host "STRICT   : every table" -ForegroundColor Green
