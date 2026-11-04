function Invoke-CheckedCommand {
    param(
        [string]$Command,
        [string[]]$CommandArguments,
        [string]$WorkingDirectory = (Split-Path -Parent $PSScriptRoot)
    )
    Push-Location -LiteralPath $WorkingDirectory
    try {
        Write-Host ('Running: {0} {1}' -f $Command, ($CommandArguments -join ' '))
        & $Command @CommandArguments
        if ($LASTEXITCODE -ne 0) {
            throw ('Command failed with exit code {0}: {1} {2}' -f $LASTEXITCODE, $Command, ($CommandArguments -join ' '))
        }
    }
    finally { Pop-Location }
}
