# Kubernetes — Billing

Manifestos da aplicação no namespace `fiap-billing`. A entrega é chamada por manage-platform.yml do principal por meio de deploy-eks.yml, com SHA de origem, digest, broker e issuer/audience explícitos. HTTP interno usa 8080; a entrada pública passa por API Gateway, ALB e frontend/Nginx.

## Responsabilidades e pré-requisitos

- Infra cria namespace, SecretStore aws-secrets-store, role ESO/RBAC, CAs e Collector. CRDs External Secrets e Metrics Server devem estar disponíveis.
- DB/Infra preparam o banco: PostgreSQL fiap_billing no RDS, com usuário exclusivo. A API recebe somente a conexão e o JWT compartilhado, sem segredos de outros bancos.
- CA somente leitura: aws-database-ca em /certs/global-bundle.pem. O certificado/hostname precisa corresponder à conexão.
- Kafka e os tópicos são criados pelo estágio messaging. A imagem deve existir no ECR e ter CI aprovado no SHA da entrega.

## Arquivos e entrega

| Arquivo | Uso |
| --- | --- |
| config-maps.yaml | Ambiente Production, broker, JWT público e OTLP |
| external-secrets.yaml | Referências às credenciais e chave JWT |
| deployment.yaml | Imagem, probes, limites e mounts de CA |
| services.yaml | Service ClusterIP |
| hpa.yaml | 1 a 3 réplicas; CPU alvo 70% |

Os YAMLs contêm placeholders intencionais. [scripts/delivery](../scripts/delivery/README.md) renderiza cópias temporárias, valida namespace/digest, executa dry-run e aguarda nova sincronização de ExternalSecrets. Não aplique os arquivos fonte diretamente nem grave credenciais em ConfigMaps.

O Job --migrate-only aplica migrations antes do rollout. Database__ApplyMigrations=false nos pods evita migrations concorrentes. O Job precisa de conexão e CA, sem iniciar HTTP ou consumidor.

## Saúde e diagnóstico

```powershell
kubectl -n fiap-billing get pods,services,hpa
kubectl -n fiap-billing get secretstores,externalsecrets
kubectl -n fiap-billing rollout status deployment/fiap-billing-deployment --timeout=360s
kubectl -n fiap-billing logs deployment/fiap-billing-deployment --tail=100
```

Confirme o contexto Kubernetes antes dos comandos. Liveness usa /api/billing/v1/health/live e readiness /api/billing/v1/health/ready. Readiness depende de banco/consumidor/broker; liveness não deve reiniciar a API por indisponibilidade transitória de uma dependência. Falhas de processamento podem encerrar o host e exigem reconciliação.

OpenTelemetry envia gRPC a http://otel-collector.fiap-observability.svc:4317 com nome oficina-billing. HPA exige capacidade nos nós; uma partição Kafka limita o paralelismo do consumidor. Mudanças de Secret/ConfigMap consumido por variável precisam de rollout; a entrega faz isso pela anotação da execução.

## Credenciais Mercado Pago

external-secrets.yaml sincroniza também fiap-billing-mercadopago-credentials. O principal recebe MERCADO_PAGO_ACCESS_TOKEN e MERCADO_PAGO_WEBHOOK_SECRET como GitHub Secrets; o ciclo grava os valores fora do Terraform e injeta MercadoPago__AccessToken/MercadoPago__WebhookSecret no pod. Atualize os GitHub Secrets e execute novo apply para gravar/sincronizar e renovar pods. Webhook positivo exige URL HTTPS pública e assinatura válida.

O [guia operacional](https://github.com/Maieru/fiap-tech-challenge/blob/main/docs/operacao/plataforma-aws.md) descreve variables/secrets, criação, destruição diária e limites de retenção. Manifestos válidos não comprovam implantação remota.
