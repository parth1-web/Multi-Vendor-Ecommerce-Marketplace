<#
.SYNOPSIS
  Drops and recreates the marketplace database, so the next API start seeds it from scratch.

.DESCRIPTION
  Useful when the demo data has drifted, or after a change to the seeder. Everything you have
  created locally is lost, which is the point.

  It lives in a file rather than inline in .vscode/tasks.json on purpose. VS Code passes a
  -Command script as a single-quoted argument, so a single quote anywhere inside that script ends
  the wrapper early; the PostgreSQL path and the SQL each contain quotes and a space, so the
  inline version never ran. A -File argument has no such wrapping.

.EXAMPLE
  pwsh scripts/reset-data.ps1

.EXAMPLE
  pwsh scripts/reset-data.ps1 -Database marketplace
#>
[CmdletBinding()]
param(
  [string] $Database = 'marketplace',
  [string] $Server = 'localhost',
  [string] $User = 'postgres',
  [string] $Password = 'postgres'
)

$ErrorActionPreference = 'Stop'

function Find-Psql {
  $roots = @(
    'C:\Program Files\PostgreSQL',
    'C:\Program Files (x86)\PostgreSQL'
  )

  foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root)) {
      continue
    }

    # Highest version first, so a machine with several installs uses the newest. The install
    # directories are named "16", "17", "18", which is a whole number rather than a version
    # string, so they are compared as numbers.
    $found = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
      ForEach-Object {
        $major = 0
        if ([int]::TryParse(($_.Name -replace '\D', ''), [ref]$major)) {
          [pscustomobject]@{ FullName = $_.FullName; Major = $major }
        }
      } |
      Sort-Object Major -Descending |
      ForEach-Object { Join-Path $_.FullName 'bin\psql.exe' } |
      Where-Object { Test-Path -LiteralPath $_ } |
      Select-Object -First 1

    if ($found) {
      return $found
    }
  }

  return $null
}

$psql = Find-Psql
if (-not $psql) {
  throw 'psql.exe was not found under C:\Program Files\PostgreSQL. Install the PostgreSQL client tools, or add psql to the path.'
}

# The password is passed through the environment rather than the command line, so it does not
# show up in the process list.
$env:PGPASSWORD = $Password

Write-Host "Dropping and recreating $Database on $Server." -ForegroundColor Cyan

& $psql -h $Server -U $User -d postgres -v ON_ERROR_STOP=1 `
  -c "DROP DATABASE IF EXISTS $Database WITH (FORCE);" `
  -c "CREATE DATABASE $Database;"

if ($LASTEXITCODE -ne 0) {
  throw "psql exited with $LASTEXITCODE. The database was left as it was."
}

Write-Host "$Database is empty. Start the API and it will migrate and seed it." -ForegroundColor Green
