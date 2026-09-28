<#
.SYNOPSIS
  Waits for the API to answer, then opens the site.

.DESCRIPTION
  The full stack starts the API and the site at the same time, so the browser can arrive before
  the API is listening. This polls the health endpoint and opens the site once it answers.

  It lives in a file rather than inline in .vscode/tasks.json on purpose. VS Code passes a
  -Command script as a single-quoted argument, so a single quote anywhere inside that script ends
  the wrapper early; a path containing a space then arrives unquoted and the command fails on the
  space. A -File argument has no such wrapping, so the script can quote its own paths.

.EXAMPLE
  pwsh scripts/wait-for-api.ps1

.EXAMPLE
  pwsh scripts/wait-for-api.ps1 -ApiPort 5000 -SitePort 3000 -TimeoutSeconds 90
#>
[CmdletBinding()]
param(
  [int] $ApiPort = 5000,
  [int] $SitePort = 3000,
  [int] $TimeoutSeconds = 60,
  [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'

$health = "http://localhost:$ApiPort/health"
$site = "http://localhost:$SitePort"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

while ((Get-Date) -lt $deadline) {
  try {
    $null = Invoke-WebRequest -Uri $health -UseBasicParsing -TimeoutSec 2
    Write-Host "The API is up at $health." -ForegroundColor Green

    if (-not $NoBrowser) {
      Start-Process $site
    }
    else {
      Write-Host "Open $site when you are ready."
    }

    exit 0
  }
  catch {
    Start-Sleep -Milliseconds 750
  }
}

Write-Host "The API did not answer at $health within $TimeoutSeconds seconds." -ForegroundColor Red
exit 1
