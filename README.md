# Condertech Gov — backend

Fundação SaaS em C# / ASP.NET Core .NET 10, PostgreSQL e EF Core. Este repositório implementa o primeiro recorte da Fase 1 do ERP, com fluxos reais e persistência. Os 34 módulos de negócio não estão implementados.

Arquitetura e modelo de dados: [ADR-001](docs/ADR-001.md). Ordem de implementação e pendências: [BACKLOG](docs/BACKLOG.md).

Consultas externas disponíveis: **IBGE (UFs e municípios), ViaCEP (endereços) e PNCP (contratos publicados)**. Exigem JWT. Endpoints, configuração, limites e teste real: [integrações públicas](docs/PUBLIC-INTEGRATIONS.md). Essas consultas não implementam os módulos de negócio nem publicação no PNCP.

## Executar com Docker

Requisitos: Docker Desktop com engine Linux ativo; PowerShell para gerar segredos locais.

```powershell
if (-not (Test-Path .env)) { ./deploy/New-LocalEnvironment.ps1 }
docker compose up --build -d
```

API: http://127.0.0.1:8080. OpenAPI em desenvolvimento: http://127.0.0.1:8080/openapi/v1.json. Readiness: `/health/ready`; liveness: `/health/live`. Caixa SMTP local: http://127.0.0.1:8025.

O serviço `migrate` aplica migrações antes da API e do worker. O bucket é privado; arquivos só ficam disponíveis após análise do scanner. O ClamAV pode levar vários minutos para baixar as bases de assinaturas. O worker Hangfire verifica pendências a cada minuto. Nenhuma credencial administrativa está embutida no código; `.env` é ignorado pelo Git.

Este Compose é um ambiente local. PostgreSQL, Redis, S3 e scanner ficam na rede interna; somente API e Mailpit são publicados em localhost. Não publique Mailpit ou a API administrativa em rede externa.

## Executar sem container da API

