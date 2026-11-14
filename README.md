# FIAP Tech Challenge — Billing

Serviço .NET 10 responsável por cobranças e integração de pagamento. Usa PostgreSQL exclusivo, recebe pedidos pelo Kafka e cria checkout hospedado pela API de Orders do Mercado Pago. Clientes, ordens e autorização de execução pertencem ao OS; catálogo, estoque e execução pertencem ao Execution.

## Pré-requisitos

- Docker com Compose e containers Linux para executar o ambiente e os testes de integração.
- .NET SDK 10 para build e testes pelo host. Os exemplos de configuração usam PowerShell 7.
- Acesso ao NuGet para restaurar dependências. Node.js 22+ é necessário somente para as ferramentas de entrega.
- Credenciais de teste Mercado Pago e uma URL HTTPS acessível para testar pagamentos positivos e reentrega de webhooks.

## Fluxo e limites

OS publica `os.pagamento-solicitado` em `oficina.os.billing.requests.v1`. Billing cria uma cobrança por ordem e publica `billing.pagamento-confirmado` ou `billing.pagamento-recusado` em `oficina.billing.events.v1`. O grupo padrão é `oficina-billing-v1`.

Valores são decimais em BRL, inclusive zero. Valor zero confirma sem chamada externa; valor positivo permanece pendente até um webhook válido confirmar o resultado. Criar checkout ou voltar pelo navegador não confirma pagamento. A chave de idempotência externa e `external_reference` usam o UUID da ordem; o evento de resultado usa o ID da cobrança como MessageId.

Só a confirmação faz OS autorizar Execution. Recusa aciona a compensação e a devolução de estoque, preservando a aprovação administrativa. Não há estorno, nova tentativa comercial após recusa, Outbox, retry de aplicação ou DLQ.

## Build e dependências

Execute na raiz:

```powershell
dotnet restore Billing.slnx
dotnet build Billing.slnx -c Release --no-restore
```

Os contratos OS usados pelo Billing são `FIAP.TechChallenge.OS.Application.Messaging` 0.2.0. O produtor Billing mantém seu contrato 0.1.0 no próprio repositório. As dependências entre serviços são NuGet, sem referências a projetos externos.

Para avaliar um pacote OS candidato da versão exigida antes de disponibilizá-lo no feed:

```powershell
./scripts/Restore-Local.ps1 -OsMessagingPackage '<caminho-absoluto-do-nupkg-OS-0.2.0>'
```

O script copia o pacote para `artifacts/packages` e gera configuração local ignorada pelo Git. O Dockerfile também aceita candidatos nessa pasta. Build com feed local não comprova restore em um runner limpo.

## Execução local isolada

Em um checkout novo, copie o exemplo e preencha o `.env`. Preserve configurações existentes.

```powershell
Copy-Item .env.example .env
docker build -t fiap-billing:local .
docker compose up -d
```

O Compose usa uma imagem pronta: não contém `build` para a API. Recrie a imagem ao alterar o código. `BILLING_IMAGE` permite selecionar outra referência.

| Variável no .env | Uso |
| --- | --- |
| `BILLING_DB_PASSWORD` | Senha do PostgreSQL local |
| `JWT_ISSUER` e `JWT_AUDIENCE` | Iguais aos do OS que emite o token |
| `JWT_SIGNING_KEY` | Chave compartilhada, com pelo menos 32 bytes UTF-8 |
| `MERCADO_PAGO_ACCESS_TOKEN` | Token de teste para pedidos de valor positivo |
| `MERCADO_PAGO_WEBHOOK_SECRET` | Segredo de assinatura dos webhooks |

