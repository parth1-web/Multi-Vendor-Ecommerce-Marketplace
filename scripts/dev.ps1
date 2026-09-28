<#
.SYNOPSIS
  Starts the whole system: the API with its database, and the site in front of it.

.DESCRIPTION
  The same thing the "Full stack" task in .vscode/tasks.json does, for when you are in a terminal
  rather than in VS Code, or on a machine where VS Code is not the editor. Both halves run in the
  foreground, interleaved, so Ctrl+C stops both.

  Prerequisite, once: PostgreSQL on localhost:5432 with a role postgres/postgres. The API creates
  the marketplace database, applies the migrations and seeds the demo catalogue on first start.

.EXAMPLE
  pwsh scripts/dev.ps1

.EXAMPLE
  pwsh scripts/dev.ps1 -ApiPort 5100 -SitePort 3100
#>
[CmdletBinding()]
param(
  [int] $ApiPort = 5000,
  [int] $SitePort = 3000,
  [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'backend\src\Marketplace.API'
$site = Join-Path $root 'frontend\marketplace-web'

function Assert-Tool($name, $why) {
  if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
    throw "$name is not on the path. $why"
  }
}

Assert-Tool 'dotnet' 'Install the .NET 8 SDK.'
Assert-Tool 'npm' 'Install Node.js 20 or newer.'

# The site reads the API's address from NEXT_PUBLIC_API_URL. If a local .env.local disagrees with
# the port asked for here, the site would talk to an API that is not running, so the file is
# written rather than assumed.
$envFile = Join-Path $site '.env.local'
$apiUrl = "http://localhost:$ApiPort"
$siteUrl = "http://localhost:$SitePort"

@(
  "# Written by scripts/dev.ps1. Delete it to go back to the defaults in code."
  "NEXT_PUBLIC_API_URL=$apiUrl"
  "NEXT_PUBLIC_SITE_URL=$siteUrl"
  "NEXT_PUBLIC_PAYMENT_RETURN_URL=$siteUrl/checkout"
  "NEXT_PUBLIC_SIGNALR_URL=$apiUrl/hubs/marketplace"
) | Set-Content -LiteralPath $envFile -Encoding utf8

Write-Host "API  $apiUrl" -ForegroundColor Cyan
Write-Host "Site $siteUrl" -ForegroundColor Cyan
Write-Host "Waiting for the API, then the site." -ForegroundColor DarkGray
Write-Host "Ctrl+C stops both." -ForegroundColor DarkGray
Write-Host ""

$apiJob = $null
$siteJob = $null

try {
  # Both jobs take what they need as arguments. $using: is for remote jobs, not Start-Job, and
  # relying on it here would start the API on the wrong port and fail quietly.
  $apiJob = Start-Job -Name 'api' -ArgumentList $api, $apiUrl -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    Set-Location (Split-Path -Parent (Split-Path -Parent $project))
    dotnet run --project $project --urls $url
  }

  $siteJob = Start-Job -Name 'site' -ArgumentList $site, $apiUrl, $siteUrl, $SitePort -ScriptBlock {
    param($dir, $api, $site, $port)
    Set-Location $dir
    $env:NEXT_PUBLIC_API_URL = $api
    $env:NEXT_PUBLIC_SITE_URL = $site
    npm run dev -- --port $port
  }

  # Print each job's output as it appears, until interrupted.
  while ($true) {
    $received = Wait-Job -Job $apiJob, $siteJob -Any -Timeout 1 -ErrorAction SilentlyContinue
    if ($received) {
      Receive-Job -Job $received | ForEach-Object {
        $name = if ($_.Name -eq 'api') { 'api ' } else { 'site' }
        Write-Host "$name $_"
      }
    }
    Start-Sleep -Milliseconds 100
  }
}
finally {
  Get-Job | Where-Object { $_.State -eq 'Running' } | Stop-Job -ErrorAction SilentlyContinue
  Get-Job | Remove-Job -Force -ErrorAction SilentlyContinue
  Write-Host ''
  Write-Host 'Both stopped.' -ForegroundColor DarkGray
}
