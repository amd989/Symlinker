<#
.SYNOPSIS
Sends a Pushover notification from the release workflow.

.DESCRIPTION
Reads the PUSHOVER_APP_TOKEN and PUSHOVER_USER_KEY environment variables (repository secrets).
Without them it only logs a notice. A failed notification logs a warning and never fails the build.
#>
param(
    [Parameter(Mandatory)] [string] $Title,
    [Parameter(Mandatory)] [string] $Message,
    [string] $Url,
    [string] $UrlTitle,
    # 1 is high priority: it gets past quiet hours and stays highlighted until seen.
    [ValidateRange(-2, 1)] [int] $Priority = 0
)

if (-not $env:PUSHOVER_APP_TOKEN -or -not $env:PUSHOVER_USER_KEY) {
    Write-Host "::notice title=Pushover::No PUSHOVER_APP_TOKEN or PUSHOVER_USER_KEY secret, so no notification: $Title"
    return
}

$body = @{
    token    = $env:PUSHOVER_APP_TOKEN
    user     = $env:PUSHOVER_USER_KEY
    title    = $Title
    message  = $Message
    priority = $Priority
}
if ($Url) { $body.url = $Url }
if ($UrlTitle) { $body.url_title = $UrlTitle }

try {
    Invoke-RestMethod -Method Post -Uri 'https://api.pushover.net/1/messages.json' -Body $body | Out-Null
    Write-Host "Sent Pushover notification: $Title"
}
catch {
    Write-Host "::warning title=Pushover::Couldn't send the notification ($($_.Exception.Message))."
}
