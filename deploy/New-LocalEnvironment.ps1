param([string]$OutputPath = '.env')
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw "$OutputPath já existe. Preserve seus segredos." }
function New-Secret {
    param([int]$Length = 48)
    $bytes = New-Object byte[] $Length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [Convert]::ToBase64String($bytes)
}
$lines = @(
    "POSTGRES_PASSWORD=$(New-Secret)"
    "REDIS_PASSWORD=$(New-Secret)"
    "JWT_KEY=$(New-Secret 80)"
    "PROVISIONING_KEY=$(New-Secret)"
    'S3_ACCESS_KEY=condertech-local'
    "S3_SECRET_KEY=$(New-Secret)"
)
[System.IO.File]::WriteAllLines((Join-Path (Get-Location) $OutputPath), $lines)
Write-Output 'Ambiente local criado. O arquivo .env não deve ser versionado.'
