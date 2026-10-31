# Backlog e critérios de aceite

## Marco 1 — primeiro recorte operacional
1. Solução modular, build reproduzível e migração inicial PostgreSQL.
2. Provisionamento atômico de órgão e administrador por API administrativa protegida.
3. Login Identity, lockout, TOTP, rotação/revogação de refresh token, suspensão e autorização por permissão.
4. Unidades: criação, consulta paginada, alteração com versão e hierarquia isolada.
5. Auditoria transacional, correlação, ProblemDetails, validação e limitação de requisições.
6. Armazenamento privado S3, quarentena e scanner; notificações duráveis SMTP.
7. Compose, CI, health checks, testes de domínio e API com PostgreSQL Testcontainers.

Aceite: build e testes unitários verdes; integração em PostgreSQL comprova provisionamento, login, restrição de permissões, isolamento por consulta/ID/hierarquia/arquivo, concorrência e replay de refresh. Migração aplicável a banco vazio. Não considerar integração verificada se Docker/dependência estiver indisponível.

## Continuação da Fase 1
Entregue como apoio: consultas autenticadas a IBGE, ViaCEP e PNCP, com cache, validação, estado observado e testes. Ver [PUBLIC-INTEGRATIONS](PUBLIC-INTEGRATIONS.md). A consulta PNCP não conclui os módulos Contratos ou Compras.

Convites e recuperação por email com proteção contra enumeração; federação OIDC por tenant; RBAC configurável, delegações temporárias; calendários; políticas de sessões por tenant; quotas e offboarding com exportação controlada; políticas de retenção e ensaio de restauração; observabilidade e alertas operacionais completos. Estes itens não serão sinalizados como concluídos sem execução e testes.

## Fases seguintes
Fase 2: documentos/versionamento e workflow durável primeiro, depois processos, protocolos, comunicações, assinaturas e pareceres (01–08).
Fase 3: solicitações, ouvidoria, e-SIC, portal, prontuário funcional, contratos, WhatsApp/AI e relatórios (09–16).
Fase 4: compras, estoque, patrimônio, frota, projetos, transparência, RH e ponto (17–24).
Fase 5: orçamento, finanças, tributário, saúde, educação, assistência, obras, meio ambiente, defesa civil e legislativo (25–34).
Cada módulo terá contratos, persistência própria, permissões, licença, testes e validação regulatória específica.
