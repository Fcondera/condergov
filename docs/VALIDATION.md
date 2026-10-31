# Validação do marco inicial

## Atualização — consultas públicas externas (2026-10-19)

Build Release de toda a solução, incluindo a ferramenta de diagnóstico: aprovado, zero avisos e zero erros. Testes atuais: **59 aprovados** (34 unitários/contrato e 25 de integração com API/PostgreSQL), zero falhas e zero ignorados. O último teste acrescentado verifica a capacidade máxima de duas chamadas simultâneas por provedor. Modelo EF sem alterações pendentes; esta entrega não altera o banco.

Imagens Docker de API e worker reconstruídas com sucesso. O contrato de resposta das novas rotas está disponível no OpenAPI e teve o schema JSON verificado pelo teste de API. Os serviços locais permanecem parados; não foi feito deploy remoto.

Os testes HTTP controlados cobrem cache, entradas inválidas, CEP inexistente em ambos os formatos retornados pelo ViaCEP, respostas malformadas/grandes, erros 429/500/redirecionamento, cancelamento/timeout, desabilitação, recuperação, paginação PNCP e autorização. Os testes de API verificam que JWT/chave administrativa não são encaminhados ao serviço externo.

Verificação com os adaptadores reais, sem handlers de teste: `dotnet run --project tools/CondertechGov.PublicApiProbe -c Release -- 2026-10-01` aprovado. IBGE retornou 27 UFs, 62 municípios do AM e o município de Manaus; ViaCEP retornou Manaus para o CEP consultado; PNCP retornou 10 contratos na primeira página para a data de publicação informada. Quantidades descrevem aquela execução, não são fixtures ou garantias futuras. Nenhum dado privado ou credencial de tenant foi enviado.

Documentação e fontes primárias: [PUBLIC-INTEGRATIONS.md](PUBLIC-INTEGRATIONS.md). Publicação PNCP e os módulos de negócio permanecem fora deste recorte.

## Marco inicial — resultados históricos

Verificação local em 2026-10-19, SDK 10.0.402. Build Release: aprovado, zero avisos e zero erros. `dotnet test -c Release`: 32 aprovados, zero falhas e zero ignorados (12 testes unitários/contrato e 20 testes de integração).

`dotnet ef migrations has-pending-model-changes --project src/CondertechGov.Infrastructure --no-build --configuration Release`: nenhuma alteração pendente. Migrações aplicadas pelos testes a PostgreSQL 17 vazio. Consulta NuGet de vulnerabilidades incluindo dependências transitivas: nenhuma vulnerabilidade reportada pelas fontes consultadas. Isso não substitui análise de segurança periódica.

Docker Compose: imagens de API/worker construídas, migrações executadas e ambos os processos inicializados; readiness da API retornou HTTP 200. Os serviços locais foram interrompidos durante as verificações para limitar consumo de memória; volumes preservados. Use `docker compose up --build -d` para iniciar o ambiente novamente.

Smoke test executado contra a imagem final em `http://127.0.0.1:8080`: provisionamento idempotente, login, unidade, upload S3 em quarentena, auditoria, bloqueio de escalada por gestor delegado e logout aprovados. O teste cria tenants locais fictícios, preservados nos volumes de desenvolvimento. O daemon ClamAV ficou fora desse fluxo; não há aprovação de conteúdo em quarentena sem análise real.

## Escopo dos testes
Domínio: contexto de tenant imutável, validação, versão de unidade, hash de tokens.
PostgreSQL real via Testcontainers: migrações, provisionamento transacional e rollback, Identity, TOTP com inscrição/ativação e revogação, políticas, refresh rotation/replay e corrida concorrente, consulta e gravação isoladas, FK e PK compostas, atualização concorrente, idempotência, auditoria e preservação de leitura após suspensão comercial.

Armazenamento: upload pela API e leitura pelo adaptador S3 verificados contra SeaweedFS 4.48 real em container descartável. Bytes persistidos conferidos, acesso anônimo negado, acesso de outro tenant negado e arquivo em quarentena bloqueado. AWS S3 de produção ainda precisa de validação com as credenciais, políticas, criptografia e região escolhidas.

SMTP: worker entregou mensagem ao Mailpit v1.27 real, marcou outbox e não reenviou a mensagem já registrada como entregue. Não foram enviadas mensagens a destinatários externos. SMTP de produção ainda precisa ser configurado e verificado.

Scanner: contrato TCP INSTREAM verificado com servidor de teste (enquadramento, resultado limpo/infectado e indisponibilidade); esses testes não equivalem à análise por um daemon ClamAV com base de assinaturas real. O serviço ClamAV do Compose não teve seu fluxo de análise homologado nesta máquina. Por falta de memória livre, foi interrompido durante a validação. Arquivos permanecem bloqueados até análise real bem-sucedida. Não há alegação de integração governamental verificada.

## Pendências de aceite
Não foi executado deploy AWS, CI remoto, teste de carga, restauração de backup, rotação de chaves em produção, pentest, fluxo de scanner real ou homologação jurídica. Os itens restantes da Fase 1 e todos os módulos de negócio estão no backlog.
