# Billing

> Operação AWS atual: ciclo completo centralizado em `manage-platform.yml` no repositório principal. Bancos gerenciados (RDS/DocumentDB), Kafka no EKS, segredos e migrations são provisionados pelo fluxo. Instruções antigas de ações manuais/pré-requisitos avulsos abaixo são históricas; seguir o [guia operacional](https://github.com/Maieru/fiap-tech-challenge/blob/main/docs/operacao/plataforma-aws.md). A aplicação remota ainda exige publicação coordenada, transição e bootstrap.

Serviço .NET 10 de cobranças, PostgreSQL exclusivo e Kafka. O gateway é `IPaymentGateway`; `MockPaymentGateway` aprova imediatamente sem chamadas externas. Não há Mercado Pago real, estorno ou cancelamento administrativo após aprovação.

## Fluxo

OS preserva o total e MessageId do diagnóstico como referência do orçamento. Aprovação administrativa retorna 202 e publica `os.pagamento-solicitado` em `oficina.os.billing.requests.v1`. Billing publica `billing.pagamento-confirmado` ou `billing.pagamento-recusado` em `oficina.billing.events.v1`. Só a confirmação faz OS publicar `os.execucao-autorizada`. Recusa aciona cancelamento no Execution e devolução de estoque; a aprovação original permanece no histórico.

Uma cobrança por ordem, valor decimal BRL preservado inclusive zero. A chave de idempotência do gateway é o UUID da ordem. O grupo Billing é `oficina-billing-v1`. Consumidores confirmam offsets após commit e encerram em falha, sem retry de aplicação ou DLQ.

## Pacotes candidatos e build

Os contratos OS 0.2.0 e Billing 0.1.0 devem ser publicados antes dos builds independentes em CI. Não foram publicados automaticamente nesta implementação. Para revisar antes da publicação:

```powershell
./scripts/Restore-Local.ps1 -OsMessagingPackage <caminho-absoluto-do-OS.0.2.0.nupkg>
dotnet build Billing.slnx --no-restore
dotnet test Billing.slnx --no-restore --filter 'TestCategory!=ContainerImage'
docker build -t fiap-billing:local .
```

O Dockerfile aceita candidatos em `artifacts/packages` e restaura do NuGet público quando publicados. O OS também precisa do nupkg Billing 0.1.0 no seu `artifacts/packages` durante a revisão. Não há referências de projetos entre repositórios.

## Execução

Copie `.env.example` para `.env` e preencha senha do banco e JWT. `docker compose up -d` usa a imagem `fiap-billing:local`, API em loopback 8083 e broker 19094, volumes exclusivos. `docker compose down` preserva dados. O Compose integrado do Execution inclui as três APIs com broker compartilhado; preencha também BILLING_DB_PASSWORD e forneça as três imagens prontas.

JWT exige issuer/audience e `Jwt__SigningKey` de pelo menos 32 bytes, exclusivamente por variável de ambiente. Fora do Compose configure `ConnectionStrings__DefaultConnection`, `Kafka__BootstrapServers`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey`. `Database__ApplyMigrations=true` aplica migrations ao iniciar (ambiente de demonstração).

GET autenticados: `/api/billing/v1/cobrancas`, `/api/billing/v1/cobrancas/{id}` e `/api/billing/v1/ordens/{ordemServicoId}/cobranca`. Lista aceita `ordemServicoId`, `situacao`, `pageNumber=1`, `pageSize=10` (máximo 100). Criação somente por Kafka. OpenAPI `/openapi/v1.json` e Scalar `/scalar/v1` em Development/Testing. Health em `/api/billing/v1/health/live` e `/ready`, incluindo banco, consumidor e broker.

## Kubernetes / EKS

As Actions manuais [publish-image.yml e deploy-eks.yml](scripts/delivery/README.md) publicam no ECR e aplicam os manifestos no EKS, em main com CI aprovado. A publicação usa tag por commit e o deploy usa digest; configure as roles OIDC e variáveis conforme o guia.

Os [manifestos e o roteiro manual](k8s/README.md) configuram HTTP interno 8080, HPA de 1 a 3 réplicas, credencial PostgreSQL exclusiva e OTLP ativo para o Collector compartilhado. Namespace/SecretStore pertencem ao Infra. As migrations devem ser aplicadas antes do rollout; `Database__ApplyMigrations=false` nas réplicas. Bancos, Kafka, segredos/IAM, imagens e deploy são etapas externas.

## Falhas e reconciliação

Persistir → publicar → commit não é atômico entre PostgreSQL e Kafka. O gateway também pode ter concluído antes de falhar a transação. O mock é determinístico; uma integração real deverá respeitar a mesma chave de idempotência. Mensagem órfã ou entrega incerta exige verificar produtor, consumidor e log Kafka, corrigir a causa e reiniciar no mesmo grupo. Preservar envelope e chave no reenvio. Não avançar offsets silenciosamente nem presumir que houve rollback externo.

A migração é aditiva e não cobra ordens antigas. Ordens sem referência e total de orçamento não podem iniciar pagamento. Para demonstração use ordens novas; drene autorizações antigas antes de trocar as imagens. Não faça downgrade com Sagas em andamento.

BDD: no repositório Execution, configure OS_TEST_IMAGE, EXECUTION_TEST_IMAGE e BILLING_TEST_IMAGE e rode `dotnet test Distributed.slnx`. Bancos, tópicos e redes dos testes são descartáveis; não usar volumes de desenvolvimento. CI exige cobertura de linhas 80%, imagem, OpenAPI e Sonar (SONAR_TOKEN e SONAR_PROJECT_KEY).

## Organização do código

- `Domain/Entities`, `Enums` e `Interfaces`: cobrança, situação e contrato de consulta.
- `Application/UseCases/Cobrancas/<Operacao>`: caso de uso, interface e command por operação; listagem também tem response próprio. `Abstractions` contém a persistência transacional e `Gateways` contém o contrato de pagamento e seus DTOs.
- `Infrastructure/Persistence`: contexto, entidades técnicas, migrations e repositórios; `Messaging` contém o consumidor Kafka; `Payments` contém o mock do gateway.
- `API/Controllers`: rotas de cobrança delegando aos casos de uso. `Program` compõe o host com `AddApplication` e `AddInfrastructure`.
- Os testes permanecem na pasta `Tests` da solução, organizados por domínio, infraestrutura, integração e imagens dentro do projeto existente.

A [documentação da Saga](https://github.com/Maieru/fiap-tech-challenge/blob/main/docs/arquitetura/saga-pagamento.md) detalha etapas, mensagens e compensação. A [revisão](https://github.com/Maieru/fiap-tech-challenge/blob/main/docs/operacao/revisao-billing-saga.md) registra as lacunas conhecidas e as evidências.
