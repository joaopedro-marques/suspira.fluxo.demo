# DemoAgencia - PoC Telegram + IA

![CI/CD](https://github.com/SEU_USUARIO/DemoAgencia/actions/workflows/ci.yml/badge.svg)
![Tests](https://img.shields.io/badge/tests-62-green)
![.NET](https://img.shields.io/badge/.NET-10-purple)

Prova de Conceito (PoC) para validação de agentes de IA operando via Telegram com pipeline multi-agente e orquestração inteligente.

## Stack

- .NET 10 (Worker Service)
- Telegram.Bot (Long Polling)
- Microsoft Semantic Kernel + OpenRouter
- Serilog (logs locais)
- Langfuse (observabilidade LLM)

## Arquitetura

O sistema utiliza um **pipeline multi-agente** com orquestração inteligente:

```
Mensagem Livre → Orquestrador → Estrategista → Produção → Qualidade → Aprovação → Formatador → Resposta
```

- **Orquestrador**: Classifica intenção (fora_contexto | direta | pipeline)
- **Estrategista**: Planeja execução e aprova resultados
- **Produção**: Agentes especializados (Redator, Dev, Editor de Imagens)
- **Qualidade**: Revisa output com controle de refações (máx 2)
- **Formatador**: Formata resposta final para Telegram

## Estrutura

```
src/DemoAgencia.Worker/
  ├── Telegram/          # Cliente Telegram e handlers
  ├── Agentes/           # Loader de agentes .md
  ├── IA/                # PipelineService, OpenRouterService, StreamingService
  └── Observabilidade/   # Serilog e Langfuse

/Assets/
  ├── agentes/           # Definições dos agentes (.md)
  │   ├── orquestrador.md
  │   ├── estrategista.md
  │   ├── qualidade.md
  │   ├── formatador.md
  │   ├── redator.md
  │   ├── dev.md
  │   └── editor_imagens.md
  └── imagens/           # Exemplos para fluxo visual
```

## Execução Local

```bash
# Configurar variáveis de ambiente
export TELEGRAM_BOT_TOKEN="seu-token"
export OPENROUTER_API_KEY="sua-chave"

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
   - `TELEGRAM_BOT_TOKEN` - Token do BotFather
   - `OPENROUTER_API_KEY` - Chave da OpenRouter
   - `LANGFUSE_PUBLIC_KEY` / `LANGFUSE_SECRET_KEY` - Chaves do Langfuse

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
  -e TELEGRAM_BOT_TOKEN="seu-token" \
  -e OPENROUTER_API_KEY="sua-chave" \
  --restart unless-stopped \
  demoagencia
```

## Status

- [x] F1 - Fundação (scaffold, pacotes, Serilog, Dockerfile)
- [x] F2 - Telegram (long polling, handlers, eco)
- [x] F3 - Agentes .md (loader, cache, 3 agentes)
- [x] F4 - SK + OpenRouter (roteamento híbrido, fallback, histórico)
- [x] F5 - Streaming + multimodal (edição progressiva, análise de fotos, geração de imagens)
- [x] F6 - Langfuse (observabilidade, traces de prompts/respostas)
- [x] F7 - Deploy manual (runbook, scripts, checklist)
- [x] F8 - Testes unitários (62 testes, xUnit + Moq + FluentAssertions)
- [x] F9 - CI/CD (GitHub Actions: build, test, docker, security scan)
- [x] F10 - Pipeline multi-agente (orquestrador, estrategista, qualidade, formatador)
- [x] F11 - Editor de imagens (enriquecimento de prompt + geração)

## Testes

```bash
# Rodar todos os testes
dotnet test

# Com coverage
dotnet test --collect:"XPlat Code Coverage"
```

**62 testes** cobrindo: AgenteLoader, HistoricoChat, PipelineService, OpenRouterService, StreamingService, LangfuseInterceptor, TelegramService.

## CI/CD

Pipeline GitHub Actions com 5 jobs: build+test, docker build, validate structure, security scan, summary.

Ver [RUNBOOK.md](RUNBOOK.md) para detalhes.