A API fica em [localhost:8083](http://localhost:8083), Kafka em `127.0.0.1:19094` e o banco só na rede Compose. Há dois tópicos Billing e volumes próprios. Este broker não está ligado ao OS: um teste de negócio completo exige a stack integrada.

```powershell
docker compose ps -a
docker compose logs -f billing-api
docker compose down
```

`down` preserva os dados; `down -v` os exclui. Alterar a senha no .env não atualiza um usuário em volume já inicializado. Sem credenciais Mercado Pago, a API pode iniciar e processar valor zero, mas uma cobrança positiva falha; corrija a causa antes de reiniciar o consumidor.

Para executar pelo SDK, configure `ConnectionStrings__DefaultConnection`, `Kafka__BootstrapServers`, `Jwt__Issuer`, `Jwt__Audience` e `Jwt__SigningKey` no processo. A chave JWT vem de variável de ambiente. O processo não carrega automaticamente o .env do Compose. Para demonstrar migrations no startup, defina `Database__ApplyMigrations=true`; para abrir Scalar, use Development:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/FIAP.TechChallenge.Billing.API --no-launch-profile --urls http://localhost:8083
```

O PostgreSQL deste Compose não publica porta no host. Para esse modo, forneça uma instância acessível pelo host e schema preparado, ou publique a porta em uma configuração local própria.

## Stack integrada

O ponto de entrada com frontend é o [repositório OS](https://github.com/Maieru/fiap-tech-challenge#subir-os-execution-e-billing), com OS, Execution e Billing lado a lado. O complemento `compose.integrated.yaml` do Execution também aceita imagens OS/Billing prontas e repassa as credenciais de pagamento. Não execute stacks que disputem 8081–8083 ou 19094 simultaneamente.

O OS faz cadastro/login em `/api/os/v1/usuarios` e `/api/os/v1/usuarios/login`. Billing apenas valida o JWT; não oferece login próprio.

## HTTP, saúde e autenticação

| Método e rota | Acesso |
| --- | --- |
| GET `/api/billing/v1/cobrancas` | JWT; filtros `ordemServicoId`, `situacao`, `pageNumber` e `pageSize` |
| GET `/api/billing/v1/cobrancas/{id}` | JWT |
| GET `/api/billing/v1/ordens/{ordemServicoId}/cobranca` | JWT |
| POST `/api/billing/v1/webhooks/mercadopago` | Assinatura Mercado Pago; sem JWT |
| GET `/api/billing/v1/health/live` | Público; processo ativo |
| GET `/api/billing/v1/health/ready` | Público; banco, consumidor e broker |

Paginação padrão 1/10, pageSize máximo 100. A criação de cobrança ocorre somente por Kafka. Em Development/Testing, [Scalar](http://localhost:8083/scalar/v1) e [OpenAPI](http://localhost:8083/openapi/v1.json) expõem o contrato.

## Mercado Pago e webhooks

A implementação usa `POST /v1/orders` e `GET /v1/orders/{id}` em `https://api.mercadopago.com/`. A URL de checkout retornada é validada como HTTPS em `www.mercadopago.com.br`. Configure credenciais e comprador de teste conforme o ambiente da aplicação Mercado Pago; não publique tokens ou assinaturas em logs/arquivos versionados.

Cadastre a URL HTTPS `<origem-publica>/api/billing/v1/webhooks/mercadopago` para eventos de order. O handler espera `type=order` e `data.id` na query, além de `x-request-id` e `x-signature`. Um endereço loopback de Compose não é acessível ao provedor; a borda deve preservar esses parâmetros e headers.

O handler valida HMAC-SHA256, consulta a order e confere referência, ID, valor e moeda. Assinatura inválida retorna 401; falha de processamento ou cobrança ainda indisponível retorna 503 para permitir reentrega. `processed/accredited` confirma; `failed` e `canceled` recusam; outros estados aguardam, inclusive `created` sem `status_detail`. Notificações repetidas não alteram uma cobrança concluída. `GatewayPaymentId` guarda o ID da order.

Na AWS, cadastre `MERCADO_PAGO_ACCESS_TOKEN` e `MERCADO_PAGO_WEBHOOK_SECRET` como GitHub Secrets do repositório principal. O fluxo central grava o JSON em `fiap-billing-mercadopago-credentials` via AWS CLI, fora das variáveis/versões de segredo Terraform; External Secrets injeta os valores no pod. Rotação exige novo apply e rollout. Consulte o [guia AWS](https://github.com/Maieru/fiap-tech-challenge/blob/main/docs/operacao/plataforma-aws.md).

## Testes e CI

```powershell
dotnet test Billing.slnx -c Release --no-build --no-restore --filter 'TestCategory!=ContainerImage'
docker build -t fiap-billing:local .
$env:BILLING_TEST_IMAGE = 'fiap-billing:local'
dotnet test tests/FIAP.TechChallenge.Billing.Tests -c Release --no-build --no-restore --filter 'TestCategory=ContainerImage'
```

Integrações usam PostgreSQL e Kafka reais em containers descartáveis; testes do gateway usam fixtures HTTP, sem compra externa. Testes de imagem exigem Docker e a imagem indicada. A suíte distribuída do Execution exige `OS_TEST_IMAGE`, `EXECUTION_TEST_IMAGE` e `BILLING_TEST_IMAGE` e simula o provedor por HTTP/webhook assinado.

CI verifica ferramentas de entrega, build, testes, cobertura mínima de 80%, imagem, OpenAPI e Sonar. Configure `SONAR_TOKEN` e `SONAR_PROJECT_KEY` conforme o workflow. O contrato é publicado por tag `messaging-vX.Y.Z` em main, com `NUGET_USER` e Trusted Publishing; verifique disponibilidade no feed antes de atualizar consumidores.

## Entrega e operação AWS

`manage-platform.yml` do principal chama os workflows reutilizáveis `publish-image.yml` e `deploy-eks.yml`; eles não têm disparo manual independente. A entrega exige CI aprovado e usa SHA explícito e imagem por digest. O [guia de entrega](scripts/delivery/README.md) descreve o contrato.

[Kubernetes](k8s/README.md) usa HTTP interno 8080, HPA de 1 a 3 réplicas e credenciais exclusivas. O fluxo aguarda ExternalSecrets e aplica migrations por Job `--migrate-only` antes do rollout; pods normais usam `Database__ApplyMigrations=false`. O laboratório tem destruição diária de dados; configuração e validação local não comprovam implantação remota.

## Falhas e reconciliação

Persistir → publicar → commit não é atômico entre PostgreSQL e Kafka. Criação externa pode concluir antes de rollback local; a reentrega reutiliza a chave de idempotência. Publicação anterior a falha de commit pode repetir o evento, cujo MessageId permanece estável.

Em falha, consulte `docker compose logs billing-api` ou os logs do pod, corrija dependências/configuração e reinicie no mesmo grupo. Preserve envelope e chave no reenvio, não avance offsets silenciosamente e não presuma rollback do gateway. Uma cobrança 404 logo após aprovação pode ser propagação pendente; falha persistente exige diagnóstico. Ordens sem referência/total de orçamento não iniciam pagamento.

## Código e documentação

`src` contém Domain, Application, Application.Messaging, Infrastructure e API; `tests` contém contratos, unidades, integração e imagem. Infraestrutura implementa persistência, consumidor e gateway; controllers delegam aos casos de uso.

- [Saga e compensação](https://github.com/Maieru/fiap-tech-challenge/blob/main/docs/arquitetura/saga-pagamento.md).
- [Padrão de mensageria](https://github.com/Maieru/fiap-tech-challenge/blob/main/docs/arquitetura/mensageria.md).
