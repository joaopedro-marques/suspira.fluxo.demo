# DemoAgencia - PoC Telegram + IA

![CI/CD](https://github.com/SEU_USUARIO/DemoAgencia/actions/workflows/ci.yml/badge.svg)
![Tests](https://img.shields.io/badge/tests-239-green)
![.NET](https://img.shields.io/badge/.NET-10-purple)

Prova de Conceito (PoC) para validação de agentes de IA operando via Telegram com loop de orquestração e referências visuais de clientes.

## Stack

- .NET 10 (Worker Service)
- Telegram.Bot (Long Polling)
- Microsoft Semantic Kernel + OpenRouter
- Serilog (logs locais)
- Langfuse (observabilidade LLM)
- Grafana Loki (observabilidade de logs)

## Arquitetura

O sistema utiliza uma pipeline de **pre-flight (intake)** seguida de um **loop de orquestração** (padrão supervisor/hub-and-spoke):

```
Mensagem Livre → PipelinePreFlightService (Intake):
  1. Identificar cliente (determinístico)
  2. Carregar contexto do cliente (referências + análise de imagens)
  3. Refinar pedido (agente Refinador — perguntas de esclarecimento se necessário)
  4. Montar briefing (agente Montador de Briefing — briefing autocontido + assets reservados)
  → OrquestradorLoopService (Loop com max 24 turnos):
    Orquestrador decide: chamar_agente | chamar_ferramenta | responder_direto | fora_contexto | finalizar
    → finalizar aciona QA obrigatório → Resposta Final
  → Pós-criação: anexar assets reservados + zip HTML se aplicável
```

- **Pre-Flight (Intake)**: Pipeline de preparação (identificar cliente, carregar contexto, refinar pedido, montar briefing)
- **Orquestrador**: Loop supervisor que decide ações a cada turno (JSON protocol)
- **Agentes de produção**: Executam tarefas (Redator, Dev, Estrategista, Prompt para Imagens)
- **Agentes pre-flight**: Refinador e Montador de Briefing (internos, sem comando direto)
- **Qualidade**: Revisor crítico independente (obrigatório antes de entregar)
- **Ferramentas**: Registry genérico (gerar_imagem)
- **Referências de clientes**: Texto + imagens analisadas automaticamente quando cliente identificado
- **Pós-criação**: Anexação automática de assets reservados + zip para entregáveis HTML

## Estrutura

```
src/DemoAgencia.Worker/
  ├── Telegram/              # Cliente Telegram (ITelegramGateway) e handlers
  ├── Agentes/               # IAgentesCatalogo + loader de agentes .md
  ├── IA/                    # OrquestradorLoop, OpenRouterService, Ferramentas/
  │   ├── OrquestradorLoop/  # ParserDecisao, GateQualidade, EnriquecedorContextoCliente
  │   └── PreFlight/         # PipelinePreFlightService, ConversaPendenteStore, Parsers
  ├── Referencias/           # IReferenciasCliente (texto + imagens)
  ├── Configuracoes/         # Options pattern (LoopOptions, PreFlightOptions, etc.)
  ├── Seguranca/             # AnonimizadorService, RateLimiterService
  ├── Observabilidade/       # Serilog e Langfuse
  └── Contracts/             # LangfuseTrace, LangfuseTraceContext

/Assets/
  ├── agentes/               # Definições dos agentes (.md)
  └── referencias/           # {cliente}_{nome}.ext (json, html, png, jpg)
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

**239 testes** cobrindo: AgenteLoader, HistoricoChat, OrquestradorLoopService, ParserDecisao, EnriquecedorContextoCliente, FerramentaRegistry, GerarImagemFerramenta, OpenRouterService, StreamingService, LangfuseInterceptor, TelegramService, AnonimizadorService, RateLimiterService, ReferenciaClienteLoader, PipelinePreFlightService, ParserRefinamento, ParserBriefing, ConversaPendenteStore.

## CI/CD

Pipeline GitHub Actions com 5 jobs: build+test, docker build, validate structure, security scan, summary.

Ver [RUNBOOK.md](RUNBOOK.md) para detalhes.
