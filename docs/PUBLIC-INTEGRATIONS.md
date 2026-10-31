# Integrações com APIs públicas

O backend consulta IBGE, ViaCEP e PNCP por HTTPS. Os endpoints locais exigem JWT de usuário ativo. A credencial `X-Provisioning-Key` não concede acesso a essas rotas. As requisições externas não encaminham JWT, cookies, chave administrativa, arquivos ou dados do tenant.

## Consultas disponíveis

| Endpoint GET | Dados |
|---|---|
| `/api/v1/integrations` | Habilitação e resultado da última consulta por provedor |
| `/api/v1/integrations/ibge/states` | UFs e códigos IBGE |
| `/api/v1/integrations/ibge/states/AM/municipalities` | Municípios de uma UF; aceita sigla em maiúsculas/minúsculas |
| `/api/v1/integrations/ibge/municipalities/1302603` | Município pelo código IBGE de sete dígitos |
| `/api/v1/integrations/viacep/addresses/69005010` | Endereço por CEP; aceita também `69005-010` |
| `/api/v1/integrations/pncp/contracts?publishedFrom=2026-10-01&publishedTo=2026-10-01&page=1&pageSize=10` | Contratos por data de **publicação**, com paginação |

PNCP aceita filtro opcional `organizationCnpj`, sem pontuação, com 14 posições. A validação é de formato, não de existência ou dígitos verificadores. O CNPJ nunca é usado para resolver o tenant. O backend limita o período a 31 dias inclusivos a partir de 2021, a página a 1–10000 e o tamanho a 10–50 registros. Esses limites locais são mais restritos que o máximo do provedor para controlar consumo. As consultas não importam automaticamente contratos para a base interna.

Cada consulta retorna `{ provider, fetchedAt, expiresAt, fromCache, data }`. Estados e municípios usam `ibgeCode`; endereço contém `cep`, `street`, `complement`, `neighborhood`, `city`, `state` e `ibgeCode`. A página PNCP retorna `items`, `total`, `totalPages`, `pageNumber` e `pageSize`; os itens contêm identificação do contrato, órgão, objeto, valores decimais e datas. CPF/documento de fornecedor não é exposto por este adaptador.

## Configuração

Sem credenciais adicionais para estas consultas. Os destinos HTTPS são fixos no código e não podem ser escolhidos por parâmetros HTTP. Redirecionamentos são rejeitados.

Variáveis opcionais (mesmo padrão para `Ibge`, `ViaCep` e `Pncp`):

```text
PublicIntegrations__Ibge__Enabled=true
PublicIntegrations__Ibge__TimeoutSeconds=10
PublicIntegrations__ViaCep__Enabled=true
PublicIntegrations__ViaCep__TimeoutSeconds=10
PublicIntegrations__Pncp__Enabled=true
PublicIntegrations__Pncp__TimeoutSeconds=10
```

Timeout deve estar entre 1 e 30 segundos. Reinicie o processo após alterar a configuração. Desabilitar um provedor também bloqueia seu cache. Os serviços não são necessários para login, gestão de unidades ou inicialização da API.

Há limite de 30 requisições por minuto por IP nessas rotas, além do limite global da API, e no máximo duas consultas externas simultâneas por provedor em cada processo. Não há fila ilimitada nem retries automáticos. O rate limiting é local por instância: produção com múltiplas réplicas precisa de limite compartilhado no gateway. Configure proxies conhecidos antes de confiar no IP do balanceador.

Cache em memória, limitado a 32 MiB: IBGE/ViaCEP por 24 horas, PNCP por cinco minutos. Apenas respostas válidas de dados públicos são compartilhadas entre tenants. Dados privados não entram nesse cache. Erros e resultados inexistentes não são armazenados; conteúdo HTTP é limitado a 2 MiB. Reiniciar a aplicação apaga cache e observações. Não há nova tabela ou migração neste recorte.

## Falhas e estado real

| HTTP | Significado |
|---|---|
| 400 | CEP/UF/código/parâmetros inválidos; provedor não é chamado |
| 401 | Sessão ausente ou inválida |
| 404 | Dado não encontrado no provedor |
| 429 | Limite local da API atingido |
| 502 | Falha de rede, HTTP inesperado ou resposta externa inválida |
| 503 | Provedor desabilitado, capacidade local esgotada ou limite externo atingido |
| 504 | Tempo limite do provedor esgotado |

Falhas de integração retornam ProblemDetails com `provider`, `reason` e `correlationId`, sem corpo bruto, detalhes internos ou credenciais do provedor.

O endpoint de estado não realiza chamadas. `not_checked` significa que nenhuma consulta foi concluída desde o início do processo; `available`/`unavailable` refletem a última consulta, acompanhada dos horários; após cinco minutos a observação aparece como `stale`. `disabled` reflete configuração. Cache válido pode responder mesmo quando a última observação está antiga; consulte `fetchedAt` e `expiresAt`. Estado não é homologação nem garantia de disponibilidade futura.

## Verificação reproduzível

Testes automatizados usam respostas HTTP controladas para validar contratos, timeout, limites, recuperação, ausência de vazamento de credenciais, autorização e erros. Eles não dependem da disponibilidade da internet.

Para executar os adaptadores reais contra os provedores:

```powershell
dotnet run --project tools/CondertechGov.PublicApiProbe -c Release -- 2026-10-01
```

O argumento opcional é uma data de publicação PNCP em `yyyy-MM-dd`; sem argumento usa dois dias antes da data UTC corrente. A ferramenta usa os mesmos clientes, validação, cache e timeout da API. Não precisa de PostgreSQL, JWT ou credenciais de produção. Código de saída zero indica que todas as consultas funcionaram naquela execução; dados, disponibilidade e quantidades podem mudar.

## Fontes e limites do escopo

- [Documentação oficial do IBGE](https://servicodados.ibge.gov.br/api/docs/localidades).
- [Documentação do ViaCEP](https://viacep.com.br/), incluindo CEP inexistente e orientação contra consultas massivas. ViaCEP é um serviço externo de endereçamento, não uma certificação de endereço ou cadastro de cidadão.
- [OpenAPI oficial de consulta PNCP](https://pncp.gov.br/api/consulta/swagger-ui/index.html) e [contrato JSON](https://pncp.gov.br/api/consulta/v3/api-docs).
- [Acesso e credenciamento de integração PNCP](https://pncp.gov.br/manual/pt-br/latest/acesso_ao_pncp/index.html): publicação e alteração exigem um fluxo separado de credenciamento e autenticação.

Este marco não implementa publicação no PNCP, identidade gov.br, assinatura ICP-Brasil, Receita/Serpro ou módulos de negócio. Também não transforma dados publicados em comprovação de regularidade ou conformidade jurídica.
