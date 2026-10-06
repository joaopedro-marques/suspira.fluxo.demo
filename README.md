# DemoAgencia - PoC Telegram + IA

![CI/CD](https://github.com/SEU_USUARIO/DemoAgencia/actions/workflows/ci.yml/badge.svg)
![Tests](https://img.shields.io/badge/tests-182-green)
![.NET](https://img.shields.io/badge/.NET-10-purple)

Prova de Conceito (PoC) para validação de automação de marketing via Telegram com pipeline de email marketing gerada por IA.

## Stack

- .NET 10 (Worker Service)
- Telegram.Bot (Long Polling)
- Microsoft Semantic Kernel + OpenRouter
- Serilog (logs locais)
- Langfuse (observabilidade LLM)
- Grafana Loki (observabilidade de logs)

## Arquitetura

O sistema utiliza um **Router (intake único)** seguido de **Pipelines por canal** (atualmente apenas email):

```
Mensagem → TelegramService:
  ├── /start, /help → respostas fixas
  └── Mensagem livre → RouterService (intake):
        ├── conversa → resposta direta
        ├── esclarecimento → perguntas (store pendente)
        ├── fora_contexto → recusa
        └── produção:
              ├── email → PipelineEmail
              │     ├── StepEstrategiaEmail (retrieval: fase, paleta, temas, mapa, satisfações)
              │     ├── StepMarcaEmail (retrieval: logo, cores, tom)
              │     ├── StepCopyEmail (LLM: assunto, título, corpo, CTA)
              │     ├── StepImagemHero (LLM + API: imagem opcional)
              │     ├── StepTemplateEmail (HTML table-based com slots)
              │     └── StepQaEmail (LLM: aprovado/reprovado, max 2 refações)
              └── fora_contexto (canal/cliente fora do escopo configurável)
  → Entrega: zip com HTML + assets + imagens
```

- **Router**: Classifica mensagens (conversa/esclarecimento/produção/fora_contexto), estrutura brief com campos (objetivo, público, canal, oferta, etc.)
- **PipelineEmail**: Steps ordenados deterministicamente, cada um recebe contexto mínimo
- **Steps**: Retrieval (estratégia, marca), LLM (copy, QA, prompt imagem), Template (HTML slots), API (gerar imagem)
- **QA com retry**: StepQaEmail avalia entregável; se reprovar, volta ao step alvo (copy/hero), max 2 refações
- **Template HTML**: Table-based, CSS inline, ghost tables para Outlook, max-width 600px, CTA bulletproof
- **Observabilidade**: Langfuse traces por step (router, email_estrategia, email_marca, email_copy, email_hero_prompt, email_hero_imagem, email_template, email_qa)

## Estrutura

```
src/DemoAgencia.Worker/
  ├── Telegram/              # Cliente Telegram (ITelegramGateway) e handlers
  ├── IA/                    # Router + Pipelines + OpenRouterService
  │   ├── Router/            # RouterService, RouterParser, Brief
  │   ├── PreFlight/         # ConversaPendenteStore, EstadoPreFlight
  │   └── Pipelines/         # PipelineRunner, IPipelineStep
  │       └── Email/         # StepEstrategiaEmail, StepMarcaEmail, StepCopyEmail,
  │                          # StepImagemHero, StepTemplateEmail, StepQaEmail
  ├── Agentes/               # IAgentesCatalogo + loader de agentes .md
  ├── Referencias/           # IReferenciasCliente (texto + imagens + estratégia)
  ├── Configuracoes/         # Options pattern (PreFlightOptions, OpenRouterOptions, etc.)
  ├── Seguranca/             # AnonimizadorService, RateLimiterService
  ├── Observabilidade/       # Serilog e Langfuse
  └── Contracts/             # LangfuseTrace, LangfuseTraceContext

Assets/
  ├── agentes/               # Agentes em markdown (router.md, redator.md, hero.md, qa.md)
  └── referencias/
      ├── estrategia/        # {cliente}_{tipo}.json (paleta, temas, jornada, mapa, satisfações)
      ├── templates/         # Modelos base de email (com slots)
      └── imagens/           # {cliente}_{tipo}_{nome}.ext (logos, icons, banners)
```

## Execução Local

```bash
# Configurar variáveis de ambiente
export Telegram__BotToken="seu-token"
export OpenRouter__ApiKey="sua-chave"

# Executar
dotnet run --project src/DemoAgencia.Worker/DemoAgencia.Worker.csproj
```

## Deploy

### Preparação

1. Copie `.env.example` para `.env` e preencha as credenciais:
```bash
cp .env.example .env
nano .env
```

2. Configure:
    - `Telegram__BotToken` - Token do BotFather
    - `OpenRouter__ApiKey` - Chave da OpenRouter
    - `Langfuse__PublicKey` / `Langfuse__SecretKey` - Chaves do Langfuse

### Deploy na VM Oracle

```bash
# Opção A: Script automatizado
chmod +x deploy.sh
./deploy.sh

# Opção B: Manual
docker-compose build --no-cache
docker-compose up -d
```

### Validação

```bash
# Executar checklist de validação
chmod +x validate.sh
./validate.sh
```

## Documentação

- **[RUNBOOK.md](RUNBOOK.md)** - Guia completo de deploy e manutenção
- **[CHECKLIST.md](CHECKLIST.md)** - Checklist de aceite E2E
- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** - Arquitetura detalhada
- **[DEVELOPMENT.md](docs/DEVELOPMENT.md)** - Guia de desenvolvimento
- **[FERRAMENTAS.md](docs/FERRAMENTAS.md)** - Ferramentas e serviços

## Docker

```bash
# Build
docker build -t demoagencia .

# Run
docker run -d \
  --name demoagencia \
  -e Telegram__BotToken="seu-token" \
  -e OpenRouter__ApiKey="sua-chave" \
  --restart unless-stopped \
  demoagencia
```

## Testes

```bash
# Rodar todos os testes
dotnet test

# Com coverage
dotnet test --collect:"XPlat Code Coverage"
```

**182 testes** cobrindo: RouterParser, RouterService, StepEstrategiaEmail, StepCopyEmail, StepImagemHero, StepTemplateEmail (11 testes TDD), StepQaEmail, OpenRouterService, OpenRouterPrivacyHandler, OpenRouterReasoningHandler, LangfuseInterceptor, TelegramService, AnonimizadorService, RateLimiterService, ReferenciaClienteLoader, ConversaPendenteStore, TelegramMessageSplitter, TelegramTextFormatter, AgentesLoader, TemplateCatalogo (10 testes TDD).

## CI/CD

Pipeline GitHub Actions com 5 jobs: build+test, docker build, validate structure, security scan, summary.

Ver [RUNBOOK.md](RUNBOOK.md) para detalhes.
