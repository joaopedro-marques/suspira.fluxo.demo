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

O sistema utiliza um **loop de orquestracao** (padrao supervisor/hub-and-spoke):

```
Mensagem Livre → OrquestradorLoopService → Loop (max 8 turnos):
  Orquestrador decide: chamar_agente | chamar_ferramenta | responder_direto | fora_contexto | finalizar
  → finalizar aciona QA obrigatorio → Resposta Final
```

- **Orquestrador**: Loop supervisor que decide acoes a cada turno (JSON protocol)
- **Agentes de producao**: Executam tarefas (Redator, Dev, Estrategista, Prompt para Imagens)
- **Qualidade**: Revisor critico independente (obrigatorio antes de entregar)
- **Ferramentas**: Registry generico (gerar_imagem)

## Estrutura

```
src/DemoAgencia.Worker/
  ├── Telegram/          # Cliente Telegram e handlers
  ├── Agentes/           # Loader de agentes .md
  ├── IA/                # OrquestradorLoopService, OpenRouterService, Ferramentas/
  └── Observabilidade/   # Serilog e Langfuse

/Assets/
  ├── agentes/           # Definicoes dos agentes (.md)
  │   ├── orquestrador.md
  │   ├── qualidade.md
  │   ├── redator.md
  │   ├── dev.md
  │   ├── estrategista.md
  │   └── criador_prompt-imagens.md
  └── imagens/           # Exemplos para fluxo visual
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
   - `Telegram__ChatIdsPermitidos` - IDs de chat permitidos (opcional, vazio = modo demo)
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
- [x] F10 - Loop de orquestrador (supervisor hub-and-spoke, QA obrigatorio, ferramentas)
- [x] F11 - Criador de prompt de imagens (agente de producao)

## Testes

```bash
# Rodar todos os testes
dotnet test

# Com coverage
dotnet test --collect:"XPlat Code Coverage"
```

**85 testes** cobrindo: AgenteLoader, HistoricoChat, OrquestradorLoopService, Ferramentas, OpenRouterService, StreamingService, LangfuseInterceptor, TelegramService.

## CI/CD

Pipeline GitHub Actions com 5 jobs: build+test, docker build, validate structure, security scan, summary.

Ver [RUNBOOK.md](RUNBOOK.md) para detalhes.