Instale o SDK indicado em `global.json`. Nesta máquina também foi preparado em `%LOCALAPPDATA%\CondertechGov\dotnet`.

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\CondertechGov\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet restore
dotnet tool restore
# Configure ConnectionStrings__Postgres, Jwt__Key e Platform__ProvisioningKey.
dotnet run --project src/CondertechGov.Api -- --migrate
dotnet run --project src/CondertechGov.Api
```

Conexões e adaptadores são configurados via variáveis de ambiente no Compose; use os mesmos nomes para execução local. O banco precisa estar acessível ao host. Para notificações e análise de arquivos, execute também `CondertechGov.Worker` com a mesma configuração. O worker não precisa receber a chave de provisionamento em produção.

## Primeiro acesso

Leia `PROVISIONING_KEY` do `.env` local para o cabeçalho `X-Provisioning-Key`. Essa credencial é exclusiva do plano administrativo e não concede acesso às unidades, arquivos ou auditoria de um órgão.

`POST /api/v1/admin/tenants`, com `X-Provisioning-Key` e opcionalmente `Idempotency-Key` (UUID):

```json
{
  "slug": "prefeitura-exemplo",
  "name": "Prefeitura de Exemplo",
  "adminEmail": "administrador@example.gov.br",
  "adminPassword": "DefinaUmaSenha!Forte2026",
  "subscriptionExpiresAt": "2027-10-19T23:59:59Z",
  "userLimit": 50,
  "storageLimitBytes": 1073741824
}
```

O provisionamento cria tenant, usuário administrador, unidade raiz e auditoria em uma transação. A senha acima é apenas exemplo de formato. Use senha exclusiva, com pelo menos 12 caracteres, letras maiúsculas e minúsculas, números e símbolos.

`POST /api/v1/auth/login`:

```json
{
  "tenantSlug": "prefeitura-exemplo",
  "email": "administrador@example.gov.br",
  "password": "senha-definida-no-provisionamento",
  "totp": null
}
```

Use `accessToken` no cabeçalho `Authorization: Bearer ...`. JWT dura 10 minutos; refresh token dura 7 dias. `POST /api/v1/auth/refresh` recebe `tenantSlug` e `refreshToken`, devolvendo um novo par. O refresh anterior deixa de funcionar; reutilização revoga a família inteira. Logout revoga a sessão atual, inclusive seu JWT. Usuários suspensos e security stamps alterados são verificados no banco em cada requisição.

## Casos de uso disponíveis

| Recurso | Operações |
|---|---|
| `/api/v1/admin/tenants` | Provisionar; consultar metadados comerciais, sem conteúdo do órgão |
| `/api/v1/admin/tenants/{id}/subscription` | Atualizar vigência e habilitação de escrita |
| `/api/v1/admin/tenants/{id}/licenses/{module}` | Registrar licença de um módulo; não implementa nem habilita endpoints inexistentes |
| `/api/v1/auth/me`, `/logout`, `/refresh` | Consultar sessão, revogar e rotacionar |
| `/api/v1/auth/mfa/setup` | Recebe `password`; retorna chave TOTP e novo par em `tokens`; use o novo access token para confirmar a ativação |
| `/api/v1/auth/mfa/enable` | Recebe `code`; ativa TOTP e revoga sessões do usuário; novo login passa a exigir TOTP |
| `/api/v1/users` | Criar conta com permissões, respeitando limite contratado; enfileira aviso sem senha |
| `/api/v1/users/{id}/suspension` | Suspender/reativar com `suspended`; revoga sessões |
| `/api/v1/organization-units` | Criar e listar unidades hierárquicas |
| `/api/v1/organization-units/{id}` | Consultar e renomear com controle de versão |
| `/api/v1/files` | Upload binário privado, com cota e quarentena |
| `/api/v1/files/{id}`, `/{id}/download` | Metadados e download autenticado após scanner |
| `/api/v1/audit-events` | Auditoria paginada do órgão |

Criar unidade: `{ "code": "FAZENDA", "name": "Secretaria de Fazenda", "parentId": null }`. A API suporta `Idempotency-Key` opcional no provisionamento e criação de unidade. Repetir a mesma chave e dados retorna o mesmo identificador; alterar os dados retorna 409. Corridas concorrentes podem retornar 409: repita com a mesma chave. O fingerprint do provisionamento usa HMAC; rotação da chave administrativa exige planejamento para chaves de idempotência antigas.

Listagem: `?page=1&pageSize=20&search=Fazenda&sort=name` (`sort=code` também disponível). Limite máximo de 100 registros por página. Renomear: `{ "name": "Novo nome", "version": "uuid-retornado-na-consulta" }`; versão desatualizada retorna 409.

Criar conta: `{ "email": "servidor@example.gov.br", "password": "senha-forte", "permissions": ["units.read"] }`. Permissões disponíveis: `units.read`, `units.write`, `users.manage`, `audit.read`, `files.read`, `files.write`. Apenas administradores autorizados podem atribuí-las. Não há cadastro público de servidores.

Um gestor delegado só pode atribuir permissões que possui e suspender contas cujo conjunto de permissões esteja contido no seu próprio conjunto.

Upload usa corpo binário, `Content-Length` de até 10 MiB e cabeçalho `X-File-Name`. Content type de download é `application/octet-stream`, como anexo. O objeto não ganha URL pública e não é buscado a partir de chave enviada pelo cliente. Arquivos rejeitados ou com scanner indisponível continuam bloqueados.

## Verificação

```powershell
dotnet build
dotnet test --logger trx
dotnet ef migrations has-pending-model-changes --project src/CondertechGov.Infrastructure
dotnet ef migrations script --idempotent --project src/CondertechGov.Infrastructure -o deploy/migrations.sql
dotnet list package --vulnerable --include-transitive
./deploy/Smoke-Test.ps1 # Com Compose iniciado; cria um tenant de teste local.
```

Os testes de API exigem Docker e criam PostgreSQL descartável com Testcontainers. Eles não usam EF InMemory para substituir o banco. Consulte [VALIDATION](docs/VALIDATION.md) para resultados e limites da verificação realizada.

## Limites deste marco

Este recorte ainda não é um produto pronto para produção. Convites, recuperação de senha, federação OIDC, RBAC administrável, exportação/offboarding, calendários e módulos 01–34 constam no backlog. Não há integração operacional com ICP-Brasil, PNCP, WhatsApp, IA ou sistemas fiscais, nem declaração de conformidade legal.

Redis está registrado como infraestrutura distribuída, sem cache de conteúdo confidencial. Rate limiting é por IP e por instância; múltiplas réplicas precisam de limite compartilhado no gateway. A API não confia em cabeçalhos de proxy enviados pelo cliente: configure proxies conhecidos antes de publicar atrás de um balanceador.

Notificações usam entrega SMTP pelo menos uma vez: uma falha após envio e antes do commit pode duplicar a mensagem. Nunca use esse canal como execução de decisão administrativa. Há limite de 10 tentativas; falhas precisam de monitoramento e reprocessamento operacional. Análise de arquivos tem o mesmo limite de tentativas. Um objeto enviado ao S3 antes de falha transacional pode ficar órfão: configure reconciliação periódica com o catálogo de arquivos; não apague objetos por prazo sem verificar referências.

Antes de produção: TLS, cofre e rotação de segredos, chaveiro persistente/protegido do ASP.NET Data Protection, criptografia de discos/buckets, credenciais S3 de privilégio mínimo, gateway administrativo restrito, CORS explícito se houver frontend, políticas de retenção e backup com restauração comprovada, limites distribuídos e validação jurídica por requisito. O diretório da plataforma pode gerenciar contratos; não deve ser usado para consultar conteúdo do tenant.
