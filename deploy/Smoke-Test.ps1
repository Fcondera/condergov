param([string]$BaseUri = 'http://127.0.0.1:8080', [string]$EnvFile = '.env')
$ErrorActionPreference = 'Stop'
$settings = @{}
foreach ($line in Get-Content -LiteralPath $EnvFile) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
if (-not $settings['PROVISIONING_KEY']) { throw 'Chave administrativa local ausente.' }
$slug = 'smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$password = 'Aa1!' + [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
$adminHeaders = @{ 'X-Provisioning-Key' = $settings['PROVISIONING_KEY']; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
$provision = @{
    slug = $slug; name = 'Órgão de teste automatizado'; adminEmail = 'smoke@example.gov.br'; adminPassword = $password
    subscriptionExpiresAt = [DateTimeOffset]::UtcNow.AddDays(30).ToString('O')
} | ConvertTo-Json
$tenant = Invoke-RestMethod "$BaseUri/api/v1/admin/tenants" -Method Post -Headers $adminHeaders -ContentType application/json -Body ([Text.Encoding]::UTF8.GetBytes($provision))
$same = Invoke-RestMethod "$BaseUri/api/v1/admin/tenants" -Method Post -Headers $adminHeaders -ContentType application/json -Body ([Text.Encoding]::UTF8.GetBytes($provision))
if ($same.id -ne $tenant.id) { throw 'Idempotência inválida.' }
$login = @{ tenantSlug = $slug; email = 'smoke@example.gov.br'; password = $password } | ConvertTo-Json
$session = Invoke-RestMethod "$BaseUri/api/v1/auth/login" -Method Post -ContentType application/json -Body $login
$headers = @{ Authorization = "Bearer $($session.accessToken)" }
$unit = Invoke-RestMethod "$BaseUri/api/v1/organization-units" -Method Post -Headers $headers -ContentType application/json -Body '{"code":"TEST","name":"Unidade de teste"}'
$fileHeaders = @{ Authorization = $headers.Authorization; 'X-File-Name' = 'smoke.txt' }
$file = Invoke-RestMethod "$BaseUri/api/v1/files" -Method Post -Headers $fileHeaders -ContentType application/octet-stream -Body ([Text.Encoding]::UTF8.GetBytes('Conteudo institucional de teste.'))
if ($file.status -ne 'quarantined') { throw 'Upload deve iniciar em quarentena.' }
$audit = Invoke-RestMethod "$BaseUri/api/v1/audit-events?pageSize=100" -Headers $headers
if ($audit.items.action -notcontains 'file.upload') { throw 'Auditoria de upload ausente.' }
$delegation = @{ email = 'manager@example.gov.br'; password = $password; permissions = @('users.manage') } | ConvertTo-Json
Invoke-RestMethod "$BaseUri/api/v1/users" -Method Post -Headers $headers -ContentType application/json -Body $delegation | Out-Null
$delegatedLogin = @{ tenantSlug = $slug; email = 'manager@example.gov.br'; password = $password } | ConvertTo-Json
$delegated = Invoke-RestMethod "$BaseUri/api/v1/auth/login" -Method Post -ContentType application/json -Body $delegatedLogin
$escalation = @{ email = 'escalated@example.gov.br'; password = $password; permissions = @('files.read') } | ConvertTo-Json
try {
    Invoke-RestMethod "$BaseUri/api/v1/users" -Method Post -Headers @{ Authorization = "Bearer $($delegated.accessToken)" } -ContentType application/json -Body $escalation | Out-Null
    throw 'Gestor delegado conseguiu elevar permissões.'
} catch {
    if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 403) { throw }
}
Invoke-RestMethod "$BaseUri/api/v1/auth/logout" -Method Post -Headers $headers | Out-Null
Write-Output "Smoke test aprovado: provisionamento idempotente, login, unidade, upload S3, auditoria, bloqueio de escalada de permissões e logout. Tenant de teste: $($tenant.id)."
